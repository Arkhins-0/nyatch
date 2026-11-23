// Nyatch: what the Explorer folder sizes code needs from Windhawk, and the DLL's entry points.
//
// Nyatch loads this DLL into explorer.exe and runs NyatchExplorerStart there; NyatchExplorerStop takes
// the hooks out and unloads it again. NyatchExplorerPrepare runs in Nyatch's own process: it finds the
// private Windows functions the code hooks, from Microsoft's public symbols, and caches where they are,
// so Explorer itself never downloads or reads symbol files.
#include "windhawk_api.h"

#include <dbghelp.h>
#include <shlobj.h>
#include <tlhelp32.h>
#include <urlmon.h>

#include <atomic>
#include <cstdio>
#include <cwctype>
#include <fstream>
#include <map>
#include <mutex>
#include <string>

#include "detours.h"

namespace {

HMODULE g_self = nullptr;
std::atomic<bool> g_started{false};
std::mutex g_mutex;

// ---------- Settings: the Explorer.* lines of Nyatch's settings.ini ----------

std::map<std::wstring, std::wstring> g_ini;

std::wstring KnownFolder(REFKNOWNFOLDERID id) {
    PWSTR raw = nullptr;
    std::wstring path;
    if (SUCCEEDED(SHGetKnownFolderPath(id, KF_FLAG_DONT_VERIFY, nullptr, &raw)) && raw) path = raw;
    CoTaskMemFree(raw);
    return path;
}

void LoadIni() {
    g_ini.clear();
    std::ifstream file(KnownFolder(FOLDERID_RoamingAppData) + L"\\Nyatch\\settings.ini", std::ios::binary);
    std::string line;
    while (std::getline(file, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        const size_t eq = line.find('=');
        if (eq == std::string::npos || line.compare(0, 9, "Explorer.") != 0) continue;
        auto wide = [](const std::string& s) {
            const int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0);
            std::wstring w(n > 0 ? n : 0, L'\0');
            if (n > 0) MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), w.data(), n);
            return w;
        };
        g_ini[wide(line.substr(0, eq))] = wide(line.substr(eq + 1));
    }
}

std::wstring Ini(const wchar_t* key) {
    const auto found = g_ini.find(key);
    return found == g_ini.end() ? std::wstring() : found->second;
}

// The code's own setting names and values, from Nyatch's.
std::wstring SettingValue(std::wstring_view name) {
    if (name == L"calculateFolderSizes") {
        const std::wstring v = Ini(L"Explorer.FolderSizes");
        return v == L"shift" ? L"withShiftKey" : v == L"always" ? L"always" : v == L"everything" ? L"everything" : L"disabled";
    }
    if (name == L"sortSizesMixFolders") return Ini(L"Explorer.MixFoldersWhenSorting") == L"true" ? L"1" : L"0";
    if (name == L"disableKbOnlySizes") {
        const std::wstring v = Ini(L"Explorer.SizeUnits");
        return v == L"mbgb" ? L"1" : v == L"kb" ? L"alwaysKb" : L"0";
    }
    if (name == L"useIecTerms") return Ini(L"Explorer.IecUnits") == L"true" ? L"1" : L"0";
    return L"";
}

// ---------- Hooks ----------

struct Hook {
    void** original;
    void* hook;
};
std::vector<Hook> g_hooks;

// Every other thread of the process, so none is caught half-way through a function being patched.
std::vector<HANDLE> SuspendableThreads() {
    std::vector<HANDLE> threads;
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return threads;
    THREADENTRY32 entry = {sizeof(entry)};
    const DWORD process = GetCurrentProcessId(), self = GetCurrentThreadId();
    for (BOOL more = Thread32First(snapshot, &entry); more; more = Thread32Next(snapshot, &entry)) {
        if (entry.th32OwnerProcessID != process || entry.th32ThreadID == self) continue;
        if (HANDLE thread = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_SET_CONTEXT, FALSE, entry.th32ThreadID)) {
            threads.push_back(thread);
        }
    }
    CloseHandle(snapshot);
    return threads;
}

void BeginTransaction(std::vector<HANDLE>& threads) {
    DetourTransactionBegin();
    threads = SuspendableThreads();
    for (HANDLE thread : threads) DetourUpdateThread(thread);
}

void CloseThreads(std::vector<HANDLE>& threads) {
    for (HANDLE thread : threads) CloseHandle(thread);
    threads.clear();
}

// ---------- Symbols ----------

// The PDB that matches a loaded module: its name and the GUID + age that identify it.
bool PdbIdentity(HMODULE module, std::wstring* key, std::wstring* pdbName) {
    auto base = reinterpret_cast<const BYTE*>(module);
    auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    const IMAGE_DATA_DIRECTORY& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
    if (!dir.VirtualAddress || !dir.Size) return false;
    auto entries = reinterpret_cast<const IMAGE_DEBUG_DIRECTORY*>(base + dir.VirtualAddress);
    for (size_t i = 0; i < dir.Size / sizeof(IMAGE_DEBUG_DIRECTORY); ++i) {
        if (entries[i].Type != IMAGE_DEBUG_TYPE_CODEVIEW || !entries[i].AddressOfRawData) continue;
        struct RSDS {
            DWORD signature;
            GUID guid;
            DWORD age;
            char name[1];
        };
        auto cv = reinterpret_cast<const RSDS*>(base + entries[i].AddressOfRawData);
        if (cv->signature != 'SDSR') continue;
        const GUID& g = cv->guid;
        wchar_t text[64] = {};
        swprintf_s(text, L"%08X%04X%04X%02X%02X%02X%02X%02X%02X%02X%02X%X", g.Data1, g.Data2, g.Data3, g.Data4[0], g.Data4[1],
                   g.Data4[2], g.Data4[3], g.Data4[4], g.Data4[5], g.Data4[6], g.Data4[7], cv->age);
        *key = text;
        const char* name = cv->name;
        if (const char* slash = strrchr(name, '\\')) name = slash + 1;
        pdbName->assign(name, name + strlen(name));
        return true;
    }
    return false;
}

std::wstring SymbolsFolder() {
    return KnownFolder(FOLDERID_LocalAppData) + L"\\Nyatch\\Symbols";
}

std::wstring CacheFile(const std::wstring& key) {
    return SymbolsFolder() + L"\\windows.storage-" + key + L".txt";
}

std::wstring Squeeze(std::wstring_view s) {
    std::wstring out;
    for (wchar_t c : s) {
        if (!iswspace(c)) out += c;
    }
    return out;
}

// name -> address in this process, from the cache Nyatch wrote.
std::map<std::wstring, void*> ReadCache(HMODULE module) {
    std::map<std::wstring, void*> found;
    std::wstring key, pdb;
    if (!PdbIdentity(module, &key, &pdb)) return found;
    std::ifstream file(CacheFile(key), std::ios::binary);
    std::string line;
    while (std::getline(file, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        const size_t tab = line.find('\t');
        if (tab == std::string::npos) continue;
        const unsigned long long rva = strtoull(line.substr(0, tab).c_str(), nullptr, 16);
        const std::string name = line.substr(tab + 1);
        const int n = MultiByteToWideChar(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), nullptr, 0);
        std::wstring wide(n > 0 ? n : 0, L'\0');
        if (n > 0) MultiByteToWideChar(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), wide.data(), n);
        found[Squeeze(wide)] = reinterpret_cast<BYTE*>(module) + rva;
    }
    return found;
}

}  // namespace

// ---------- Windhawk's API ----------

int Wh_GetIntSetting(PCWSTR name, ...) {
    return _wtoi(SettingValue(name).c_str());
}

PCWSTR Wh_GetStringSetting(PCWSTR name, ...) {
    const std::wstring value = SettingValue(name);
    auto copy = new wchar_t[value.size() + 1];
    wcscpy_s(copy, value.size() + 1, value.c_str());
    return copy;
}

void Wh_FreeStringSetting(PCWSTR string) {
    delete[] string;
}

BOOL Wh_SetFunctionHook(void* targetFunction, void* hookFunction, void** originalFunction) {
    if (!targetFunction || !hookFunction || !originalFunction) return FALSE;
    *originalFunction = targetFunction;
    if (DetourAttach(originalFunction, hookFunction) != NO_ERROR) {
        *originalFunction = nullptr;
        return FALSE;
    }
    g_hooks.push_back({originalFunction, hookFunction});
    return TRUE;
}

bool WindhawkUtils::HookSymbols(HMODULE module, const SYMBOL_HOOK* symbolHooks, size_t symbolHooksCount) {
    const std::map<std::wstring, void*> cache = ReadCache(module);
    if (cache.empty()) return false;
    for (size_t i = 0; i < symbolHooksCount; ++i) {
        const SYMBOL_HOOK& hook = symbolHooks[i];
        void* address = nullptr;
        for (std::wstring_view symbol : hook.symbols) {
            if (const auto found = cache.find(Squeeze(symbol)); found != cache.end()) {
                address = found->second;
                break;
            }
        }
        if (!address) {
            if (hook.optional) continue;
            return false;
        }
        if (hook.hookFunction) {
            if (!Wh_SetFunctionHook(address, hook.hookFunction, hook.pOriginalFunction)) return false;
        } else if (hook.pOriginalFunction) {
            *hook.pOriginalFunction = address;
        }
    }
    return true;
}

// ---------- Entry points ----------

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

extern "C" {

// In Explorer: read the settings and put the hooks in. Run as a thread by Nyatch. 1 = working.
__declspec(dllexport) DWORD WINAPI NyatchExplorerStart(LPVOID) {
    std::lock_guard lock(g_mutex);
    if (g_started) return 1;
    LoadIni();
    std::vector<HANDLE> threads;
    BeginTransaction(threads);
    g_hooks.clear();
    if (!Wh_ModInit()) {
        DetourTransactionAbort();
        CloseThreads(threads);
        g_hooks.clear();
        return 0;
    }
    const LONG committed = DetourTransactionCommit();
    CloseThreads(threads);
    if (committed != NO_ERROR) {
        g_hooks.clear();
        return 0;
    }
    g_started = true;
    return 1;
}

// In Explorer: take the hooks out, wait for any call still inside them, and unload. Run as a thread.
__declspec(dllexport) DWORD WINAPI NyatchExplorerStop(LPVOID) {
    {
        std::lock_guard lock(g_mutex);
        if (g_started) {
            std::vector<HANDLE> threads;
            BeginTransaction(threads);
            for (const Hook& hook : g_hooks) DetourDetach(hook.original, hook.hook);
            DetourTransactionCommit();
            CloseThreads(threads);
            g_hooks.clear();
            Wh_ModUninit();  // waits until no call is inside a hook
            g_started = false;
        }
    }
    Sleep(250);  // a call that just left a hook is out of this DLL's code
    FreeLibraryAndExitThread(g_self, 0);
}

// In Nyatch's own process: make sure the symbol cache for this Windows' windows.storage.dll exists,
// downloading its symbols from Microsoft if needed. 1 = ready. `message` says what went wrong.
__declspec(dllexport) int NyatchExplorerPrepare(wchar_t* message, int chars) {
    auto fail = [&](const wchar_t* text) {
        if (message && chars > 0) wcsncpy_s(message, chars, text, _TRUNCATE);
        return 0;
    };
    HMODULE storage = LoadLibraryExW(L"windows.storage.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!storage) return fail(L"windows.storage.dll could not be loaded.");
    std::wstring key, pdbName;
    if (!PdbIdentity(storage, &key, &pdbName)) return fail(L"windows.storage.dll has no symbol information.");
    const std::wstring cache = CacheFile(key);
    if (GetFileAttributesW(cache.c_str()) != INVALID_FILE_ATTRIBUTES) return 1;

    const std::wstring folder = SymbolsFolder() + L"\\download";
    SHCreateDirectoryExW(nullptr, folder.c_str(), nullptr);
    const std::wstring pdbPath = folder + L"\\" + pdbName;
    const std::wstring url = L"https://msdl.microsoft.com/download/symbols/" + pdbName + L"/" + key + L"/" + pdbName;
    if (FAILED(URLDownloadToFileW(nullptr, url.c_str(), pdbPath.c_str(), 0, nullptr))) {
        return fail(L"The Windows symbols could not be downloaded from Microsoft.");
    }

    wchar_t modulePath[MAX_PATH] = {};
    GetModuleFileNameW(storage, modulePath, MAX_PATH);
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(reinterpret_cast<const BYTE*>(storage) +
                                                         reinterpret_cast<const IMAGE_DOS_HEADER*>(storage)->e_lfanew);
    const DWORD imageSize = nt->OptionalHeader.SizeOfImage;

    const HANDLE session = reinterpret_cast<HANDLE>(static_cast<ULONG_PTR>(0x4E594154));  // any unique value
    SymSetOptions(SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_NO_PROMPTS | SYMOPT_EXACT_SYMBOLS);
    if (!SymInitializeW(session, folder.c_str(), FALSE)) {
        DeleteFileW(pdbPath.c_str());
        return fail(L"The symbol reader could not start.");
    }
    constexpr DWORD64 kBase = 0x10000000;
    const DWORD64 base = SymLoadModuleExW(session, nullptr, modulePath, nullptr, kBase, imageSize, nullptr, 0);
    IMAGEHLP_MODULEW64 info = {sizeof(info)};
    if (!base || !SymGetModuleInfoW64(session, base, &info) || info.SymType != SymPdb) {
        SymCleanup(session);
        DeleteFileW(pdbPath.c_str());
        return fail(L"The Windows symbols could not be read.");
    }

    // Only the classes the code hooks, undecorated the way Windhawk writes them.
    struct Context {
        DWORD64 base;
        std::string out;
        int count = 0;
    } context{base};
    SymEnumSymbolsW(session, base, L"*", [](PSYMBOL_INFOW symbol, ULONG, PVOID user) -> BOOL {
        auto ctx = static_cast<Context*>(user);
        const std::wstring_view name(symbol->Name, symbol->NameLen);
        if (name.find(L"CFSFolder@@") == std::wstring_view::npos &&
            name.find(L"CRecursiveFolderOperation@@") == std::wstring_view::npos &&
            name.find(L"CDefCollection@@") == std::wstring_view::npos) {
            return TRUE;
        }
        wchar_t plain[4096] = {};
        const std::wstring decorated(name);
        if (!UnDecorateSymbolNameW(decorated.c_str(), plain, ARRAYSIZE(plain), 0x20000 /* UNDNAME_NO_PTR64 */)) return TRUE;
        // Windows' undecorator keeps "__ptr64" even when asked not to; Windhawk's names have none.
        std::wstring clean(plain);
        for (size_t at; (at = clean.find(L" __ptr64")) != std::wstring::npos;) clean.erase(at, 8);
        const int n = WideCharToMultiByte(CP_UTF8, 0, clean.c_str(), -1, nullptr, 0, nullptr, nullptr);
        std::string utf8(n > 0 ? n - 1 : 0, '\0');
        if (n > 1) WideCharToMultiByte(CP_UTF8, 0, clean.c_str(), -1, utf8.data(), n, nullptr, nullptr);
        char rva[32] = {};
        sprintf_s(rva, "%llx\t", static_cast<unsigned long long>(symbol->Address - ctx->base));
        ctx->out += rva + utf8 + "\n";
        ++ctx->count;
        return TRUE;
    }, &context);
    SymUnloadModule64(session, base);
    SymCleanup(session);
    DeleteFileW(pdbPath.c_str());
    if (context.count == 0) return fail(L"The functions Explorer folder sizes needs were not found in this Windows.");

    std::ofstream file(cache, std::ios::binary);
    file << context.out;
    return file.good() ? 1 : fail(L"The symbol list could not be saved.");
}

}  // extern "C"

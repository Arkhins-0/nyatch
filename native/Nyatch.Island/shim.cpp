// The library's doorway: what the host app calls, and the settings, storage
// and logging the island's code asks for.
#include "windhawk_shim.h"

#include <shlobj.h>

#include <cstdarg>
#include <cstdio>
#include <cwchar>
#include <mutex>
#include <string>
#include <unordered_map>

namespace {

std::mutex g_settingsLock;
std::unordered_map<std::wstring, std::wstring> g_settings;
bool g_started = false;

// %APPDATA%\Nyatch, created on first use. Empty if Windows will not say.
std::wstring DataFolder() {
    PWSTR roaming = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &roaming))) {
        return L"";
    }
    std::wstring folder = std::wstring(roaming) + L"\\Nyatch";
    CoTaskMemFree(roaming);
    CreateDirectoryW(folder.c_str(), nullptr);
    return folder;
}

std::wstring Trim(const std::wstring& text) {
    const size_t first = text.find_first_not_of(L" \t\r\n");
    if (first == std::wstring::npos) {
        return L"";
    }
    return text.substr(first, text.find_last_not_of(L" \t\r\n") - first + 1);
}

// The host writes settings.ini as UTF-8 lines of Name=Value, one per setting,
// with every setting present (it fills in the defaults).
void ReadSettings() {
    std::unordered_map<std::wstring, std::wstring> next;
    const std::wstring path = DataFolder() + L"\\settings.ini";
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (file != INVALID_HANDLE_VALUE) {
        std::string bytes;
        char chunk[8192];
        DWORD read = 0;
        while (ReadFile(file, chunk, sizeof(chunk), &read, nullptr) && read > 0) {
            bytes.append(chunk, read);
        }
        CloseHandle(file);
        if (bytes.size() >= 3 && bytes.compare(0, 3, "\xEF\xBB\xBF") == 0) {
            bytes.erase(0, 3);
        }
        std::wstring text;
        if (!bytes.empty()) {
            const int chars = MultiByteToWideChar(CP_UTF8, 0, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
            text.resize(chars);
            MultiByteToWideChar(CP_UTF8, 0, bytes.data(), static_cast<int>(bytes.size()), text.data(), chars);
        }
        size_t start = 0;
        while (start <= text.size()) {
            size_t end = text.find(L'\n', start);
            if (end == std::wstring::npos) {
                end = text.size();
            }
            const std::wstring line = text.substr(start, end - start);
            const size_t equals = line.find(L'=');
            if (equals != std::wstring::npos) {
                next[Trim(line.substr(0, equals))] = Trim(line.substr(equals + 1));
            }
            start = end + 1;
        }
    }
    std::lock_guard<std::mutex> hold(g_settingsLock);
    g_settings.swap(next);
}

// Setting names may carry printf-style arguments, as Windhawk allows.
std::wstring FormatName(PCWSTR name, va_list args) {
    wchar_t buffer[256];
    vswprintf_s(buffer, name, args);
    return buffer;
}

std::wstring Setting(const std::wstring& name) {
    std::lock_guard<std::mutex> hold(g_settingsLock);
    const auto found = g_settings.find(name);
    return found == g_settings.end() ? L"" : found->second;
}

const wchar_t kValuesKey[] = L"Software\\Nyatch\\State";

}  // namespace

void NyatchLog(const wchar_t* function, int line, const wchar_t* format, ...) {
    wchar_t message[1024];
    va_list args;
    va_start(args, format);
    vswprintf_s(message, format, args);
    va_end(args);
    wchar_t entry[1200];
    swprintf_s(entry, L"[Nyatch %s:%d] %s\n", function, line, message);
    OutputDebugStringW(entry);
}

int Wh_GetIntSetting(PCWSTR name, ...) {
    va_list args;
    va_start(args, name);
    const std::wstring value = Setting(FormatName(name, args));
    va_end(args);
    if (value == L"true") {
        return 1;
    }
    if (value == L"false" || value.empty()) {
        return 0;
    }
    return _wtoi(value.c_str());
}

PCWSTR Wh_GetStringSetting(PCWSTR name, ...) {
    va_list args;
    va_start(args, name);
    const std::wstring value = Setting(FormatName(name, args));
    va_end(args);
    return _wcsdup(value.c_str());
}

void Wh_FreeStringSetting(PCWSTR value) {
    free(const_cast<wchar_t*>(value));
}

int Wh_GetIntValue(PCWSTR name, int defaultValue) {
    DWORD value = 0;
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, kValuesKey, name, RRF_RT_REG_DWORD, nullptr, &value, &size) != ERROR_SUCCESS) {
        return defaultValue;
    }
    return static_cast<int>(value);
}

BOOL Wh_SetIntValue(PCWSTR name, int value) {
    const DWORD stored = static_cast<DWORD>(value);
    return RegSetKeyValueW(HKEY_CURRENT_USER, kValuesKey, name, REG_DWORD, &stored, sizeof(stored)) == ERROR_SUCCESS;
}

BOOL Wh_GetModStoragePath(PWSTR buffer, DWORD chars) {
    const std::wstring folder = DataFolder();
    if (folder.empty() || folder.size() + 1 > chars) {
        return FALSE;
    }
    wcscpy_s(buffer, chars, folder.c_str());
    return TRUE;
}

extern "C" {

// Show the island. Returns 1 if it started (or was already running).
__declspec(dllexport) int NyatchStart() {
    if (g_started) {
        return 1;
    }
    ReadSettings();
    g_started = WhTool_ModInit() != FALSE;
    return g_started ? 1 : 0;
}

// The host has rewritten settings.ini: read it again and apply it.
__declspec(dllexport) void NyatchReload() {
    ReadSettings();
    if (g_started) {
        WhTool_ModSettingsChanged();
    }
}

// Close the island and stop its threads.
__declspec(dllexport) void NyatchStop() {
    if (g_started) {
        g_started = false;
        WhTool_ModUninit();
    }
}

}  // extern "C"

// Nyatch: the part of Windhawk's mod API that the Explorer folder sizes code uses.
//
// Settings come from Nyatch's settings.ini (the Explorer.* settings), hooks go through Microsoft
// Detours and are applied together once the code has set them all up, and Windows' private
// functions are found from symbols Nyatch has already looked up (see windhawk_api.cpp).
#pragma once

// windows.h without its STATUS_ values, which the code takes from ntstatus.h; NTSTATUS from winternl.h.
#define WIN32_NO_STATUS
#include <windows.h>
#undef WIN32_NO_STATUS
#include <winternl.h>

// Windhawk defines the mod's id for every mod.
#define WH_MOD_ID L"explorer-details-better-file-sizes"

#include <string_view>
#include <type_traits>
#include <vector>

// Logging is left out: the code logs inside Explorer's hot paths, and there is nobody to read it.
#define Wh_Log(...) ((void)0)

int Wh_GetIntSetting(PCWSTR name, ...);
PCWSTR Wh_GetStringSetting(PCWSTR name, ...);
void Wh_FreeStringSetting(PCWSTR string);

// Queued, and applied when the code's set-up has finished. *originalFunction is usable from then on.
BOOL Wh_SetFunctionHook(void* targetFunction, void* hookFunction, void** originalFunction);

// What the folder sizes code defines, and Nyatch calls.
BOOL Wh_ModInit();
void Wh_ModUninit();
BOOL Wh_ModSettingsChanged(BOOL* bReload);

namespace WindhawkUtils {

template <typename Prototype>
BOOL SetFunctionHook(Prototype* targetFunction, Prototype* hookFunction, Prototype** originalFunction) {
    return Wh_SetFunctionHook(reinterpret_cast<void*>(targetFunction), reinterpret_cast<void*>(hookFunction),
                              reinterpret_cast<void**>(originalFunction));
}

struct SYMBOL_HOOK {
    std::vector<std::wstring_view> symbols;
    void** pOriginalFunction = nullptr;
    void* hookFunction = nullptr;
    bool optional = false;

    SYMBOL_HOOK() = default;

    template <typename Prototype>
    SYMBOL_HOOK(std::vector<std::wstring_view> symbols, Prototype** originalFunction,
                std::type_identity_t<Prototype*> hookFunction = nullptr, bool optional = false)
        : symbols(std::move(symbols)),
          pOriginalFunction(reinterpret_cast<void**>(originalFunction)),
          hookFunction(reinterpret_cast<void*>(hookFunction)),
          optional(optional) {}
};

// Finds each symbol in `module` (from Nyatch's lookup) and hooks it, or only fills in its address
// when there is no hook function. False if a required one is missing.
bool HookSymbols(HMODULE module, const SYMBOL_HOOK* symbolHooks, size_t symbolHooksCount);

}  // namespace WindhawkUtils

// Stands in for the Windhawk API the island's code was written against, so
// the same code builds as a plain library. Settings come from a file the host
// app writes; small remembered values go in the registry.
#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

void NyatchLog(const wchar_t* function, int line, const wchar_t* format, ...);

// Windhawk's Wh_Log is a macro taking a wide string literal and printf-style
// arguments.
#define Wh_Log(message, ...) NyatchLog(__FUNCTIONW__, __LINE__, L"" message, __VA_ARGS__)

// A setting by its full name ("Group.Name"). Switches read as 1 or 0.
int Wh_GetIntSetting(PCWSTR name, ...);
// The caller releases the string with Wh_FreeStringSetting.
PCWSTR Wh_GetStringSetting(PCWSTR name, ...);
void Wh_FreeStringSetting(PCWSTR value);

// Small values the island remembers between runs.
int Wh_GetIntValue(PCWSTR name, int defaultValue);
BOOL Wh_SetIntValue(PCWSTR name, int value);

// The folder the island keeps its own files in (lyrics cache, file shelf).
BOOL Wh_GetModStoragePath(PWSTR buffer, DWORD chars);

// The island's entry points, defined at the end of island.cpp.
BOOL WhTool_ModInit();
void WhTool_ModSettingsChanged();
void WhTool_ModUninit();

// Windhawk defines these for every mod; the island uses the version in the
// User-Agent of its web requests.
#define WH_MOD_ID L"nyatch"
#define WH_MOD_VERSION L"1.4.0"

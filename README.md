<p align="center"><img src="assets/brand/logo-256.png" width="96" alt="Nyatch logo"></p>

# Nyatch

A Dynamic Island–style notch for Windows, with a second notch that opens into pages of small tools.
Made by [Arkhins](https://arkhins.com).

Nyatch sits at the edge of your screen and stays out of the way. The **top notch** shows what is
happening right now; the **second notch** opens into pages you can turn with the mouse wheel.

## Top notch

- **Now playing**: cover, controls, a live sound wave, and synced lyrics.
- **Notices** that open under the notch: volume, brightness, Bluetooth devices, low battery,
  Caps Lock, Do not disturb, clipboard, and Windows notifications (click one to open its app).
- **Pages on hover**: clock, calendar, weather, hardware monitor, file tray. Choose which, and their order.
- **Quick Lookup**: a search box on a shortcut. Finds files, folders and apps, looks words up,
  searches the web, and works as a calculator (type `500*10`, press Enter to copy the answer).
- **Privacy icons** when the microphone or camera is in use.
- **Game overlay**: FPS, CPU, GPU, memory and disk in a compact strip.

## Second notch

Hover or click it and it opens into pages. Show, hide and reorder them in settings.

| Page | What it does |
|---|---|
| Apps | Open and recently closed apps, as icons |
| Tray | Files you dropped on a notch, shared by both |
| Clipboard | History of text, pictures and files, with pins; click to paste |
| Notifications | Windows notifications, clear one or all, Do not disturb |
| Claude, ChatGPT, Gemini | Chat with each through your own API key, with saved chats |
| Focus | Timer, stopwatch and pomodoro |
| Notes & to-dos | Several lists and notes |
| Terminal | Command Prompt and PowerShell tabs |

Hold **Alt** while pointing at either notch to hide it and click what is behind it.

## Also included

- **Better file sizes in File Explorer** (optional): folder sizes in the Details view, and MB / GB
  instead of only KB.
- **Themes**: presets or your own colours, three shapes (pill, Windows 11 box, notch), blur or acrylic.
- **Automatic updates**, installed only if signed with Nyatch's release key.

## Install

Download the newest installer from [nyatch.arkhins.com](https://nyatch.arkhins.com) and run it.
Windows asks for administrator approval, and Nyatch is installed for the whole PC.

Requires 64-bit Windows. Built and tested on Windows 11.

## Privacy

- Nyatch has no account and collects nothing.
- API keys for the chat pages are kept in Windows Credential Manager on your PC and sent only to the
  assistant you chat with.
- Chats, clipboard history and notes are saved on your PC only.
- It goes online only for what you turn on: weather, lyrics, lookups, chat, and update checks.

## Build from source

You need Visual Studio 2022 with "Desktop development with C++" and the .NET 10 SDK.

```powershell
.\build.ps1          # build
.\build.ps1 -Run     # build and start
.\build.ps1 -Test    # build and run the tests
.\build.ps1 -Msi     # build the installer: out\Nyatch-<version>.msi
```

| Folder | What is in it |
|---|---|
| `native/Nyatch.Island` | Both notches: C++, Direct2D |
| `native/Nyatch.Explorer` | File Explorer folder sizes: C++, loaded into Explorer |
| `src/Nyatch.App` | The app: settings window, the second notch's pages (C#, WPF) |
| `src/Nyatch.Core` | Settings, history, updates and assistant clients (C#) |
| `tests/Nyatch.Core.Tests` | Tests for the core |
| `installer` | The MSI (WiX) |

## Feedback

Use **Report a problem** or **Send feedback** in Nyatch's tray menu: both open an email to
arkhins@arkhins.com, the report with the version and the last crash filled in.

## Licence

Apache License 2.0. Copyright 2026 Arkhins (https://arkhins.com). See [LICENSE](LICENSE) and [NOTICE](NOTICE);
the NOTICE must be kept in copies and derived works.

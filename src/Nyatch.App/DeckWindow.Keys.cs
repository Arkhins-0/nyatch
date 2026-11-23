using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Nyatch.Core;

namespace Nyatch.App;

// The second notch's hotkey: it opens a page picker (drawn by the island), and a page's letter opens
// that page, ready to use. Pages can be "hotkey only": then the wheel and the dots pass them by.
internal sealed partial class DeckWindow
{
    private const int DeckHotkeyId = 0x4E01;
    private const int HotkeyMessage = 0x0312;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);

    // The pages the wheel and the dots reach come first in _tabs; then the hotkey-only ones; the picker last.
    private int _turnCount = 1;
    private string _hotkeyShown = "";
    private bool _hotkeyHooked;
    private readonly List<string> _keyTargets = [];

    // The picker's LLM tile: it opens the LLM panel (on the chat last used), and the letter it has.
    private const string LlmTarget = "llm";
    private char _llmLetter;

    /// <summary>The next page the wheel turns to: round the pages on the notch (from a hotkey-only page, the first or last of them).</summary>
    private int TurnFrom(int tab, bool forward)
    {
        var count = Math.Max(1, _turnCount);
        if (tab < 0 || tab >= count) return forward ? 0 : count - 1;
        return (tab + (forward ? 1 : count - 1)) % count;
    }

    /// <summary>Hotkey modifiers ("ctrl_alt") and key ("N", "Space", "F5") as RegisterHotKey wants them; null when they are not a hotkey.</summary>
    internal static (uint Modifiers, uint Key)? ParseHotkey(string modifiers, string key)
    {
        uint mods = 0;
        foreach (var part in modifiers.ToLowerInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            mods |= part switch { "alt" => 1u, "ctrl" => 2u, "shift" => 4u, "win" => 8u, _ => 0u };
        }
        key = key.Trim();
        uint vk = key.Length == 1 && DeckPages.IsKey(char.ToUpperInvariant(key[0])) ? char.ToUpperInvariant(key[0])
            : key.Equals("Space", StringComparison.OrdinalIgnoreCase) ? 0x20u
            : key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var n) && n is >= 1 and <= 12 ? (uint)(0x70 + n - 1)
            : 0u;
        // Without a modifier it would take the key from every app.
        return mods == 0 || vk == 0 ? null : (mods, vk);
    }

    /// <summary>Take the hotkey from the settings (again, when they change).</summary>
    private void RegisterDeckHotkey()
    {
        var window = new WindowInteropHelper(this).Handle;
        if (window == IntPtr.Zero) return;
        if (!_hotkeyHooked)
        {
            HwndSource.FromHwnd(window)?.AddHook((IntPtr _, int message, IntPtr w, IntPtr _, ref bool handled) =>
            {
                if (message == HotkeyMessage && w.ToInt32() == DeckHotkeyId)
                {
                    handled = true;
                    OnDeckHotkey();
                }
                return IntPtr.Zero;
            });
            _hotkeyHooked = true;
        }
        UnregisterHotKey(window, DeckHotkeyId);
        _hotkeyShown = "";
        if (_settings.Get("Shortcuts.DeckHotkeyEnabled") != "true") return;
        var modifiers = _settings.Get("Shortcuts.DeckModifiers");
        var key = _settings.Get("Shortcuts.DeckKey");
        if (ParseHotkey(modifiers, key) is not { } hotkey) return;
        // MOD_NOREPEAT: holding it down does not open and close it over and over.
        if (RegisterHotKey(window, DeckHotkeyId, hotkey.Modifiers | 0x4000, hotkey.Key)) _hotkeyShown = DeckPages.HotkeyWords(modifiers, key);
    }

    private int PickerTab => _tabs.FindIndex(t => t.Native == "keys");

    /// <summary>The hotkey: the picker opens (or, while it is open, closes).</summary>
    private void OnDeckHotkey()
    {
        var picker = PickerTab;
        if (picker < 0) return;
        if (_expanded && _tab == picker)
        {
            Collapse();
            return;
        }
        Island.DeckKeyboard(true);
        if (_expanded) ShowTab(picker, 0);
        else
        {
            _tab = picker;
            Expand();
        }
    }

    /// <summary>The picker for the island (see its DrawDeckKeys): a tile for each page with a letter.</summary>
    private string KeysData()
    {
        _keyTargets.Clear();
        var keys = DeckPages.Keys(_settings.Get("Deck.PageKeys"));
        var records = new List<string> { "H\u001F" + _hotkeyShown };
        foreach (var (id, show) in DeckPages.Order(_settings.Get("Deck.Pages")))
        {
            // While the LLM panel is on, the chats are there: one LLM tile below stands for them.
            if (SideOn && AiIds.Contains(id)) continue;
            if (show == PageShow.Hidden || !keys.TryGetValue(id, out var key) || !_pagesById.TryGetValue(id, out var page)) continue;
            _keyTargets.Add(id);
            records.Add(string.Join('\u001F', "K", key.ToString(), page.Name, show == PageShow.HotkeyOnly ? "1" : "0", (_keyTargets.Count - 1).ToString()));
        }
        _llmLetter = '\0';
        if (SideOn)
        {
            // L, unless another page has it: then the first letter no page has.
            var taken = keys.Where(k => !AiIds.Contains(k.Key)).Select(k => k.Value).ToHashSet();
            _llmLetter = !taken.Contains('L') ? 'L' : Enumerable.Range('A', 26).Select(c => (char)c).FirstOrDefault(c => !taken.Contains(c));
            _keyTargets.Add(LlmTarget);
            records.Add(string.Join('\u001F', "K", _llmLetter == '\0' ? "" : _llmLetter.ToString(), "LLM", "0", (_keyTargets.Count - 1).ToString()));
        }
        return string.Join('\u001E', records);
    }

    /// <summary>The picker, from the island: a tile, a letter (60000 + it), or Esc (59999).</summary>
    private void KeysAction(int action)
    {
        if (action == 59999)
        {
            Collapse();
            return;
        }
        if (action >= 60000)
        {
            var letter = (char)(action - 60000);
            if (letter == _llmLetter && _llmLetter != '\0')
            {
                OpenPage(LlmTarget);
                return;
            }
            var keys = DeckPages.Keys(_settings.Get("Deck.PageKeys"));
            var id = _keyTargets.FirstOrDefault(t => keys.TryGetValue(t, out var k) && k == letter);
            if (id is not null) OpenPage(id);
            return;
        }
        if (action >= 0 && action < _keyTargets.Count) OpenPage(_keyTargets[action]);
    }

    /// <summary>Open a page, ready to use: the cursor in its text box, if it has one.</summary>
    private void OpenPage(string id)
    {
        // The chats are on the LLM panel while it is on: the picker opens it there.
        if (id == LlmTarget)
        {
            Collapse();
            OpenSide();
            return;
        }
        if (SideOn && AiIds.Contains(id))
        {
            Collapse();
            OpenSide(id);
            return;
        }
        var at = _tabs.FindIndex(t => t.Native == id);
        if (at < 0) return;
        // From the picker: the page keeps the keyboard, so Esc closes it.
        Island.DeckKeyboard(true);
        if (_expanded) ShowTab(at, 0);
        else
        {
            _tab = at;
            Expand();
        }
        var box = id switch
        {
            "claude" or "chatgpt" or "gemini" when ShownAi is { } chat && HasKey(chat) && !chat.History => ReplyBox,
            "focus" => 1,
            "terminal" => CommandBox,
            "notes" => PickedNote() is not null ? NoteBox : TodoBox,
            _ => 0,
        };
        if (box != 0) Island.DeckFocusField(box);
    }
}

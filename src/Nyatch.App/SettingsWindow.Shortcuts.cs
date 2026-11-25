using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Nyatch.Core;

namespace Nyatch.App;

// The Shortcuts page: every hotkey of Nyatch, and nowhere else. Each is recorded the same way (click
// the box, press the keys) and checked against Nyatch's other hotkeys and other apps. Under the second
// notch's hotkey are the letters that open its pages.
internal sealed partial class SettingsWindow
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);

    /// <summary>A hotkey as settings: what it is for, its held keys, its key, and its on/off switch if it has one.</summary>
    private sealed record Hotkey(string For, string Modifiers, string Key, string? Enabled);

    private static readonly Hotkey HideShowHotkey = new("hiding and showing the top notch", "Shortcuts.HideShowModifiers", "Shortcuts.HideShowKey", "Shortcuts.HideShowHotkeyEnabled");
    private static readonly Hotkey LookupHotkey = new("Quick Lookup", "Shortcuts.LookupModifiers", "Shortcuts.LookupKey", null);
    private static readonly Hotkey DeckHotkey = new("opening a page of the second notch", "Shortcuts.DeckModifiers", "Shortcuts.DeckKey", "Shortcuts.DeckHotkeyEnabled");
    private static readonly Hotkey SideHotkey = new("opening and closing the LLM panel", "Shortcuts.SideModifiers", "Shortcuts.SideKey", "Shortcuts.SideHotkeyEnabled");
    private static readonly Hotkey[] Hotkeys = [HideShowHotkey, LookupHotkey, DeckHotkey, SideHotkey];

    private string HotkeyWords(Hotkey hotkey) => DeckPages.HotkeyWords(_settings.Get(hotkey.Modifiers), _settings.Get(hotkey.Key));

    private void ShowShortcuts()
    {
        Head(_page);

        SectionStrip("Top notch", 2);
        var rows = Rows();
        HotkeyRow(rows, "Hide or show the top notch", "", HideShowHotkey);
        var lookupOn = _settings.Get("Modules.QuickLookup") == "true";
        var lookup = HotkeyRow(rows, "Quick Lookup", "", LookupHotkey);
        if (!lookupOn && WordsOf(lookup) is { } lookupWords)
        {
            var off = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            off.Children.Add(Chip("Quick Lookup is off", _faint, _high, margin: new Thickness(0, 0, 8, 0)));
            off.Children.Add(LinkChip("Quick Lookup", () => OpenPage("search")));
            lookupWords.Children.Add(off);
        }

        var order = DeckPages.Order(_settings.Get("Deck.Pages"));
        SectionStrip("Second notch", null);
        rows = Rows();
        var open = HotkeyRow(rows, "Open a page", "Then press the page's letter.", DeckHotkey);
        WordsOf(open)?.Children.Add(Framed(PickerPicture(), new Thickness(0, 12, 0, 0)));
        var letters = Row(rows, "Page letters", "Click a key, then press a letter. Backspace removes it.", null, hover: false);
        if (WordsOf(letters) is { } letterWords)
        {
            letterWords.Margin = new Thickness(0);
            var grid = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var keys = DeckPages.Keys(_settings.Get("Deck.PageKeys"));
            // A hidden page has no letter (one kept from before is not in use).
            // (The chats have no letters here while the LLM panel has them: the picker's LLM tile opens it.)
            var llmPanel = NotchUse.SideInUse(_settings);
            foreach (var (id, show) in order)
            {
                if (llmPanel && id is "claude" or "chatgpt" or "gemini") continue;
                grid.Children.Add(LetterTile(id, show, show != PageShow.Hidden && keys.TryGetValue(id, out var k) ? k.ToString() : ""));
            }
            letterWords.Children.Add(grid);
        }
        if (_settings.Get("Shortcuts.DeckHotkeyEnabled") != "true") letters.Opacity = 0.5;

        SectionStrip("LLM panel", 1);
        rows = Rows();
        var side = HotkeyRow(rows, "Open or close the LLM panel", "On the chat last used, ready to type in.", SideHotkey);
        if (_settings.Get("Side.Enabled") != "true" && WordsOf(side) is { } sideWords)
        {
            // Turned off, it is hidden: this hotkey still opens it (and with the hotkey off, it is not used).
            var off = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            off.Children.Add(Chip("Hidden: only this hotkey opens it", _faint, _high, margin: new Thickness(0, 0, 8, 0)));
            off.Children.Add(LinkChip("Panel", () => OpenPage("side-layout")));
            sideWords.Children.Add(off);
        }
    }

    // ---------- A hotkey's row ----------

    /// <summary>A hotkey's row: its recorder, its switch if it has one, and under the words any problem with the keys pressed.</summary>
    private Border HotkeyRow(StackPanel rows, string title, string line, Hotkey hotkey)
    {
        var problem = new TextBlock { FontSize = 12.5, Foreground = _danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        string[] names = hotkey.Enabled is null ? [hotkey.Modifiers, hotkey.Key] : [hotkey.Modifiers, hotkey.Key, hotkey.Enabled];
        if (names.Any(_settings.IsChanged))
        {
            var reset = IconButton(IconReset, "Reset to the default hotkey", () =>
            {
                foreach (var name in names) _settings.Reset(name);
                Changed();
                ShowPage(keepScroll: true);
            });
            reset.Margin = new Thickness(0, 0, 8, 0);
            right.Children.Add(reset);
        }
        var on = hotkey.Enabled is null || _settings.Get(hotkey.Enabled) == "true";
        var recorder = HotkeyBox(hotkey, problem);
        recorder.Opacity = on ? 1 : 0.5;
        right.Children.Add(recorder);
        if (hotkey.Enabled is { } enabled)
        {
            var toggle = Switch(on, value =>
            {
                _settings.Set(enabled, value ? "true" : "false");
                Changed();
                ShowPage(keepScroll: true);
            });
            toggle.SetValue(AutomationName, title);
            ((FrameworkElement)toggle).Margin = new Thickness(12, 0, 0, 0);
            right.Children.Add(toggle);
        }
        var row = Row(rows, title, line, right, hover: false);
        WordsOf(row)?.Children.Add(problem);
        return row;
    }

    /// <summary>The hotkey as key caps in a box: click, then press the keys. Checked against Nyatch's other hotkeys and other apps.</summary>
    private FrameworkElement HotkeyBox(Hotkey hotkey, TextBlock problem)
    {
        var caps = KeyCaps(HotkeyWords(hotkey));
        caps.HorizontalAlignment = HorizontalAlignment.Center;
        var prompt = new TextBlock { Text = "Press the keys…", FontSize = 12.5, Foreground = _faint, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        var box = new Border
        {
            Child = new Grid { Children = { caps, prompt } },
            MinWidth = 168,
            Height = 34,
            Padding = new Thickness(10, 0, 6, 0),
            CornerRadius = new CornerRadius(8),
            Background = _panel,
            BorderBrush = _line,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = "Click, then press the keys",
            VerticalAlignment = VerticalAlignment.Center,
        };
        box.SetValue(AutomationName, $"Hotkey for {hotkey.For}: {HotkeyWords(hotkey)}. Press new keys to change it.");
        box.MouseLeftButtonUp += (_, _) => box.Focus();
        box.GotKeyboardFocus += (_, _) =>
        {
            box.BorderBrush = _accent;
            caps.Visibility = Visibility.Collapsed;
            prompt.Visibility = Visibility.Visible;
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            box.BorderBrush = _line;
            caps.Visibility = Visibility.Visible;
            prompt.Visibility = Visibility.Collapsed;
        };
        box.PreviewKeyDown += (_, press) =>
        {
            var key = press.Key == Key.System ? press.SystemKey : press.Key;
            // Tab still moves on; Esc gives up.
            if (key == Key.Tab) return;
            press.Handled = true;
            if (key == Key.Escape)
            {
                Keyboard.ClearFocus();
                return;
            }
            // The held keys on their own: wait for the key.
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var name = key is >= Key.A and <= Key.Z ? ((char)('A' + (key - Key.A))).ToString()
                : key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString()
                : key == Key.Space ? "Space"
                : key is >= Key.F1 and <= Key.F12 ? "F" + (key - Key.F1 + 1)
                : null;
            var held = Keyboard.Modifiers;
            var parts = new List<string>();
            if ((held & ModifierKeys.Control) != 0) parts.Add("ctrl");
            if ((held & ModifierKeys.Alt) != 0) parts.Add("alt");
            if ((held & ModifierKeys.Shift) != 0) parts.Add("shift");
            if ((held & ModifierKeys.Windows) != 0 || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("win");
            var modifiers = string.Join("_", parts);
            void Say(string words)
            {
                problem.Text = words;
                problem.Visibility = Visibility.Visible;
            }
            if (name is null)
            {
                Say("Use a letter, a number, Space or F1 to F12.");
                return;
            }
            if (parts.Count == 0 || (parts.Count == 1 && parts[0] == "shift"))
            {
                Say("Hold Ctrl, Alt or Win with it, so the key still works in other apps.");
                return;
            }
            var pressed = DeckPages.HotkeyWords(modifiers, name);
            // Nyatch's own hotkeys.
            foreach (var other in Hotkeys)
            {
                if (other == hotkey || _settings.Get(other.Modifiers) != modifiers || !_settings.Get(other.Key).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                Say($"Nyatch already uses {pressed} for {other.For}.");
                return;
            }
            var same = _settings.Get(hotkey.Modifiers) == modifiers && _settings.Get(hotkey.Key).Equals(name, StringComparison.OrdinalIgnoreCase);
            // Another app's: Windows says so when it cannot be taken.
            if (!same && DeckWindow.ParseHotkey(modifiers, name) is { } wanted)
            {
                var window = new WindowInteropHelper(this).Handle;
                if (window != IntPtr.Zero)
                {
                    if (!RegisterHotKey(window, 0x4E7F, wanted.Modifiers, wanted.Key))
                    {
                        Say($"Another app already uses {pressed}. Try another.");
                        return;
                    }
                    UnregisterHotKey(window, 0x4E7F);
                }
            }
            problem.Visibility = Visibility.Collapsed;
            _settings.Set(hotkey.Modifiers, modifiers);
            _settings.Set(hotkey.Key, name);
            Changed();
            ShowPage(keepScroll: true);
        };
        return box;
    }

    // ---------- The second notch's page letters ----------

    /// <summary>A page with its letter as a key cap: click the cap and press a letter or a number; Backspace takes it away.</summary>
    private FrameworkElement LetterTile(string id, PageShow show, string letter)
    {
        var choice = DeckPageChoices.First(c => c.Id == id);
        var has = letter.Length > 0;
        var text = new TextBlock
        {
            Text = has ? letter : IconAdd,
            FontSize = has ? 14 : 10,
            FontWeight = FontWeights.SemiBold,
            FontFamily = has ? Numbers : Glyphs,
            Foreground = has ? _accent : _faint,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cap = new Border
        {
            Child = text,
            Width = 36,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = has ? _accentTint : _high,
            BorderBrush = _line,
            BorderThickness = new Thickness(1, 1, 1, 2),
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = has ? "Click, then press a letter or a number. Backspace takes it away." : "Click, then press a letter or a number",
            VerticalAlignment = VerticalAlignment.Center,
        };
        cap.SetValue(AutomationName, has ? $"{choice.Name}: letter {letter}. Press a letter or a number to change it, Backspace to take it away." : $"{choice.Name}: no letter. Press a letter or a number to give it one.");
        cap.MouseLeftButtonUp += (_, _) => cap.Focus();
        cap.GotKeyboardFocus += (_, _) =>
        {
            cap.BorderBrush = _accent;
            text.Text = "…";
            text.FontFamily = Numbers;
            text.FontSize = 14;
        };
        cap.LostKeyboardFocus += (_, _) =>
        {
            cap.BorderBrush = _line;
            text.Text = has ? letter : IconAdd;
            text.FontFamily = has ? Numbers : Glyphs;
            text.FontSize = has ? 14 : 10;
        };
        cap.PreviewKeyDown += (_, key) =>
        {
            if (key.Key == Key.Tab) return;
            if (key.Key == Key.Escape)
            {
                key.Handled = true;
                Keyboard.ClearFocus();
                return;
            }
            if (key.Key is Key.Back or Key.Delete)
            {
                key.Handled = true;
                if (has) SetPageLetter(id, null);
                return;
            }
            var pressed = key.Key is >= Key.A and <= Key.Z ? (char)('A' + (key.Key - Key.A))
                : key.Key is >= Key.D0 and <= Key.D9 ? (char)('0' + (key.Key - Key.D0))
                : key.Key is >= Key.NumPad0 and <= Key.NumPad9 ? (char)('0' + (key.Key - Key.NumPad0))
                : '\0';
            if (pressed == '\0' || Keyboard.Modifiers != ModifierKeys.None) return;
            key.Handled = true;
            SetPageLetter(id, pressed);
        };

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        words.Children.Add(new TextBlock { Text = choice.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = show == PageShow.Hidden ? _faint : _text, TextTrimming = TextTrimming.CharacterEllipsis });
        words.Children.Add(new TextBlock
        {
            Text = show switch { PageShow.Shown => has ? "On the notch" : "On the notch, no letter", PageShow.HotkeyOnly => "Hotkey only", _ => "Hidden" },
            FontSize = 12,
            Foreground = _faint,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(cap);
        line.Children.Add(words);
        return new Border { Child = line, Width = 226, Margin = new Thickness(0, 0, 8, 12) };
    }

    /// <summary>
    /// Give a page a letter (the page that had it takes this page's old one), or take its letter away.
    /// Off the notch, a page with a letter opens by the hotkey only; without one it is hidden.
    /// </summary>
    private void SetPageLetter(string id, char? letter)
    {
        var keys = DeckPages.Keys(_settings.Get("Deck.PageKeys"));
        var order = DeckPages.Order(_settings.Get("Deck.Pages"));
        void Off(string page, PageShow now)
        {
            var at = order.FindIndex(o => o.Id == page);
            if (at >= 0 && order[at].Show != PageShow.Shown) order[at] = (page, now);
        }
        if (letter is not { } pressed)
        {
            keys.Remove(id);
            Off(id, PageShow.Hidden);
        }
        else
        {
            var old = keys.TryGetValue(id, out var was) ? was : '\0';
            var other = keys.FirstOrDefault(p => p.Value == pressed && p.Key != id).Key;
            if (other is not null)
            {
                if (old != '\0') keys[other] = old;
                else keys.Remove(other);
            }
            keys[id] = pressed;
            // A page only its letter opened keeps a letter: the first free one.
            if (other is not null && !keys.ContainsKey(other) && order.Any(o => o.Id == other && o.Show == PageShow.HotkeyOnly)) DeckPages.GiveKey(keys, other);
            Off(id, PageShow.HotkeyOnly);
        }
        _settings.Set("Deck.PageKeys", DeckPages.KeysText(keys));
        _settings.Set("Deck.Pages", DeckPages.OrderText(order));
        Changed();
        ShowPage(keepScroll: true);
    }
}

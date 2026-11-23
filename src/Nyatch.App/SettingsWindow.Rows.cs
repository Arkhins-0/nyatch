using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nyatch.Core;

namespace Nyatch.App;

// One setting as a row: its name and what it does on the left, the control that suits it on the right
// (a switch, segments, a list, a slider, a stepper, a colour well, a font list, or pictures of its
// choices under the words). A setting that needs another one that is off is dimmed and sits under it.
internal sealed partial class SettingsWindow
{
    /// <summary>Numbers set with a slider: lowest, highest, step, unit.</summary>
    private static readonly Dictionary<string, (int Min, int Max, int Step, string Unit)> Sliders = new(StringComparer.Ordinal)
    {
        ["Themes.PillOpacity"] = (35, 100, 1, "%"),
        ["Themes.TintIntensity"] = (0, 100, 1, "%"),
        ["Themes.BackdropFillOpacity"] = (0, 100, 1, "%"),
        ["Themes.BackdropTint"] = (0, 100, 1, "%"),
        ["Themes.AccentBloom"] = (0, 200, 5, "%"),
        ["Themes.CoverTint"] = (0, 100, 1, "%"),
        ["Themes.TextScale"] = (70, 160, 5, "%"),
    };

    /// <summary>Numbers typed or nudged: lowest, highest, step, unit.</summary>
    private static readonly Dictionary<string, (int Min, int Max, int Step, string Unit)> Steppers = new(StringComparer.Ordinal)
    {
        ["Appearance.OffsetX"] = (-4000, 4000, 1, "px"),
        ["Appearance.OffsetY"] = (-4000, 4000, 1, "px"),
        ["Appearance.OffsetYExpanded"] = (-4000, 4000, 1, "px"),
        ["Deck.OffsetX"] = (-4000, 4000, 1, "px"),
        ["Deck.OffsetY"] = (-4000, 4000, 1, "px"),
        ["Modules.LyricsOffsetMs"] = (-3000, 3000, 50, "ms"),
        ["Modules.LyricsOffsetBluetoothMs"] = (-3000, 3000, 50, "ms"),
        ["Modules.FileTrayMaxItems"] = (1, 25, 1, ""),
        ["Deck.AppsRecentCount"] = (1, 30, 1, ""),
        ["Deck.ClipboardKept"] = (5, 500, 5, ""),
        ["Deck.TimerMinutes"] = (1, 999, 1, "min"),
        ["Deck.PomodoroFocus"] = (1, 180, 5, "min"),
        ["Deck.PomodoroBreak"] = (1, 60, 1, "min"),
        ["Deck.PomodoroLongBreak"] = (1, 120, 5, "min"),
        ["Deck.PomodoroRounds"] = (2, 12, 1, ""),
    };

    /// <summary>The icon (Segoe Fluent Icons) on a setting's card, for the settings that lead a group.</summary>
    private static readonly Dictionary<string, string> SettingGlyphs = new(StringComparer.Ordinal)
    {
        ["General.LaunchAtStartup"] = "\uE7E8",
        ["General.SettingsTheme"] = "\uE790",
        ["Appearance.Position"] = "\uE707",
        ["Appearance.TargetMonitor"] = "\uE7F4",
        ["Appearance.SizeScale"] = "\uE740",
        ["Behavior.AlwaysOnTop"] = "\uE718",
        ["Behavior.ExpandOnHover"] = "\uE7C9",
        ["Behavior.AutoHideIdleSeconds"] = "\uE7B3",
        ["Behavior.AutoHideFullscreen"] = "\uE740",
        ["Animations.TargetFPS"] = "\uE9D9",
        ["Animations.AnimationStyle"] = "\uE7C9",
        ["Animations.AnimationSpeed"] = "\uE916",
        ["Themes.ThemePreset"] = "\uE790",
        ["Themes.BackdropMaterial"] = "\uE81E",
        ["Themes.TextScale"] = "\uE8E9",
        ["Themes.AccentColorMode"] = "\uE790",
        ["Themes.FontFamily"] = "\uE8D2",
        ["Indicators.PrivacyDots"] = "\uE722",
        ["Modules.Media"] = "\uE8D6",
        ["Modules.Volume"] = "\uE767",
        ["Modules.Brightness"] = "\uE706",
        ["Modules.CapsLock"] = "\uE765",
        ["Modules.Battery"] = "\uE83F",
        ["Modules.BluetoothIndicator"] = "\uE702",
        ["Modules.TimerModule"] = "\uE916",
        ["Modules.Clipboard"] = "\uE77F",
        ["Modules.DoNotDisturbIndicator"] = "\uE708",
        ["Modules.GameOverlay"] = "\uE7FC",
        ["Modules.Weather"] = "\uE753",
        ["Modules.WeatherCity"] = "\uE707",
        ["Modules.Lyrics"] = "\uE8BD",
        ["Modules.MediaTabLyrics"] = "\uE8BD",
        ["Modules.LyricsSource"] = "\uE774",
        ["Modules.LyricsCache"] = "\uE74E",
        ["Modules.Language"] = "\uE774",
        ["Modules.ClockFormat"] = "\uE823",
        ["Modules.ShowSeconds"] = "\uE916",
        ["Modules.DateStyle"] = "\uE787",
        ["Modules.FirstDayOfWeek"] = "\uE787",
        ["Modules.ClockNote"] = "\uE70F",
        ["Modules.QuickLookup"] = "\uE721",
        ["Modules.Calculator"] = "\uE8EF",
        ["Modules.FileTrayModule"] = "\uE8B7",
        ["Modules.MediaExpandBlocklist"] = "\uE8D6",
        ["Assistant.KeepHistory"] = "\uE81C",
        ["Assistant.Instructions"] = "\uE70F",
        ["Deck.Enabled"] = "\uE7E8",
        ["Deck.Position"] = "\uE707",
        ["Deck.ExpandOnHover"] = "\uE7C9",
        ["Deck.AlwaysOnTop"] = "\uE718",
        ["Deck.Emoji"] = "\uE76E",
        ["Deck.AppsPinned"] = "\uE718",
        ["Deck.AppsRecent"] = "\uE81C",
        ["Deck.ClipboardRemember"] = "\uE77F",
        ["Deck.TimerMinutes"] = "\uE916",
        ["Deck.PomodoroFocus"] = "\uE916",
        ["Deck.FocusSound"] = "\uE767",
        ["Deck.FocusNotify"] = "\uEA8F",
        ["Deck.TerminalShell"] = "\uE756",
        ["Deck.TerminalFolder"] = "\uE8B7",
        ["Deck.PingHost"] = "\uE701",
        ["Explorer.Enabled"] = "\uEC50",
        ["Characters.Enabled"] = "\uE76E",
        ["Characters.AutoUpdate"] = "\uE895",
        ["Characters.UseAccent"] = "\uE790",
        ["Themes.AccentBloom"] = "\uE706",
        ["Appearance.ShapeStyle"] = "\uE7A8",
        ["Themes.ContourBorderMode"] = "\uE8B3",
        ["Themes.PillOpacity"] = "\uE7B3",
        ["Themes.TintIntensity"] = "\uE790",
        ["Themes.MaterialDepth"] = "\uE81E",
        ["Themes.DropShadow"] = "\uE8B9",
        ["Themes.PillBgColor"] = "\uE790",
        ["Themes.TextPrimaryColor"] = "\uE8D2",
        ["Themes.TextSecondaryColor"] = "\uE8D2",
        ["Themes.ContourBorderHex"] = "\uE8B3",
        ["Animations.ExpandedMediaTransitions"] = "\uE8D6",
        ["Animations.PillCoverSpin"] = "\uE7AD",
        ["Modules.DateFirst"] = "\uE787",
        ["Themes.ClockAccentGlow"] = "\uE706",
        ["Modules.MediaPillClock"] = "\uE823",
        ["Appearance.OffsetX"] = "\uE7C2",
        ["Appearance.OffsetY"] = "\uE7C2",
        ["Appearance.SeparateExpandedOffsetY"] = "\uE7C2",
        ["Appearance.BorderMergedMode"] = "\uE740",
        ["Appearance.AutoDpiScale"] = "\uE7F4",
        ["Behavior.UnhideOnHover"] = "\uE890",
        ["Themes.CalendarAccent"] = "\uE790",
        ["Modules.LyricsOffsetMs"] = "\uE916",
        ["Modules.LyricsOffsetBluetoothMs"] = "\uE702",
        ["Modules.Progress"] = "\uE9D9",
        ["Modules.NotificationRespectDnD"] = "\uE708",
        ["Modules.StatusCountdownProgress"] = "\uE916",
        ["Deck.OffsetX"] = "\uE7C2",
        ["Deck.OffsetY"] = "\uE7C2",
        ["Deck.Shows"] = "\uE8FD",
        ["Modules.FileTrayMaxItems"] = "\uE8B7",
        ["Modules.FileTrayPersist"] = "\uE74E",
        ["Deck.PomodoroBreak"] = "\uE916",
        ["Deck.PomodoroLongBreak"] = "\uE916",
        ["Deck.PomodoroRounds"] = "\uE72C",
        ["Explorer.SizeUnits"] = "\uE8EC",
        ["Explorer.IecUnits"] = "\uE8EC",
        ["Explorer.FolderSizes"] = "\uE8B7",
        ["Updates.AutoCheck"] = "\uE895",
        ["Updates.AutoInstall"] = "\uE896",
    };

    /// <summary>Text that can be long: its box goes under the words, across the row.</summary>
    private static readonly HashSet<string> WideText = new(StringComparer.Ordinal) { "Assistant.Prompt", "Assistant.Instructions", "Modules.MediaExpandBlocklist", "Deck.TerminalFolder" };

    /// <summary>What an empty text box says, where "Empty" would not help.</summary>
    private static readonly Dictionary<string, string> Hints = new(StringComparer.Ordinal)
    {
        ["Assistant.Instructions"] = "Such as: keep answers short, and use British spelling",
        ["Modules.MediaExpandBlocklist"] = "Such as: chrome, youtube.com",
        ["Deck.TerminalFolder"] = "Such as: C:\\Projects",
        ["Modules.WeatherCity"] = "Found by itself",
        ["Modules.DateFormat"] = "ddd d MMM",
        ["Modules.ClockNote"] = "Such as: Stay curious",
    };

    /// <summary>Settings that are a colour.</summary>
    private static bool IsColour(Setting setting) =>
        setting.Kind == SettingKind.Text && setting.Default.StartsWith('#') && (setting.Key.EndsWith("Hex", StringComparison.Ordinal) || setting.Key.EndsWith("Color", StringComparison.Ordinal));

    /// <summary>A short list of short names reads best side by side; anything longer is a drop-down list.</summary>
    private static bool FitsSegments(Setting setting) =>
        setting.Kind == SettingKind.Choice && setting.Options.Count <= 7 && setting.Options.Sum(o => o.Label.Length) + setting.Options.Count * 2 <= 46;

    /// <summary>
    /// One setting: its name and what it does on the left; its control (and a reset button while changed)
    /// on the right. <paramref name="siblings"/> are the settings listed with it: one that needs another
    /// of them is set in under it.
    /// </summary>
    private void SettingRow(StackPanel rows, Setting setting, IReadOnlyList<string> siblings)
    {
        var name = setting.FullName;
        var inUse = SettingsLayout.InUse(name, _settings.Get);
        var depth = 0;
        for (var at = name; depth < 3 && SettingsLayout.Needs.TryGetValue(at, out var need) && siblings.Contains(need.Parent); at = need.Parent) depth++;
        // Other settings wait on this one: the page is drawn again when it changes.
        var others = SettingsLayout.Needs.Values.Any(n => n.Parent == name);

        Border? reset = null;
        void Apply(string value)
        {
            if (!_settings.Set(name, value)) return;
            var theme = name == "General.SettingsTheme";
            Changed(theme);
            if (theme || others)
            {
                ShowPage(keepScroll: true);
                return;
            }
            if (reset is not null) reset.Visibility = _settings.IsChanged(name) ? Visibility.Visible : Visibility.Collapsed;
            _refreshPicture?.Invoke();
        }
        reset = IconButton(IconReset, $"Reset to the default: {DefaultWords(setting)}", () =>
        {
            if (!_settings.Set(name, setting.Default)) return;
            Changed(name == "General.SettingsTheme");
            ShowPage(keepScroll: true);
        });
        reset.Visibility = _settings.IsChanged(name) ? Visibility.Visible : Visibility.Collapsed;

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var glyph = depth == 0 ? SettingGlyphs.GetValueOrDefault(name, "") : "";
        Border row;
        // Settings about looks: a picture of each choice, under the words.
        if (PickerFor(setting, Apply) is { } picker)
        {
            right.Children.Add(reset);
            right.VerticalAlignment = VerticalAlignment.Top;
            row = Row(rows, setting.Name, setting.Description, right, hover: false, indent: depth, glyph: glyph);
            WordsOf(row)?.Children.Add(picker.View);
        }
        else if (WideText.Contains(name))
        {
            right.Children.Add(reset);
            right.VerticalAlignment = VerticalAlignment.Top;
            // (No icon: its words start at the card's edge, above the box, so an icon would sit on them.)
            row = Row(rows, setting.Name, setting.Description, right, hover: false, indent: depth, glyph: "");
            var field = TextField(_settings.Get(name), double.NaN, Hints.GetValueOrDefault(name, "Empty"), typed =>
            {
                if (!SettingsStore.Accepts(setting, typed)) return false;
                Apply(typed);
                return true;
            });
            field.Margin = new Thickness(0, 8, 0, 0);
            // Long text (the prompt, your instructions) wraps over several lines, so all of it can be read.
            if (name is "Assistant.Prompt" or "Assistant.Instructions" && field.Child is DockPanel { Children.Count: > 0 } line && line.Children[^1] is Grid { Children.Count: > 0 } inside && inside.Children[0] is TextBox text)
            {
                field.Height = double.NaN;
                field.MinHeight = name == "Assistant.Prompt" ? 88 : 32;
                field.Padding = new Thickness(10, 7, 10, 7);
                text.TextWrapping = TextWrapping.Wrap;
                text.VerticalContentAlignment = VerticalAlignment.Top;
                text.VerticalAlignment = VerticalAlignment.Top;
                inside.VerticalAlignment = VerticalAlignment.Top;
                text.MaxHeight = 220;
                text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                if (inside.Children.Count > 1 && inside.Children[1] is TextBlock hint) hint.VerticalAlignment = VerticalAlignment.Top;
            }
            if (WordsOf(row) is { } words)
            {
                words.Margin = new Thickness(0);
                words.Children.Add(field);
            }
        }
        else
        {
            reset.Margin = new Thickness(0, 0, 8, 0);
            right.Children.Add(reset);
            var control = Control(setting, Apply);
            control.SetValue(AutomationName, setting.Name);
            right.Children.Add(control);
            row = Row(rows, setting.Name, setting.Description, right, indent: depth, glyph: glyph);
        }
        // The theme's font is over this one while the theme is in use: said under it.
        if (name == "Themes.FontFamily" && _settings.Get("Characters.Enabled") == "true" && _settings.Get("Characters.UseFont") == "true" &&
            _packs.InstalledPack(_settings.Get("Characters.Pack")) is { Font.Length: > 0 } themed && _packs.FontOf(themed.Id) is not null && WordsOf(row) is { } fontSaid)
        {
            fontSaid.Children.Add(new TextBlock { Text = $"The theme's font ({themed.Font}) is in use. Change that on the Theme page.", FontSize = 12, Foreground = _accent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        }
        // The character pack's colour is over this one while the pack is in use: said under it.
        if (name == "Themes.AccentColorMode" && NyatchApp.PackAccent(_settings, _packs) is { } packColour && WordsOf(row) is { } said)
        {
            said.Children.Add(new TextBlock { Text = $"The theme's colour ({packColour}) is in use. Change that on the Theme page.", FontSize = 12, Foreground = _accent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        }
        if (inUse) return;
        // Not in use: dimmed, and it says what it waits on.
        row.Opacity = 0.45;
        row.IsEnabled = false;
        if (SettingsLayout.Needs.TryGetValue(name, out var needs) && _settings.Schema.Find(needs.Parent) is { } parent)
        {
            ToolTipService.SetShowOnDisabled(row, true);
            row.ToolTip = parent.Kind == SettingKind.Switch ? $"Turn on “{parent.Name}” to use this." : $"Used when “{parent.Name}” is {string.Join(" or ", needs.Values.Select(v => parent.Options.FirstOrDefault(o => o.Value == v)?.Label ?? v))}.";
        }
    }

    private static string DefaultWords(Setting setting) => setting.Kind switch
    {
        SettingKind.Switch => setting.Default == "true" ? "on" : "off",
        SettingKind.Choice => setting.Options.FirstOrDefault(o => o.Value == setting.Default)?.Label ?? setting.Default,
        _ => setting.Default.Length == 0 ? "empty" : setting.Default,
    };

    /// <summary>The control that suits a setting.</summary>
    private FrameworkElement Control(Setting setting, Action<string> apply)
    {
        var value = _settings.Get(setting.FullName);
        static int Number(string text) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : 0;
        static string Text(int n) => n.ToString(CultureInfo.InvariantCulture);

        if (setting.Kind == SettingKind.Switch) return Switch(value == "true", on => apply(on ? "true" : "false"), label: true);
        if (FitsSegments(setting)) return Segments(setting.Options.Select(o => (o.Value, o.Label)).ToList(), value, apply);
        if (setting.Kind == SettingKind.Choice)
        {
            var list = new ComboBox { Width = 220, Height = 32, DisplayMemberPath = nameof(SettingOption.Label), SelectedValuePath = nameof(SettingOption.Value), ItemsSource = setting.Options, SelectedValue = value, VerticalContentAlignment = VerticalAlignment.Center };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedValue is string chosen) apply(chosen);
            };
            return list;
        }
        if (Sliders.TryGetValue(setting.FullName, out var slide)) return SliderBox(slide.Min, slide.Max, slide.Step, slide.Unit, Number(value), n => apply(Text(n)));
        if (Steppers.TryGetValue(setting.FullName, out var nudge)) return Stepper(nudge.Min, nudge.Max, nudge.Step, nudge.Unit, Number(value), n => apply(Text(n)));
        if (setting.Kind == SettingKind.Number) return Stepper(-100000, 100000, 1, "", Number(value), n => apply(Text(n)));
        if (IsColour(setting)) return ColourWell(value, apply);
        if (setting.FullName == "Themes.FontFamily") return FontBox(value, apply);
        return TextField(value, 220, Hints.GetValueOrDefault(setting.FullName, setting.Default.Length == 0 ? "Empty" : ""), typed =>
        {
            if (!SettingsStore.Accepts(setting, typed)) return false;
            apply(typed);
            return true;
        });
    }

    /// <summary>The fonts on this PC, to choose from or type; empty is the usual font.</summary>
    private FrameworkElement FontBox(string value, Action<string> apply)
    {
        const string usual = "Usual font";
        var names = Fonts.SystemFontFamilies.Select(f => f.Source).Where(n => n.Length > 0 && !n.StartsWith('.')).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        // One typed in an earlier version that is not installed stays in the list while chosen.
        if (value.Length > 0 && !names.Contains(value, StringComparer.OrdinalIgnoreCase)) names.Insert(0, value);
        names.Insert(0, usual);
        var list = new ComboBox { Width = 220, Height = 32, ItemsSource = names, VerticalContentAlignment = VerticalAlignment.Center, IsTextSearchEnabled = true, ToolTip = "Type a few letters to find a font" };
        list.SelectedItem = value.Length == 0 ? usual : names.First(n => n.Equals(value, StringComparison.OrdinalIgnoreCase));
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string chosen) apply(chosen == usual ? "" : chosen);
        };
        return list;
    }
}

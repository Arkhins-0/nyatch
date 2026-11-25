namespace Nyatch.Core;

/// <summary>
/// A titled part of a settings page: its settings, in the order they are shown. <paramref name="Key"/>
/// names the notch page the part belongs to ("weather"), on the pages that list a notch's pages.
/// </summary>
public sealed record LayoutSection(string Title, string[] Settings, string Key = "", string Note = "");

/// <summary>
/// A page of the settings window. <paramref name="Special"/> are settings the page shows with a control
/// of its own (a hotkey recorder, the page list) rather than as rows.
/// </summary>
public sealed record LayoutPage(string Id, string Group, string Name, string Title, string About, LayoutSection[] Sections, string[]? Special = null)
{
    public IEnumerable<string> Settings => Sections.SelectMany(s => s.Settings).Concat(Special ?? []);
}

/// <summary>
/// Where every setting lives in the settings window. Each setting is in one place only: on one page,
/// either in a section or as one of the page's special controls. The pages are grouped by what someone
/// comes to do, not by which notch draws a feature: General first, then the Features (each on a page of
/// its own, named as people know it: Quick Lookup, Media & lyrics...), the Look, the Notches (where each
/// sits and which pages it has) and the LLM.
/// </summary>
public static class SettingsLayout
{
    public const string General = "General", Features = "Features", Look = "Look", Notches = "Notches", Llm = "LLM";

    /// <summary>The sidebar's groups, in order.</summary>
    public static readonly IReadOnlyList<string> Groups = [General, Features, Look, Notches, Llm];

    public static readonly IReadOnlyList<LayoutPage> Pages =
    [
        new("general", General, "General", "General", "Starting, language and this window.",
        [
            new("Startup", ["General.LaunchAtStartup"]),
            new("Language", ["Modules.Language"]),
            new("This window", ["General.SettingsTheme"]),
        ]),
        new("shortcuts", General, "Shortcuts", "Shortcuts", "Every hotkey of Nyatch, in one place.", [],
        [
            "Shortcuts.HideShowHotkeyEnabled", "Shortcuts.HideShowModifiers", "Shortcuts.HideShowKey",
            "Shortcuts.LookupModifiers", "Shortcuts.LookupKey",
            "Shortcuts.DeckHotkeyEnabled", "Shortcuts.DeckModifiers", "Shortcuts.DeckKey", "Deck.PageKeys",
            "Shortcuts.SideHotkeyEnabled", "Shortcuts.SideModifiers", "Shortcuts.SideKey",
        ]),
        new("updates", General, "Updates", "Updates", "Nyatch updates itself from its releases.",
        [
            new("Automatic", ["Updates.AutoCheck", "Updates.AutoInstall"]),
        ]),
        new("about", General, "About", "About", "The creator and the copyright.", []),
        new("terms", General, "Terms", "Terms", "The services Nyatch uses, and its licence.", []),
        new("notice", General, "Notice", "Notice", "The notice that comes with Nyatch.", []),

        new("search", Features, "Quick Lookup", "Quick Lookup", "A search box at the top of the screen, opened with its hotkey: files, apps, sums and the web.",
        [
            new("Quick Lookup", ["Modules.QuickLookup"]),
            new("What it finds", ["Modules.QuickLookupFiles", "Modules.QuickLookupWeb", "Modules.Calculator"]),
        ]),
        new("media", Features, "Media & lyrics", "Media & lyrics", "The player and the lyrics in the top notch.",
        [
            new("Player", ["Modules.Media", "Modules.MediaAutoExpand", "Modules.MediaExpandBlocklist", "Appearance.ProgressStyle", "Appearance.SpectrumStyle", "Themes.CoverTint"]),
            new("Lyrics", ["Modules.Lyrics", "Modules.LyricsSource", "Modules.MediaTabLyrics", "Modules.LyricsVisualizer", "Modules.KaraokeLyrics", "Modules.KaraokeLetterGlow", "Modules.LyricsNeighborLinesVisible"]),
            new("Lyrics timing", ["Modules.LyricsOffsetMs", "Modules.LyricsOffsetBluetoothMs"]),
            new("Lyrics on this PC", ["Modules.LyricsCache", "Modules.LyricsCacheMaxMB"]),
        ]),
        new("alerts", Features, "Alerts", "Alerts", "Short notices the top notch shows when something changes.",
        [
            new("System", ["Modules.Volume", "Modules.Brightness", "Modules.CapsLock", "Modules.Battery", "Modules.Progress", "Modules.Clipboard"]),
            new("Bluetooth", ["Modules.BluetoothIndicator", "Modules.BluetoothShowBattery"]),
            new("Do not disturb", ["Modules.DoNotDisturbIndicator", "Modules.NotificationRespectDnD"]),
            new("Notices", ["Modules.StatusCountdownProgress"]),
            new("Privacy", ["Indicators.PrivacyDots", "Indicators.PrivacyDotsMic", "Indicators.PrivacyDotsMicHex", "Indicators.PrivacyDotsCam", "Indicators.PrivacyDotsCamHex"]),
        ]),
        new("focus", Features, "Focus & timers", "Focus & timers", "The timer, the stopwatch and the alarms on the second notch's Focus page.",
        [
            new("Timer", ["Deck.TimerMinutes"]),
            new("When it rings", ["Deck.FocusSound", "Deck.TimerSound", "Deck.AlarmSound", "Deck.RingVolume", "Deck.RingLowersOthers", "Deck.SnoozeMinutes", "Deck.FocusNotify"]),
            new("Top notch menu", ["Modules.TimerModule"]),
        ]),
        new("deck-files", Features, "Apps & files", "Apps & files", "The Apps, Tray and Clipboard pages of the second notch.",
        [
            new("Apps", ["Deck.AppsPinned", "Deck.AppsRecent", "Deck.AppsRecentCount"]),
            new("Tray", ["Modules.FileTrayMaxItems", "Modules.FileTrayPersist"]),
            new("Clipboard", ["Deck.ClipboardRemember", "Deck.ClipboardKept", "Deck.ClipboardPictures"]),
        ]),
        new("deck-tools", Features, "Terminal & Online", "Terminal & Online", "The Terminal and Online pages of the second notch.",
        [
            new("Terminal", ["Deck.TerminalShell", "Deck.TerminalFolder"]),
            new("Online", ["Deck.PingHost"]),
        ]),
        new("game", Features, "Game overlay", "Game overlay", "Frames per second and how busy the PC is, at the top while a game is full screen.",
        [
            new("Game overlay", ["Modules.GameOverlay", "Modules.GameOverlayCompact", "Modules.ShowMetricText"]),
            new("What it shows", ["Modules.GameOverlayShowFps", "Modules.GameOverlayShowCpu", "Modules.GameOverlayShowGpu", "Modules.GameOverlayShowRam", "Modules.GameOverlayShowDisk"]),
        ]),
        new("explorer", Features, "File Explorer", "File Explorer", "Folder sizes and clearer units in File Explorer.",
        [
            new("Folder sizes", ["Explorer.Enabled", "Explorer.FolderSizes", "Explorer.MixFoldersWhenSorting"]),
            new("Units", ["Explorer.SizeUnits", "Explorer.IecUnits"]),
        ]),

        new("appearance", Look, "Appearance", "Appearance", "Theme, shape, material and text of both notches.",
        [
            new("Colours", ["Themes.ThemePreset", "Themes.AccentColorMode", "Themes.CustomAccentHex", "Themes.AccentBloom"]),
            new("Shape", ["Appearance.ShapeStyle", "Themes.ContourBorderMode"]),
            new("Material", ["Themes.PillOpacity", "Themes.TintIntensity", "Themes.BackdropMaterial", "Themes.BackdropFillOpacity", "Themes.BackdropTint", "Themes.MaterialDepth", "Themes.DropShadow"]),
            new("Text", ["Themes.FontFamily", "Themes.TextScale"]),
            new("Custom colours", ["Themes.PillBgColor", "Themes.TextPrimaryColor", "Themes.TextSecondaryColor", "Themes.ContourBorderHex"]),
        ]),
        new("motion", Look, "Motion", "Motion", "How the notches move.",
        [
            new("Movement", ["Animations.AnimationStyle", "Animations.AnimationSpeed", "Animations.TargetFPS"]),
            new("Player", ["Animations.ExpandedMediaTransitions", "Animations.PillCoverSpin"]),
        ]),
        new("clock", Look, "Clock & date", "Clock & date", "The time and the date, as both notches write them.",
        [
            new("Time", ["Modules.ClockFormat", "Modules.ShowSeconds"]),
            new("Date", ["Modules.DateStyle", "Modules.DateFormat"]),
            new("In the top notch", ["Modules.ClockNote", "Modules.DateFirst", "Themes.ClockAccentGlow", "Modules.MediaPillClock"]),
        ]),
        new("characters", Look, "Theme", "Theme", "A theme for the notches: a picture for each moment on the open pages, a colour and a font.",
        [
            new("Theme", ["Characters.Enabled", "Characters.UseAccent", "Characters.UseFont", "Characters.AutoUpdate"]),
        ], Special: ["Characters.Pack"]),

        new("top-layout", Notches, "Top notch", "Top notch", "Where the top notch sits, how big it is and when it shows.",
        [
            new("Place", ["Appearance.Position", "Appearance.TargetMonitor", "Appearance.OffsetX", "Appearance.OffsetY", "Appearance.SeparateExpandedOffsetY", "Appearance.OffsetYExpanded", "Appearance.BorderMergedMode"]),
            new("Size", ["Appearance.SizeScale", "Appearance.AutoDpiScale"]),
            new("Behaviour", ["Behavior.AlwaysOnTop", "Behavior.ExpandOnHover", "Behavior.AutoHideIdleSeconds", "Behavior.AutoHideFullscreen", "Behavior.UnhideOnHover"]),
        ]),
        new("top-pages", Notches, "Top notch pages", "Top notch pages", "The pages the top notch turns through, and each page's own settings.",
        [
            new("Calendar", ["Modules.FirstDayOfWeek", "Themes.CalendarAccent"], Key: "calendar"),
            new("Weather", ["Modules.Weather", "Modules.WeatherCity", "Modules.WeatherFahrenheit"], Key: "weather"),
        ], ["Appearance.TopPages", "Modules.FileTrayModule"]),
        new("deck-layout", Notches, "Second notch", "Second notch", "Where the second notch sits and what it shows while closed.",
        [
            new("Second notch", ["Deck.Enabled"]),
            new("Place", ["Deck.Position", "Deck.OffsetX", "Deck.OffsetY"]),
            new("Behaviour", ["Deck.ExpandOnHover", "Deck.AlwaysOnTop"]),
            new("When closed", ["Deck.Shows"]),
        ], ["Deck.Emoji"]),
        new("deck-pages", Notches, "Second notch pages", "Second notch pages", "The pages of the second notch: their order, and which are on it.", [], ["Deck.Pages"]),

        // The LLM panel: the services (a key and a model each), the saved chats, and where the panel sits.
        new("chats", Llm, "Services", "LLM services", "Connect a key for each LLM you use: its chat comes into the LLM panel.",
        [
            new("Answers", ["Assistant.Prompt", "Assistant.Instructions"]),
            new("Conversations", ["Assistant.KeepHistory"]),
        ], ["Assistant.ClaudeModel", "Assistant.ChatGptModel", "Assistant.GeminiModel", "Assistant.DeepSeekModel", "Assistant.MistralModel", "Assistant.GrokModel", "Assistant.GroqModel", "Assistant.PerplexityModel", "Assistant.OpenRouterModel"]),
        new("history", Llm, "Chat history", "Chat history", "Saved conversations, with every LLM.", []),
        new("side-layout", Llm, "Panel", "LLM panel", "Where the LLM panel sits on the screen, and how tall it is.",
        [
            new("LLM panel", ["Side.Enabled"]),
            new("Place", ["Side.Edge", "Side.Anchor", "Side.Height"]),
        ]),
    ];

    /// <summary>
    /// Settings that only matter while another has one of the given values. The window dims them
    /// otherwise, so a page reads as what is in use.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Parent, string[] Values)> Needs = new Dictionary<string, (string, string[])>
    {
        ["Themes.CustomAccentHex"] = ("Themes.AccentColorMode", ["custom"]),
        ["Themes.BackdropFillOpacity"] = ("Themes.BackdropMaterial", ["blur", "acrylic"]),
        ["Themes.BackdropTint"] = ("Themes.BackdropMaterial", ["acrylic"]),
        ["Modules.DateFormat"] = ("Modules.DateStyle", ["custom"]),
        ["Appearance.OffsetYExpanded"] = ("Appearance.SeparateExpandedOffsetY", ["true"]),
        ["Modules.WeatherCity"] = ("Modules.Weather", ["true"]),
        ["Modules.WeatherFahrenheit"] = ("Modules.Weather", ["true"]),
        ["Modules.MediaAutoExpand"] = ("Modules.Media", ["true"]),
        ["Modules.MediaExpandBlocklist"] = ("Modules.MediaAutoExpand", ["true"]),
        ["Appearance.ProgressStyle"] = ("Modules.Media", ["true"]),
        ["Appearance.SpectrumStyle"] = ("Modules.Media", ["true"]),
        ["Themes.CoverTint"] = ("Modules.Media", ["true"]),
        ["Modules.MediaPillClock"] = ("Modules.Media", ["true"]),
        ["Modules.MediaTabLyrics"] = ("Modules.Lyrics", ["true"]),
        ["Modules.LyricsSource"] = ("Modules.Lyrics", ["true"]),
        ["Modules.LyricsVisualizer"] = ("Modules.Lyrics", ["true"]),
        ["Modules.KaraokeLyrics"] = ("Modules.Lyrics", ["true"]),
        ["Modules.KaraokeLetterGlow"] = ("Modules.KaraokeLyrics", ["true"]),
        ["Modules.LyricsNeighborLinesVisible"] = ("Modules.Lyrics", ["true"]),
        ["Modules.LyricsOffsetMs"] = ("Modules.Lyrics", ["true"]),
        ["Modules.LyricsOffsetBluetoothMs"] = ("Modules.Lyrics", ["true"]),
        ["Modules.LyricsCache"] = ("Modules.Lyrics", ["true"]),
        ["Modules.LyricsCacheMaxMB"] = ("Modules.LyricsCache", ["true"]),
        ["Modules.BluetoothShowBattery"] = ("Modules.BluetoothIndicator", ["true"]),
        ["Indicators.PrivacyDotsMic"] = ("Indicators.PrivacyDots", ["true"]),
        ["Indicators.PrivacyDotsMicHex"] = ("Indicators.PrivacyDotsMic", ["true"]),
        ["Indicators.PrivacyDotsCam"] = ("Indicators.PrivacyDots", ["true"]),
        ["Indicators.PrivacyDotsCamHex"] = ("Indicators.PrivacyDotsCam", ["true"]),
        ["Modules.QuickLookupFiles"] = ("Modules.QuickLookup", ["true"]),
        ["Modules.QuickLookupWeb"] = ("Modules.QuickLookup", ["true"]),
        ["Modules.Calculator"] = ("Modules.QuickLookup", ["true"]),
        ["Modules.GameOverlayCompact"] = ("Modules.GameOverlay", ["true"]),
        ["Modules.ShowMetricText"] = ("Modules.GameOverlay", ["true"]),
        ["Modules.GameOverlayShowFps"] = ("Modules.GameOverlay", ["true"]),
        ["Modules.GameOverlayShowCpu"] = ("Modules.GameOverlay", ["true"]),
        ["Modules.GameOverlayShowGpu"] = ("Modules.GameOverlay", ["true"]),
        ["Modules.GameOverlayShowRam"] = ("Modules.GameOverlay", ["true"]),
        ["Modules.GameOverlayShowDisk"] = ("Modules.GameOverlay", ["true"]),
        ["Side.Edge"] = ("Side.Enabled", ["true"]),
        ["Side.Anchor"] = ("Side.Enabled", ["true"]),
        ["Side.Height"] = ("Side.Enabled", ["true"]),
        ["Deck.Position"] = ("Deck.Enabled", ["true"]),
        ["Deck.OffsetX"] = ("Deck.Enabled", ["true"]),
        ["Deck.OffsetY"] = ("Deck.Enabled", ["true"]),
        ["Deck.ExpandOnHover"] = ("Deck.Enabled", ["true"]),
        ["Deck.AlwaysOnTop"] = ("Deck.Enabled", ["true"]),
        ["Deck.Shows"] = ("Deck.Enabled", ["true"]),
        ["Deck.AppsRecentCount"] = ("Deck.AppsRecent", ["true"]),
        ["Deck.ClipboardKept"] = ("Deck.ClipboardRemember", ["true"]),
        ["Deck.ClipboardPictures"] = ("Deck.ClipboardRemember", ["true"]),
        ["Explorer.FolderSizes"] = ("Explorer.Enabled", ["true"]),
        ["Characters.UseAccent"] = ("Characters.Enabled", ["true"]),
        ["Characters.UseFont"] = ("Characters.Enabled", ["true"]),
        ["Explorer.MixFoldersWhenSorting"] = ("Explorer.Enabled", ["true"]),
        ["Explorer.SizeUnits"] = ("Explorer.Enabled", ["true"]),
        ["Explorer.IecUnits"] = ("Explorer.Enabled", ["true"]),
        ["Updates.AutoInstall"] = ("Updates.AutoCheck", ["true"]),
        ["Deck.TimerSound"] = ("Deck.FocusSound", ["true"]),
        ["Deck.AlarmSound"] = ("Deck.FocusSound", ["true"]),
        ["Deck.RingVolume"] = ("Deck.FocusSound", ["true"]),
        ["Deck.RingLowersOthers"] = ("Deck.FocusSound", ["true"]),
    };

    public static LayoutPage? Page(string id) => Pages.FirstOrDefault(p => p.Id == id);

    /// <summary>The page a setting is on.</summary>
    public static LayoutPage? PageOf(string setting) => Pages.FirstOrDefault(p => p.Settings.Contains(setting));

    /// <summary>
    /// Whether a setting is in use: every setting it needs, up the chain, has a value that turns it on.
    /// </summary>
    public static bool InUse(string setting, Func<string, string> get)
    {
        for (var hops = 0; hops < 8 && Needs.TryGetValue(setting, out var need); hops++)
        {
            if (!need.Values.Contains(get(need.Parent))) return false;
            setting = need.Parent;
        }
        return true;
    }

    /// <summary>Settings of the schema that no page shows (a setting added without a place).</summary>
    public static IEnumerable<Setting> Unplaced(SettingsSchema schema)
    {
        var placed = Pages.SelectMany(p => p.Settings).ToHashSet(StringComparer.Ordinal);
        return schema.All.Where(s => !placed.Contains(s.FullName));
    }
}

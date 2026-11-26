namespace Nyatch.Core;

/// <summary>How a place shows a picture: fitted in a square, in a tall box, cut round, or small (a face).</summary>
public enum SlotShape { Square, Tall, Round, Face }

/// <summary>
/// One picture slot of a character pack: its file name (without .png or .gif), where it shows and when,
/// the names tried when the pack has no picture for it, and how big the place draws it (DIPs).
/// </summary>
public sealed record CharacterSlot(string Name, string Place, string When, string[] FallsBackTo, SlotShape Shape, int Width, int Height, bool Core = false)
{
    /// <summary>The names tried for this place, in order: its own, then the fallbacks.</summary>
    public IEnumerable<string> Chain => FallsBackTo.Prepend(Name);
}

/// <summary>
/// Every picture slot Nyatch draws a character in (the island reads the same names; see
/// the Miku pack's README.md, in nyatch themes/miku). A pack needs <c>idle</c>; anything else falls back.
/// </summary>
public static class CharacterSlots
{
    public const string General = "General", Time = "Time page", Calendar = "Calendar", Weather = "Weather", Hardware = "Hardware",
        Tray = "File tray", Player = "Player and lyrics", Lookup = "Quick Lookup", Notices = "Notices", Second = "Second notch",
        Side = "LLM panel", Settings = "Settings";

    /// <summary>The places, in the order they are listed.</summary>
    public static readonly IReadOnlyList<string> Places = [General, Time, Calendar, Weather, Hardware, Tray, Player, Lookup, Notices, Second, Side, Settings];

    private static CharacterSlot S(string name, string place, string when, string fallbacks, SlotShape shape, int w, int h = 0, bool core = false) =>
        new(name, place, when, fallbacks.Length == 0 ? [] : fallbacks.Split(' '), shape, w, h == 0 ? w : h, core);

    public static readonly IReadOnlyList<CharacterSlot> All =
    [
        // General pictures: used wherever a place has none of its own.
        S("idle", General, "Any place without its own picture", "", SlotShape.Square, 132, core: true),
        S("happy", General, "Waving: a new chat, a task ticked, an empty list", "idle", SlotShape.Square, 112),
        S("sleeping", General, "Night, Do not disturb, a long break", "idle", SlotShape.Square, 112),
        S("confused", General, "Nothing found, an empty page", "idle", SlotShape.Square, 112),
        S("shocked", General, "An error, a storm, a busy PC", "idle", SlotShape.Square, 112),
        S("sorry", General, "Something failed, offline", "idle", SlotShape.Square, 112),
        S("cool", General, "Sunny, a fast connection, a quiet PC", "idle", SlotShape.Square, 112),
        S("thinking", General, "An answer is on its way", "idle", SlotShape.Square, 48),
        S("searching", General, "Looking something up", "idle", SlotShape.Square, 52),
        S("waiting-snail", General, "Slow: an answer, the connection", "idle", SlotShape.Square, 52),
        S("done", General, "Done: a file caught, a download", "happy idle", SlotShape.Square, 64),
        S("party", General, "Every to-do ticked", "happy idle", SlotShape.Square, 64),
        S("listening", General, "Music playing", "idle", SlotShape.Square, 52),
        S("headpat", General, "The end of a list", "happy idle", SlotShape.Square, 64),
        S("surprised", General, "Wind, Caps Lock", "shocked idle", SlotShape.Square, 88),
        S("grossed-out", General, "Fog, haze and smoke", "idle", SlotShape.Square, 88),
        S("angry", General, "Very hot weather", "idle", SlotShape.Square, 88),
        S("dance", General, "A command or the stopwatch running", "idle", SlotShape.Square, 104),
        S("face", General, "Small places: the closed notch, the side panel's tab", "happy idle", SlotShape.Face, 24),

        S("time-morning", Time, "5:00 to 10:00", "idle", SlotShape.Square, 132),
        S("time-day", Time, "10:00 to 18:00", "idle", SlotShape.Square, 132),
        S("time-evening", Time, "18:00 to 22:00", "idle", SlotShape.Square, 132),
        S("time-night", Time, "22:00 to 5:00", "sleeping idle", SlotShape.Square, 132),
        S("time-hour", Time, "The first seconds of each hour", "happy idle", SlotShape.Square, 132),

        S("calendar", Calendar, "Any day (upright)", "idle", SlotShape.Tall, 100, 160),
        S("calendar-weekend", Calendar, "Saturday and Sunday", "calendar happy idle", SlotShape.Tall, 100, 160),
        S("calendar-friday", Calendar, "Friday", "calendar happy idle", SlotShape.Tall, 100, 160),
        S("calendar-monday", Calendar, "Monday", "calendar idle", SlotShape.Tall, 100, 160),

        S("weather-sun", Weather, "Clear, by day", "cool idle", SlotShape.Square, 88),
        S("weather-night", Weather, "At night", "sleeping idle", SlotShape.Square, 88),
        S("weather-cloud", Weather, "Cloudy", "idle", SlotShape.Square, 88),
        S("weather-rain", Weather, "Rain", "sorry idle", SlotShape.Square, 88),
        S("weather-storm", Weather, "Thunder", "shocked idle", SlotShape.Square, 88),
        S("weather-snow", Weather, "Snow and sleet", "idle", SlotShape.Square, 88),
        S("weather-fog", Weather, "Fog, haze and smoke", "grossed-out idle", SlotShape.Square, 88),
        S("weather-hot", Weather, "35 °C (95 °F) and up", "angry idle", SlotShape.Square, 88),
        S("weather-wind", Weather, "Wind of 30 km/h and up", "surprised idle", SlotShape.Square, 88),
        S("weather-loading", Weather, "Before the first reading", "searching idle", SlotShape.Square, 88),

        S("hardware-idle", Hardware, "Under 40% load", "cool idle", SlotShape.Round, 96),
        S("hardware-work", Hardware, "40% to 80% load", "idle", SlotShape.Round, 96),
        S("hardware-busy", Hardware, "Over 80% load", "shocked idle", SlotShape.Round, 96),
        S("hardware-memory", Hardware, "Memory over 92% full", "sorry idle", SlotShape.Round, 96),
        S("hardware-download", Hardware, "Downloading at 50 Mb/s and up", "done idle", SlotShape.Round, 96),

        S("tray-empty", Tray, "The tray is empty", "confused idle", SlotShape.Square, 70),
        S("tray-end", Tray, "Under the last file", "headpat happy idle", SlotShape.Square, 64),
        S("tray-catch", Tray, "A file dragged over", "done happy idle", SlotShape.Square, 70),

        S("player", Player, "A song playing (still while paused)", "listening idle", SlotShape.Square, 52),
        S("player-paused", Player, "A song paused", "", SlotShape.Square, 52),
        S("lyrics", Player, "Singing along", "listening idle", SlotShape.Square, 50),
        S("lyrics-end", Player, "The last line", "happy idle", SlotShape.Square, 50),
        S("lyrics-fetching", Player, "Lyrics being looked for", "searching idle", SlotShape.Square, 50),
        S("lyrics-none", Player, "No lyrics found", "confused idle", SlotShape.Square, 50),

        S("lookup", Lookup, "Waiting for words", "happy idle", SlotShape.Face, 28),
        S("lookup-searching", Lookup, "Looking", "searching thinking idle", SlotShape.Face, 28),
        S("lookup-found", Lookup, "Found", "done happy idle", SlotShape.Face, 28),
        S("lookup-none", Lookup, "Nothing found", "confused idle", SlotShape.Face, 28),

        S("notice-message", Notices, "A notification", "happy idle", SlotShape.Face, 44),
        S("notice-capslock", Notices, "Caps Lock, Num Lock or Scroll Lock", "surprised shocked idle", SlotShape.Face, 44),
        S("ringing", Notices, "An alarm or the timer going off", "surprised shocked idle", SlotShape.Face, 44),
        S("notice-battery", Notices, "Battery low", "sleeping idle", SlotShape.Face, 44),
        S("notice-copied", Notices, "Something copied", "done happy idle", SlotShape.Face, 44),
        S("notice-device", Notices, "A device or Bluetooth", "happy idle", SlotShape.Face, 44),
        S("notice-dnd", Notices, "Do not disturb", "sleeping idle", SlotShape.Face, 44),
        S("notice-volume", Notices, "Volume", "listening idle", SlotShape.Face, 44),
        S("notice-brightness", Notices, "Brightness", "cool idle", SlotShape.Face, 44),
        S("notice", Notices, "Any other notice", "idle", SlotShape.Face, 44),

        S("notifications-empty", Second, "No notifications", "happy idle", SlotShape.Square, 112),
        S("notifications-dnd", Second, "None, with Do not disturb on", "sleeping idle", SlotShape.Square, 112),
        S("notifications-end", Second, "After the last notification", "headpat happy idle", SlotShape.Square, 64),
        S("clipboard-empty", Second, "Nothing copied", "confused idle", SlotShape.Square, 112),
        S("clipboard-end", Second, "After the last copied item", "headpat happy idle", SlotShape.Square, 64),
        S("apps-end", Second, "After the last app", "happy idle", SlotShape.Square, 64),
        S("focus-work", Second, "The timer running", "cool idle", SlotShape.Square, 104),
        S("focus-stopwatch", Second, "The stopwatch running", "dance idle", SlotShape.Square, 104),
        S("focus-idle", Second, "Nothing running", "idle", SlotShape.Square, 104),
        S("alarm-snoozed", Second, "An alarm snoozed", "sleeping idle", SlotShape.Square, 104),
        S("notes-empty", Second, "No notes", "confused idle", SlotShape.Square, 112),
        S("notes-done", Second, "Every to-do ticked", "party happy idle", SlotShape.Square, 64),
        S("terminal-empty", Second, "No terminal open", "cool idle", SlotShape.Square, 112),
        S("terminal-idle", Second, "At the prompt", "cool idle", SlotShape.Square, 52),
        S("terminal-running", Second, "A command running", "dance idle", SlotShape.Square, 52),
        S("online", Second, "The connection", "idle", SlotShape.Square, 52),
        S("online-testing", Second, "The speed test running", "searching idle", SlotShape.Square, 52),
        S("online-fast", Second, "100 Mbps and up", "cool idle", SlotShape.Square, 52),
        S("online-slow", Second, "Under 10 Mbps", "waiting-snail idle", SlotShape.Square, 52),
        S("online-offline", Second, "Offline", "sorry idle", SlotShape.Square, 52),
        S("picker", Second, "The page picker", "happy idle", SlotShape.Square, 104),
        S("closed", Second, "The closed second notch, when its mark is the character", "face happy idle", SlotShape.Face, 24),

        S("chat-new", Side, "A new chat", "happy idle", SlotShape.Square, 112),
        S("chat-history-empty", Side, "No past chats", "confused idle", SlotShape.Square, 112),
        S("chat-key", Side, "No key set", "confused idle", SlotShape.Square, 112),
        S("chat-thinking", Side, "Waiting for the answer", "thinking idle", SlotShape.Square, 48),
        S("chat-answered", Side, "The answer just came", "happy idle", SlotShape.Square, 48),
        S("chat-failed", Side, "The answer failed", "sorry idle", SlotShape.Square, 48),
        S("side", Side, "The LLM panel's closed tab", "face happy idle", SlotShape.Face, 24),

        S("about", Settings, "Settings, About", "happy idle", SlotShape.Square, 64),
        S("updates", Settings, "Settings, Updates, when up to date", "cool happy idle", SlotShape.Square, 40),
    ];

    public static CharacterSlot? Find(string name) => All.FirstOrDefault(s => s.Name == name);

    /// <summary>The page of its place a slot is on, where the place has several (the second notch's pages), else null.</summary>
    public static string? PageOf(CharacterSlot slot) => slot.Place != Second ? null : slot.Name.Split('-')[0] switch
    {
        "notifications" => "Notifications",
        "clipboard" => "Clipboard",
        "apps" => "Apps",
        "focus" => "Focus",
        "notes" => "Notes",
        "terminal" => "Terminal",
        "online" => "Online",
        "picker" => "Page picker",
        "closed" => "Closed notch",
        _ => null,
    };

    /// <summary>The picture a place would show from a pack that has <paramref name="has"/>: the first of its chain, or null.</summary>
    public static string? Resolve(CharacterSlot slot, Func<string, bool> has) => slot.Chain.FirstOrDefault(has);
}

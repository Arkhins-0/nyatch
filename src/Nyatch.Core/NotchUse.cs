namespace Nyatch.Core;

/// <summary>
/// Whether the second notch and the LLM panel are in use. One turned off is hidden, not gone: while
/// its hotkey is on, the hotkey still opens it, and it hides again when it closes. Off with its hotkey
/// off too, it is not used at all (and the chats go back onto the second notch).
/// </summary>
public static class NotchUse
{
    public static bool DeckInUse(SettingsStore settings) =>
        settings.Get("Deck.Enabled") == "true" || settings.Get("Shortcuts.DeckHotkeyEnabled") == "true";

    public static bool SideInUse(SettingsStore settings) =>
        settings.Get("Side.Enabled") == "true" || settings.Get("Shortcuts.SideHotkeyEnabled") == "true";

    /// <summary>What the island reads: in use or not, and whether it shows only while open.</summary>
    public static void Derive(SettingsStore settings, IDictionary<string, string> derived)
    {
        var deck = DeckInUse(settings);
        derived["Deck.Enabled"] = deck ? "true" : "false";
        derived["Deck.HiddenAtRest"] = deck && settings.Get("Deck.Enabled") != "true" ? "true" : "false";
        var side = SideInUse(settings);
        derived["Side.Enabled"] = side ? "true" : "false";
        derived["Side.HiddenAtRest"] = side && settings.Get("Side.Enabled") != "true" ? "true" : "false";
    }
}

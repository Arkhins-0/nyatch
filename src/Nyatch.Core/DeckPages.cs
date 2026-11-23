using System;
using System.Collections.Generic;
using System.Linq;

namespace Nyatch.Core;

/// <summary>Whether a page of the second notch is on it. Any page may also have a letter for the hotkey, or none.</summary>
public enum PageShow
{
    /// <summary>On the notch: the wheel and the dots reach it, and so does its letter if it has one.</summary>
    Shown,

    /// <summary>Only by the hotkey and its letter: the wheel and the dots pass it by.</summary>
    HotkeyOnly,

    /// <summary>Not at all.</summary>
    Hidden,
}

/// <summary>
/// The second notch's pages as settings: their order and how each is reached (Deck.Pages:
/// "apps,~gemini,-online", a tilde for hotkey only and a minus for hidden), and the letter that opens
/// each after the hotkey (Deck.PageKeys: "apps=A,gemini=G,online=", empty for no letter).
/// </summary>
public static class DeckPages
{
    /// <summary>Every page, in the order a new install has them, with its letter.</summary>
    public static readonly IReadOnlyList<(string Id, char Key)> All =
    [
        ("apps", 'A'), ("tray", 'F'), ("clipboard", 'V'), ("notifications", 'B'), ("claude", 'C'), ("chatgpt", 'P'),
        ("gemini", 'G'), ("focus", 'T'), ("notes", 'N'), ("terminal", 'R'), ("online", 'O'),
    ];

    /// <summary>The saved order; pages it does not name (new ones) come at the end, shown.</summary>
    public static List<(string Id, PageShow Show)> Order(string value)
    {
        var order = new List<(string Id, PageShow Show)>();
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var id = raw.TrimStart('-', '~');
            if (All.All(p => p.Id != id) || order.Any(o => o.Id == id)) continue;
            order.Add((id, raw.StartsWith('-') ? PageShow.Hidden : raw.StartsWith('~') ? PageShow.HotkeyOnly : PageShow.Shown));
        }
        foreach (var (id, _) in All)
        {
            if (order.All(o => o.Id != id)) order.Add((id, PageShow.Shown));
        }
        return order;
    }

    public static string OrderText(IEnumerable<(string Id, PageShow Show)> order) =>
        string.Join(",", order.Select(o => (o.Show switch { PageShow.Hidden => "-", PageShow.HotkeyOnly => "~", _ => "" }) + o.Id));

    /// <summary>
    /// Each page's letter: the saved ones, the usual one for the rest (unless another page took it).
    /// A page saved as "notes=" has no letter, and the hotkey's picker leaves it out.
    /// </summary>
    public static Dictionary<string, char> Keys(string value)
    {
        var keys = new Dictionary<string, char>(StringComparer.Ordinal);
        var none = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = pair.IndexOf('=');
            if (at <= 0 || at < pair.Length - 2) continue;
            var id = pair[..at];
            if (All.All(p => p.Id != id) || keys.ContainsKey(id) || none.Contains(id)) continue;
            if (at == pair.Length - 1)
            {
                none.Add(id);
                continue;
            }
            var key = char.ToUpperInvariant(pair[^1]);
            if (IsKey(key) && !keys.ContainsValue(key)) keys[id] = key;
        }
        foreach (var (id, key) in All)
        {
            if (!keys.ContainsKey(id) && !none.Contains(id) && !keys.ContainsValue(key)) keys[id] = key;
        }
        return keys;
    }

    /// <summary>The letters to save: every page, "id=" for one without a letter.</summary>
    public static string KeysText(IReadOnlyDictionary<string, char> keys) =>
        string.Join(",", All.Select(p => keys.TryGetValue(p.Id, out var key) ? $"{p.Id}={key}" : $"{p.Id}="));

    /// <summary>Give a page a letter if it has none: its usual one, else the first free one ('\0' when all are taken).</summary>
    public static char GiveKey(Dictionary<string, char> keys, string id)
    {
        if (keys.TryGetValue(id, out var have)) return have;
        var usual = All.FirstOrDefault(p => p.Id == id).Key;
        var free = usual != '\0' && !keys.ContainsValue(usual) ? usual
            : "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".FirstOrDefault(c => !keys.ContainsValue(c));
        if (free != '\0') keys[id] = free;
        return free;
    }

    /// <summary>A letter or a digit: what can pick a page.</summary>
    public static bool IsKey(char key) => key is >= 'A' and <= 'Z' or >= '0' and <= '9';

    /// <summary>The hotkey in words: "Ctrl + Alt + N".</summary>
    public static string HotkeyWords(string modifiers, string key)
    {
        var parts = new List<string>();
        foreach (var part in modifiers.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            parts.Add(part switch { "ctrl" => "Ctrl", "alt" => "Alt", "shift" => "Shift", "win" => "Win", _ => part });
        }
        parts.Add(key.Length == 1 ? key.ToUpperInvariant() : key);
        return string.Join(" + ", parts);
    }
}

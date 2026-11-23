using System.Text;
using System.Text.Json;

namespace Nyatch.Core;

/// <summary>
/// The user's settings. Only values that differ from the default are kept
/// (settings.json); the island reads a flat file with every setting spelled
/// out (settings.ini), which this writes.
/// </summary>
public sealed class SettingsStore
{
    private readonly SettingsSchema _schema;
    private readonly string _folder;
    private readonly Dictionary<string, string> _changed;

    public SettingsStore(SettingsSchema schema, string folder)
    {
        _schema = schema;
        _folder = folder;
        _changed = ReadChanged(Path.Combine(folder, "settings.json"), schema);
    }

    /// <summary>%APPDATA%\Nyatch, where the island also keeps its files.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nyatch");

    public SettingsSchema Schema => _schema;

    public string Get(string fullName) =>
        _changed.TryGetValue(fullName, out var value) ? value : _schema.Find(fullName)?.Default ?? "";

    public bool IsChanged(string fullName) => _changed.ContainsKey(fullName);

    /// <summary>Set a value. Unknown names and values a setting cannot take are refused.</summary>
    public bool Set(string fullName, string value)
    {
        var setting = _schema.Find(fullName);
        if (setting is null || !Accepts(setting, value)) return false;
        if (value == setting.Default) _changed.Remove(fullName);
        else _changed[fullName] = value;
        return true;
    }

    public void Reset(string fullName) => _changed.Remove(fullName);

    /// <summary>The settings that differ from their defaults.</summary>
    public IReadOnlyCollection<string> Changed => _changed.Keys;

    /// <summary>Every setting back to its default.</summary>
    public void ResetAll() => _changed.Clear();

    /// <summary>The changed settings as text, to keep as a backup (the same form as settings.json).</summary>
    public string Export() =>
        JsonSerializer.Serialize(_changed.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value), new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Take the settings from a backup, in place of the current ones. Settings it does not name go back
    /// to their defaults; names and values this version does not know are left out. Returns how many
    /// were taken. Throws <see cref="JsonException"/> when the text is not a backup.
    /// </summary>
    public int Import(string backup)
    {
        var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(backup) ?? throw new JsonException("Empty backup.");
        saved = Migrate(saved);
        _changed.Clear();
        foreach (var (name, value) in saved)
        {
            if (_schema.Find(name) is { } setting && Accepts(setting, value) && value != setting.Default) _changed[name] = value;
        }
        return _changed.Count;
    }

    public static bool Accepts(Setting setting, string value) => setting.Kind switch
    {
        SettingKind.Switch => value is "true" or "false",
        SettingKind.Number => int.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out _),
        SettingKind.Choice => setting.Options.Any(o => o.Value == value),
        // One line: the island's file has a setting per line.
        _ => !value.Contains('\n') && !value.Contains('\r'),
    };

    /// <summary>
    /// Write both files. The island is told to re-read afterwards.
    /// <paramref name="derived"/> are extra lines for the island that the app
    /// works out itself (whether an assistant can answer), not user settings.
    /// </summary>
    public void Save(IReadOnlyDictionary<string, string>? derived = null)
    {
        Directory.CreateDirectory(_folder);
        var ordered = _changed.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value);
        WriteWhole(Path.Combine(_folder, "settings.json"), JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true }));
        WriteWhole(Path.Combine(_folder, "settings.ini"), ToIni(derived));
    }

    /// <summary>Every setting as Name=Value, one per line.</summary>
    public string ToIni(IReadOnlyDictionary<string, string>? derived = null)
    {
        var text = new StringBuilder();
        foreach (var setting in _schema.All) text.Append(setting.FullName).Append('=').Append(Get(setting.FullName)).Append('\n');
        foreach (var (name, value) in derived ?? new Dictionary<string, string>()) text.Append(name).Append('=').Append(value).Append('\n');
        return text.ToString();
    }

    // Written beside the target and moved into place, so the island never
    // reads half a file.
    private static void WriteWhole(string path, string text)
    {
        var draft = path + ".new";
        File.WriteAllText(draft, text, new UTF8Encoding(false));
        File.Move(draft, path, overwrite: true);
    }

    private static Dictionary<string, string> ReadChanged(string path, SettingsSchema schema)
    {
        var changed = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(path)) return changed;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
            saved = Migrate(saved);
            foreach (var (name, value) in saved)
            {
                // A setting that no longer exists, or a value it no longer takes, is dropped.
                if (schema.Find(name) is { } setting && Accepts(setting, value) && value != setting.Default) changed[name] = value;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable file means the defaults; it is replaced on the next save.
        }
        return changed;
    }

    /// <summary>
    /// Settings that moved. The second notch used to have its own date layout and clock; both notches now
    /// share the top notch's. A date layout chosen for the second notch is kept as the shared one; its
    /// own clock format and seconds give way to the top notch's.
    /// </summary>
    public static Dictionary<string, string> Migrate(Dictionary<string, string> saved)
    {
        var result = new Dictionary<string, string>(saved, StringComparer.Ordinal);

        // The Hardware monitor switch became a page in the top notch's page list.
        if (result.Remove("Modules.HardwareMonitorModule", out var hardware) && hardware == "false" && !result.ContainsKey("Appearance.TopPages"))
        {
            result["Appearance.TopPages"] = "calendar,weather,-hardware,tray";
        }
        result.Remove("Deck.ClockFormat");
        result.Remove("Deck.ShowSeconds");

        var deckStyle = result.GetValueOrDefault("Deck.DateStyle");
        var deckCustom = result.GetValueOrDefault("Deck.DateCustom");
        result.Remove("Deck.DateStyle");
        result.Remove("Deck.DateCustom");
        if (result.ContainsKey("Modules.DateStyle")) return result;

        var topCustom = result.GetValueOrDefault("Modules.DateFormat") ?? "";
        if (topCustom.Trim().Length > 0)
        {
            // The top notch already had its own pattern: that stays, now as the Custom layout.
            result["Modules.DateStyle"] = "custom";
        }
        else if (deckStyle is not null)
        {
            result["Modules.DateStyle"] = deckStyle;
            // The old custom layout's default was never saved, so it is filled in here.
            if (deckStyle == "custom") result["Modules.DateFormat"] = deckCustom ?? "ddd · d MMM yyyy";
        }
        return result;
    }
}

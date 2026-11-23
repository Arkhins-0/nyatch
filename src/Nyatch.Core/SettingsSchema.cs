using System.Globalization;

namespace Nyatch.Core;

/// <summary>What kind of control a setting needs.</summary>
public enum SettingKind
{
    Switch,
    Number,
    Text,
    Choice,
}

/// <summary>One value a <see cref="SettingKind.Choice"/> setting can take.</summary>
public sealed record SettingOption(string Value, string Label);

/// <summary>One setting of the island.</summary>
public sealed class Setting
{
    /// <summary>The name the island reads it by: "Group.Key".</summary>
    public required string FullName { get; init; }
    public required string Key { get; init; }
    public required string Default { get; init; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<SettingOption> Options { get; } = [];

    public SettingKind Kind =>
        Options.Count > 0 ? SettingKind.Choice
        : Default is "true" or "false" ? SettingKind.Switch
        : int.TryParse(Default, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? SettingKind.Number
        : SettingKind.Text;
}

/// <summary>A page of settings.</summary>
public sealed class SettingGroup
{
    public required string Key { get; init; }
    public string Name { get; set; } = "";
    public List<Setting> Settings { get; } = [];
}

/// <summary>
/// The island's settings, read from the block its C++ source declares them in
/// (the format Windhawk uses for mod settings):
/// <code>
/// - Group:
///   - Key: default
///     $name: Shown name
///     $description: What it does
///     $options:
///       - value: Label
///   $name: Group's shown name
/// </code>
/// </summary>
public sealed class SettingsSchema
{
    public IReadOnlyList<SettingGroup> Groups { get; }

    private readonly Dictionary<string, Setting> _byName;

    private SettingsSchema(List<SettingGroup> groups)
    {
        Groups = groups;
        _byName = groups.SelectMany(g => g.Settings).ToDictionary(s => s.FullName);
    }

    public IEnumerable<Setting> All => Groups.SelectMany(g => g.Settings);

    public Setting? Find(string fullName) => _byName.GetValueOrDefault(fullName);

    /// <summary>The schema built into this library.</summary>
    public static SettingsSchema Load()
    {
        using var stream = typeof(SettingsSchema).Assembly.GetManifestResourceStream("settings.schema.yaml")
            ?? throw new InvalidOperationException("The settings schema is missing from the build.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static SettingsSchema Parse(string text)
    {
        var groups = new List<SettingGroup>();
        SettingGroup? group = null;
        Setting? setting = null;
        var inOptions = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ');
            if (line.Length == 0) continue;
            var indent = line.Length - line.TrimStart(' ').Length;
            var body = line[indent..];
            var isItem = body.StartsWith("- ", StringComparison.Ordinal);
            if (isItem) body = body[2..];
            var colon = body.IndexOf(':');
            if (colon < 0) continue;
            var key = Unquote(body[..colon].Trim());
            var value = Unquote(body[(colon + 1)..].Trim());

            if (indent == 0 && isItem)
            {
                group = new SettingGroup { Key = key, Name = key };
                groups.Add(group);
                setting = null;
                inOptions = false;
            }
            else if (group is null)
            {
                continue;
            }
            else if (indent == 2 && isItem)
            {
                setting = new Setting { Key = key, FullName = $"{group.Key}.{key}", Default = value, Name = key };
                group.Settings.Add(setting);
                inOptions = false;
            }
            else if (indent == 2 && key == "$name")
            {
                group.Name = value;
            }
            else if (setting is null)
            {
                continue;
            }
            else if (indent == 4 && key == "$name")
            {
                setting.Name = value;
            }
            else if (indent == 4 && key == "$description")
            {
                setting.Description = value;
            }
            else if (indent == 4 && key == "$options")
            {
                inOptions = true;
            }
            else if (indent == 6 && isItem && inOptions)
            {
                setting.Options.Add(new SettingOption(key, value));
            }
        }
        return new SettingsSchema(groups);
    }

    /// <summary>'it''s' → it's. Unquoted text is returned as it is.</summary>
    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'') return text[1..^1].Replace("''", "'");
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') return text[1..^1];
        return text;
    }
}

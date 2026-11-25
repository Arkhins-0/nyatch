using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

public class SettingsTests
{
    private const string Sample = """
        - Appearance:
          - Position: top-center
            $name: Position
            $description: Where the island sits.
            $options:
              - top-center: Top Center
              - 'bottom-left': Bottom Left
          - OffsetX: 0
            $name: Offset X
          - FontFamily: ''
            $name: Font
            $description: 'It''s optional: leave blank for the default.'
          $name: Appearance & Position
        - Behavior:
          - AlwaysOnTop: true
            $name: Always on top
          $name: Behavior
        """;

    [Fact]
    public void ReadsGroupsSettingsAndOptions()
    {
        var schema = SettingsSchema.Parse(Sample);
        Assert.Equal(["Appearance & Position", "Behavior"], schema.Groups.Select(g => g.Name));
        var position = schema.Find("Appearance.Position")!;
        Assert.Equal(SettingKind.Choice, position.Kind);
        Assert.Equal("top-center", position.Default);
        Assert.Equal(["top-center", "bottom-left"], position.Options.Select(o => o.Value));
        Assert.Equal(SettingKind.Number, schema.Find("Appearance.OffsetX")!.Kind);
        var font = schema.Find("Appearance.FontFamily")!;
        Assert.Equal(SettingKind.Text, font.Kind);
        Assert.Equal("", font.Default);
        Assert.Equal("It's optional: leave blank for the default.", font.Description);
        Assert.Equal(SettingKind.Switch, schema.Find("Behavior.AlwaysOnTop")!.Kind);
    }

    [Fact]
    public void TheBuiltInSchemaIsWhole()
    {
        var schema = SettingsSchema.Load();
        Assert.Equal(14, schema.Groups.Count);
        Assert.Equal(170, schema.All.Count());
        Assert.All(schema.All, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
        // Every choice's default is one of its options.
        Assert.All(schema.All.Where(s => s.Kind == SettingKind.Choice), s => Assert.Contains(s.Options, o => o.Value == s.Default));
        Assert.Equal("obsidian", schema.Find("Themes.ThemePreset")!.Default);
    }

    [Fact]
    public void KeepsOnlyChangesAndWritesEverySettingForTheIsland()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var schema = SettingsSchema.Parse(Sample);
            var store = new SettingsStore(schema, folder);
            Assert.Equal("top-center", store.Get("Appearance.Position"));

            Assert.True(store.Set("Appearance.Position", "bottom-left"));
            Assert.True(store.Set("Appearance.OffsetX", "-12"));
            Assert.True(store.Set("Behavior.AlwaysOnTop", "true")); // the default: nothing to keep
            Assert.False(store.Set("Appearance.Position", "sideways"));
            Assert.False(store.Set("Appearance.OffsetX", "twelve"));
            Assert.False(store.Set("Behavior.AlwaysOnTop", "yes"));
            Assert.False(store.Set("Appearance.FontFamily", "two\nlines"));
            Assert.False(store.Set("Nothing.Here", "1"));
            Assert.False(store.IsChanged("Behavior.AlwaysOnTop"));
            store.Save();

            Assert.Equal(
                "Appearance.Position=bottom-left\nAppearance.OffsetX=-12\nAppearance.FontFamily=\nBehavior.AlwaysOnTop=true\n",
                File.ReadAllText(Path.Combine(folder, "settings.ini")));

            var again = new SettingsStore(schema, folder);
            Assert.Equal("bottom-left", again.Get("Appearance.Position"));
            Assert.Equal("-12", again.Get("Appearance.OffsetX"));
            again.Reset("Appearance.OffsetX");
            Assert.Equal("0", again.Get("Appearance.OffsetX"));

            // A damaged file falls back to the defaults.
            File.WriteAllText(Path.Combine(folder, "settings.json"), "{ not json");
            Assert.Equal("top-center", new SettingsStore(schema, folder).Get("Appearance.Position"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SecondNotchDateMovesToTheSharedOne()
    {
        var moved = SettingsStore.Migrate(new() { ["Deck.DateStyle"] = "dddd", ["Deck.ShowSeconds"] = "true", ["Deck.ClockFormat"] = "24h" });
        Assert.Equal("dddd", moved["Modules.DateStyle"]);
        Assert.False(moved.ContainsKey("Deck.DateStyle"));
        Assert.False(moved.ContainsKey("Deck.ShowSeconds"));
        Assert.False(moved.ContainsKey("Deck.ClockFormat"));

        // A custom layout keeps its pattern, even the default one that was never saved.
        var custom = SettingsStore.Migrate(new() { ["Deck.DateStyle"] = "custom" });
        Assert.Equal("custom", custom["Modules.DateStyle"]);
        Assert.Equal("ddd · d MMM yyyy", custom["Modules.DateFormat"]);
    }

    [Fact]
    public void TopNotchDatePatternWins()
    {
        var moved = SettingsStore.Migrate(new() { ["Modules.DateFormat"] = "yyyy-MM-dd", ["Deck.DateStyle"] = "dddd" });
        Assert.Equal("custom", moved["Modules.DateStyle"]);
        Assert.Equal("yyyy-MM-dd", moved["Modules.DateFormat"]);

        // Already migrated: left alone.
        var again = SettingsStore.Migrate(new() { ["Modules.DateStyle"] = "d MMM" });
        Assert.Equal("d MMM", Assert.Single(again).Value);
    }
}

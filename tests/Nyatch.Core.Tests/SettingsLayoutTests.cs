using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

public class SettingsLayoutTests
{
    private static readonly SettingsSchema Schema = SettingsSchema.Load();

    [Fact]
    public void EverySettingIsInOnePlaceOnly()
    {
        var placed = SettingsLayout.Pages.SelectMany(p => p.Settings).ToList();
        // Nothing twice, nothing left out, nothing that is not a setting.
        Assert.Empty(placed.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(SettingsLayout.Unplaced(Schema).Select(s => s.FullName));
        Assert.DoesNotContain(placed, n => Schema.Find(n) is null);
    }

    [Fact]
    public void PagesHaveTheirOwnIdsAndAKnownGroup()
    {
        Assert.Equal(SettingsLayout.Pages.Count, SettingsLayout.Pages.Select(p => p.Id).Distinct().Count());
        Assert.All(SettingsLayout.Pages, p => Assert.Contains(p.Group, SettingsLayout.Groups));
        // Within a group, no two pages share a name in the sidebar.
        Assert.All(SettingsLayout.Pages.GroupBy(p => p.Group), g => Assert.Equal(g.Count(), g.Select(p => p.Name).Distinct().Count()));
    }

    [Fact]
    public void WhatASettingNeedsExistsAndCanBeChosen()
    {
        foreach (var (child, (parent, values)) in SettingsLayout.Needs)
        {
            Assert.NotNull(Schema.Find(child));
            var needed = Schema.Find(parent);
            Assert.NotNull(needed);
            Assert.All(values, v => Assert.True(SettingsStore.Accepts(needed!, v), $"{parent} cannot be {v}"));
        }
    }

    [Fact]
    public void ASettingIsInUseOnlyWhileWhatItNeedsIsOn()
    {
        var values = new Dictionary<string, string> { ["Modules.Lyrics"] = "true", ["Modules.KaraokeLyrics"] = "false" };
        string Get(string name) => values.GetValueOrDefault(name, Schema.Find(name)!.Default);
        Assert.True(SettingsLayout.InUse("Modules.KaraokeLyrics", Get));
        Assert.False(SettingsLayout.InUse("Modules.KaraokeLetterGlow", Get));
        values["Modules.KaraokeLyrics"] = "true";
        Assert.True(SettingsLayout.InUse("Modules.KaraokeLetterGlow", Get));
        // Up the chain: with lyrics off, nothing under it is in use.
        values["Modules.Lyrics"] = "false";
        Assert.False(SettingsLayout.InUse("Modules.KaraokeLetterGlow", Get));
        Assert.True(SettingsLayout.InUse("Themes.ThemePreset", Get));
    }

    [Fact]
    public void ABackupBringsTheSettingsBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Schema, folder);
            Assert.True(store.Set("Deck.Emoji", "🐱"));
            Assert.True(store.Set("Themes.ThemePreset", "nord"));
            var backup = store.Export();

            store.ResetAll();
            Assert.Empty(store.Changed);
            Assert.Equal("paws", store.Get("Deck.Emoji"));

            Assert.True(store.Set("Modules.Lyrics", "false"));
            // What the backup does not name goes back to its default; what this version does not know is left out.
            var taken = store.Import(backup.Replace("{", "{ \"Gone.Setting\": \"x\", "));
            Assert.Equal(2, taken);
            Assert.Equal("🐱", store.Get("Deck.Emoji"));
            Assert.Equal("nord", store.Get("Themes.ThemePreset"));
            Assert.Equal("true", store.Get("Modules.Lyrics"));
            Assert.Throws<System.Text.Json.JsonException>(() => store.Import("not a backup"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}

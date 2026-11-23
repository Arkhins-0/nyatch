using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

public class DeckPagesTests
{
    [Fact]
    public void ReadsTheOrderWithHotkeyOnlyAndHiddenPages()
    {
        var order = DeckPages.Order("gemini,~claude,-online,bogus,gemini");
        Assert.Equal(("gemini", PageShow.Shown), order[0]);
        Assert.Equal(("claude", PageShow.HotkeyOnly), order[1]);
        Assert.Equal(("online", PageShow.Hidden), order[2]);
        // Every page once; the ones not named come after, shown.
        Assert.Equal(DeckPages.All.Count, order.Count);
        Assert.Equal(PageShow.Shown, order.Single(o => o.Id == "apps").Show);
        Assert.StartsWith("gemini,~claude,-online,apps", DeckPages.OrderText(order));
    }

    [Fact]
    public void GivesEachPageOneLetter()
    {
        var keys = DeckPages.Keys("gemini=x,claude=G,notes=?,apps=AB");
        Assert.Equal('X', keys["gemini"]);
        Assert.Equal('G', keys["claude"]);
        // A letter that is not one, or a pair that is not a page and a letter, keeps the usual one.
        Assert.Equal('N', keys["notes"]);
        Assert.Equal('A', keys["apps"]);
        Assert.Equal(keys.Count, keys.Values.Distinct().Count());
        Assert.Equal(keys, DeckPages.Keys(DeckPages.KeysText(keys)));
    }

    [Fact]
    public void APageCanHaveNoLetter()
    {
        var keys = DeckPages.Keys("online=,tray=O");
        Assert.False(keys.ContainsKey("online"));
        Assert.Equal('O', keys["tray"]);
        // Saved and read back, it still has none; the usual letters do not fill it in.
        var again = DeckPages.Keys(DeckPages.KeysText(keys));
        Assert.False(again.ContainsKey("online"));
        // Given one back: its usual letter is taken, so the first free one.
        Assert.Equal('D', DeckPages.GiveKey(again, "online"));
    }

    [Fact]
    public void SaysTheHotkeyInWords()
    {
        Assert.Equal("Ctrl + Alt + N", DeckPages.HotkeyWords("ctrl_alt", "n"));
        Assert.Equal("Win + Shift + Space", DeckPages.HotkeyWords("win_shift", "Space"));
    }
}

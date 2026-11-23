using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

public class DeckTests
{
    [Fact]
    public void ReadsTypedLinks()
    {
        Assert.Equal("https://github.com/", WebLink.From("github.com")!.Url);
        Assert.Equal("GitHub", WebLink.From("www.github.com")!.Name);
        Assert.Equal("Arkhins", WebLink.From("https://arkhins.com/nyatch")!.Name);
        Assert.Null(WebLink.From("not a link"));
        Assert.Null(WebLink.From("localhost"));
        Assert.Null(WebLink.From("ftp://files.example.com"));
    }

    [Fact]
    public void KeepsLinksAcrossRuns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-deck-" + Guid.NewGuid().ToString("N"));
        try
        {
            var deck = new DeckStore(folder);
            deck.Links.Add(WebLink.From("arkhins.com")!);
            deck.Save();
            Assert.Equal("https://arkhins.com/", Assert.Single(new DeckStore(folder).Links).Url);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static readonly DateTime T0 = new(2026, 11, 20, 9, 0, 0);

    [Fact]
    public void CountdownRunsPausesAndFinishesOnce()
    {
        var timer = new Countdown(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Left(T0));
        timer.Start(T0);
        Assert.Equal(TimeSpan.FromMinutes(3), timer.Left(T0.AddMinutes(2)));
        timer.Pause(T0.AddMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(3), timer.Left(T0.AddHours(1)));
        timer.Start(T0.AddHours(1));
        Assert.False(timer.TakeFinished(T0.AddHours(1).AddMinutes(2)));
        Assert.True(timer.TakeFinished(T0.AddHours(1).AddMinutes(3)));
        Assert.False(timer.TakeFinished(T0.AddHours(2)));
        Assert.False(timer.Running);
        // Starting a finished timer runs the full length again.
        timer.Start(T0.AddHours(2));
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Left(T0.AddHours(2)));
        timer.Reset(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(1), timer.Left(T0));
    }

    [Fact]
    public void StopwatchKeepsTimeAcrossPauses()
    {
        var watch = new Stopwatch2();
        watch.Start(T0);
        watch.Pause(T0.AddSeconds(10));
        watch.Start(T0.AddSeconds(60));
        Assert.Equal(TimeSpan.FromSeconds(15), watch.Elapsed(T0.AddSeconds(65)));
        watch.Reset();
        Assert.Equal(TimeSpan.Zero, watch.Elapsed(T0.AddHours(1)));
    }

    [Fact]
    public void PomodoroAlternatesAndTakesALongBreakAfterFour()
    {
        var pomodoro = new Pomodoro();
        var now = T0;
        pomodoro.Clock.Start(now);
        Assert.Null(pomodoro.Advance(now.AddMinutes(24)));
        var breaks = new List<TimeSpan>();
        for (var turn = 0; turn < 4; turn++)
        {
            now += Pomodoro.Focus;
            Assert.Equal("Focus turn done", pomodoro.Advance(now));
            Assert.True(pomodoro.OnBreak);
            breaks.Add(pomodoro.Clock.Length);
            now += pomodoro.Clock.Length;
            Assert.Equal("Break over", pomodoro.Advance(now));
            Assert.False(pomodoro.OnBreak);
        }
        Assert.Equal([Pomodoro.ShortBreak, Pomodoro.ShortBreak, Pomodoro.ShortBreak, Pomodoro.LongBreak], breaks);
        Assert.Equal(4, pomodoro.Done);
    }

    [Fact]
    public void KeepsTodoListsAndNotes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var deck = new DeckStore(folder);
            var first = Assert.Single(deck.Lists);
            deck.Add(first.Id, "  buy milk ");
            deck.Add(first.Id, "   ");
            deck.Add(first.Id, "call home");
            deck.Toggle(first.Id, first.Items[0].Id);
            var work = deck.AddList("  Work ");
            deck.Add(work.Id, "ship it");
            deck.RenameList(work.Id, "Job");
            deck.RenameList(work.Id, "  ");
            Assert.Equal("List 3", deck.AddList("").Name);

            var older = deck.AddNote(T0);
            deck.SetNote(older.Id, "# Shopping\nmilk", T0);
            var newer = deck.AddNote(T0.AddMinutes(1));
            deck.SetNote(newer.Id, "ideas", T0.AddMinutes(1));
            deck.AddNote(T0.AddMinutes(2));
            // Changing an older note brings it to the top.
            deck.SetNote(older.Id, "# Shopping\nmilk, eggs", T0.AddMinutes(3));
            deck.Save();

            var again = new DeckStore(folder);
            Assert.Equal(["To-dos", "Job", "List 3"], again.Lists.Select(l => l.Name));
            Assert.Equal(["buy milk", "call home"], again.Lists[0].Items.Select(t => t.Text));
            Assert.True(again.Lists[0].Items[0].Done);
            Assert.Equal(2, again.Open);
            // The empty note was not kept; the most recently changed is first; titles come from the first line.
            Assert.Equal(["Shopping", "ideas"], again.Pages.Select(n => n.Title));
            Assert.Equal("New note", new Note("x", "  \n", T0).Title);

            again.ClearDone(again.Lists[0].Id);
            Assert.Equal(["call home"], again.Lists[0].Items.Select(t => t.Text));
            again.Remove(again.Lists[0].Id, again.Lists[0].Items[0].Id);
            Assert.Empty(again.Lists[0].Items);
            again.RemoveNote(again.Pages[0].Id);
            Assert.Equal(["ideas"], again.Pages.Select(n => n.Title));

            // The last list is never deleted, only emptied.
            again.RemoveList(again.Lists[1].Id);
            again.RemoveList(again.Lists[1].Id);
            again.Add(again.Lists[0].Id, "left");
            again.RemoveList(again.Lists[0].Id);
            Assert.Empty(Assert.Single(again.Lists).Items);

            File.WriteAllText(Path.Combine(folder, "deck.json"), "{ not json");
            Assert.Empty(Assert.Single(new DeckStore(folder).Lists).Items);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MovesTheFirstVersionsListAndPadIn()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "deck.json"), """{"Todos":[{"Id":"a","Text":"old one","Done":false}],"Notes":"old pad"}""");
            var deck = new DeckStore(folder);
            Assert.Equal(["old one"], Assert.Single(deck.Lists).Items.Select(t => t.Text));
            Assert.Equal("old pad", Assert.Single(deck.Pages).Text);
            deck.Save();
            Assert.DoesNotContain("\"Todos\"", File.ReadAllText(Path.Combine(folder, "deck.json")));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AddsUpScreenTimeAndForgetsOldDays()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var day = new DateOnly(2026, 11, 20);
            var time = new ScreenTime(folder);
            time.Add("Code", 600, day);
            time.Add("Edge", 1200, day);
            time.Add("Code", 3000, day);
            time.Add("", 50, day);
            time.Add("Old", 100, day.AddDays(-20));
            time.Add("Code", 5, day);
            Assert.Equal(TimeSpan.FromSeconds(4805), time.Total(day));
            Assert.Equal(["Code", "Edge"], time.Top(day, 5).Select(t => t.App));
            Assert.Equal(TimeSpan.Zero, time.Total(day.AddDays(-20)));
            time.Save();
            Assert.Equal(TimeSpan.FromSeconds(4805), new ScreenTime(folder).Total(day));
            Assert.Equal("1h 20m", OnlineData.DurationWords(time.Total(day)));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ReadsOnlineReplies()
    {
        Assert.Equal(["BTC", "ETH", "SOL"], OnlineData.Symbols("btc, ETH ;sol, btc, a, bad/one"));
        Assert.Contains("BTCUSDT", Uri.UnescapeDataString(OnlineData.CoinsUrl(["BTC"])));
        var coins = OnlineData.Coins("""[{"symbol":"BTCUSDT","lastPrice":"97000.50","priceChangePercent":"-1.25"},{"symbol":"ODD","lastPrice":"1","priceChangePercent":"0"}]""");
        Assert.Equal([new CoinPrice("BTC", 97000.50m, -1.25m)], coins);

        var rates = OnlineData.Rates("""{"rates":{"INR":84.1,"EUR":0.92}}""", ["INR", "GBP", "EUR"]);
        Assert.Equal([("INR", 84.1m), ("EUR", 0.92m)], rates);

        Assert.True(OnlineData.IsLogin("octo-cat"));
        Assert.False(OnlineData.IsLogin("a/b"));
        var profile = OnlineData.GitHub("""{"login":"octo","public_repos":8,"followers":21}""", """[{"stargazers_count":3},{"stargazers_count":4},{}]""");
        Assert.Equal(new GitHubProfile("octo", 8, 21, 7), profile);

        var solved = OnlineData.LeetCode("""{"data":{"matchedUser":{"submitStatsGlobal":{"acSubmissionNum":[{"difficulty":"All","count":120},{"difficulty":"Easy","count":70},{"difficulty":"Medium","count":40},{"difficulty":"Hard","count":10}]}}}}""");
        Assert.Equal(new LeetCodeStats(120, 70, 40, 10), solved);
        Assert.Null(OnlineData.LeetCode("""{"data":{"matchedUser":null}}"""));
    }

    [Fact]
    public void RecentAppsOffersClosedOnesNewestFirst()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-recent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recent = new RecentApps(folder);
            Assert.True(recent.Seen("Code", @"C:\a\code.exe", T0));
            Assert.True(recent.Seen("Paint", @"C:\a\paint.exe", T0.AddMinutes(1)));
            Assert.True(recent.Seen("", @"C:\a\calc.exe", T0.AddMinutes(2)));
            // Seen again: no new entry, a later time, and paths match whatever their case.
            Assert.False(recent.Seen("Code", @"c:\A\CODE.EXE", T0.AddMinutes(5)));
            Assert.False(recent.Seen("Nothing", " ", T0));

            Assert.Equal(["Code", "calc", "Paint"], recent.Closed([], 9).Select(a => a.Name));
            Assert.Equal(["calc", "Paint"], recent.Closed([@"C:\A\code.exe"], 9).Select(a => a.Name));
            Assert.Single(recent.Closed([], 1));

            recent.Forget(@"C:\a\CALC.exe");
            recent.Save();
            var again = new RecentApps(folder);
            Assert.Equal(["Code", "Paint"], again.Closed([], 9).Select(a => a.Name));
            Assert.Equal(T0.AddMinutes(5), again.Closed([], 1)[0].Seen);
            Assert.Null(again.Closed([], 1)[0].Command);

            // An app that shares its program with others keeps its own command and icon.
            again.Seen("Shows", "Browser.app.shows", T0.AddMinutes(9), "\"browser.exe\" --app-id=shows", @"C:\icons\shows.ico");
            again.Save();
            var shows = new RecentApps(folder).Closed([], 1)[0];
            Assert.Equal(("Shows", "\"browser.exe\" --app-id=shows", @"C:\icons\shows.ico"), (shows.Name, shows.Command, shows.Icon));
            again.Forget("browser.APP.shows");

            // The oldest is dropped once the list is full.
            for (var i = 0; i < RecentApps.Kept; i++) again.Seen("App" + i, $@"C:\b\{i}.exe", T0.AddHours(1).AddSeconds(i));
            Assert.Equal(RecentApps.Kept, again.Closed([], 99).Count);
            Assert.DoesNotContain(again.Closed([], 99), a => a.Name is "Code" or "Paint");

            again.Clear();
            again.Save();
            Assert.False(File.Exists(Path.Combine(folder, "recent-apps.json")));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SaysHowLongAgo()
    {
        Assert.Equal("just now", RecentApps.AgoWords(TimeSpan.FromSeconds(40)));
        Assert.Equal("5m ago", RecentApps.AgoWords(TimeSpan.FromMinutes(5.5)));
        Assert.Equal("3h ago", RecentApps.AgoWords(TimeSpan.FromHours(3.9)));
        Assert.Equal("yesterday", RecentApps.AgoWords(TimeSpan.FromHours(30)));
        Assert.Equal("4d ago", RecentApps.AgoWords(TimeSpan.FromDays(4.2)));
    }
}

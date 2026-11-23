using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nyatch.Core;

// The logic behind the second pill's widgets. Nothing here draws anything or
// reads the clock for itself: the time is passed in, so it can all be tested.

/// <summary>A countdown that can be paused: the timer widget.</summary>
public sealed class Countdown
{
    private DateTime? _endsAt;
    private TimeSpan _left;

    public Countdown(TimeSpan length)
    {
        Length = length;
        _left = length;
    }

    public TimeSpan Length { get; private set; }
    public bool Running => _endsAt is not null;

    public TimeSpan Left(DateTime now) => _endsAt is { } end ? (end > now ? end - now : TimeSpan.Zero) : _left;

    /// <summary>True the first time it is asked after the time has run out.</summary>
    public bool TakeFinished(DateTime now)
    {
        if (_endsAt is not { } end || end > now) return false;
        _endsAt = null;
        _left = TimeSpan.Zero;
        return true;
    }

    public void Start(DateTime now)
    {
        if (Running) return;
        if (_left <= TimeSpan.Zero) _left = Length;
        _endsAt = now + _left;
    }

    public void Pause(DateTime now)
    {
        if (!Running) return;
        _left = Left(now);
        _endsAt = null;
    }

    public void Reset(TimeSpan? length = null)
    {
        if (length is { } changed) Length = changed;
        _endsAt = null;
        _left = Length;
    }
}

/// <summary>A stopwatch that can be paused.</summary>
public sealed class Stopwatch2
{
    private DateTime? _startedAt;
    private TimeSpan _banked;

    public bool Running => _startedAt is not null;
    public TimeSpan Elapsed(DateTime now) => _banked + (_startedAt is { } start ? now - start : TimeSpan.Zero);

    public void Start(DateTime now) => _startedAt ??= now;

    public void Pause(DateTime now)
    {
        _banked = Elapsed(now);
        _startedAt = null;
    }

    public void Reset()
    {
        _banked = TimeSpan.Zero;
        _startedAt = null;
    }
}

/// <summary>Focus and break turns: a few focus turns (four, unless set otherwise), then a long break.</summary>
public sealed class Pomodoro
{
    public static readonly TimeSpan Focus = TimeSpan.FromMinutes(25);
    public static readonly TimeSpan ShortBreak = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LongBreak = TimeSpan.FromMinutes(15);

    /// <summary>How long each turn is, and how many focus turns come before a long break.</summary>
    public TimeSpan FocusLength { get; private set; } = Focus;
    public TimeSpan ShortBreakLength { get; private set; } = ShortBreak;
    public TimeSpan LongBreakLength { get; private set; } = LongBreak;
    public int Rounds { get; private set; } = 4;

    /// <summary>
    /// New lengths. A turn under way (or paused part-way) keeps its own; a first focus turn that has
    /// not started takes the new length at once.
    /// </summary>
    public void Configure(TimeSpan focus, TimeSpan shortBreak, TimeSpan longBreak, int rounds)
    {
        var untouched = !OnBreak && !Clock.Running && Clock.Left(DateTime.MinValue) == Clock.Length;
        FocusLength = focus;
        ShortBreakLength = shortBreak;
        LongBreakLength = longBreak;
        Rounds = Math.Max(1, rounds);
        if (untouched && Clock.Length != focus) Clock.Reset(focus);
    }

    public Countdown Clock { get; } = new(Focus);
    public bool OnBreak { get; private set; }
    /// <summary>Focus turns finished since the last long break.</summary>
    public int Done { get; private set; }

    /// <summary>Move to the next turn once the clock has run out. Returns what just ended, or null.</summary>
    public string? Advance(DateTime now)
    {
        if (!Clock.TakeFinished(now)) return null;
        string ended;
        if (OnBreak)
        {
            ended = "Break over";
            OnBreak = false;
            Clock.Reset(FocusLength);
        }
        else
        {
            ended = "Focus turn done";
            Done++;
            OnBreak = true;
            var longBreak = Done % Rounds == 0;
            Clock.Reset(longBreak ? LongBreakLength : ShortBreakLength);
        }
        Clock.Start(now);
        return ended;
    }

    public void Reset()
    {
        OnBreak = false;
        Done = 0;
        Clock.Reset(FocusLength);
    }
}

public sealed record Todo(string Id, string Text, bool Done);

/// <summary>A named list of to-dos, such as "Today" or "Work".</summary>
public sealed record TodoList(string Id, string Name, List<Todo> Items);

/// <summary>A note. Its first line is its title.</summary>
public sealed record Note(string Id, string Text, DateTime Changed)
{
    public string Title
    {
        get
        {
            var first = Text.TrimStart().Split('\n', 2)[0].Trim().TrimStart('#', ' ').Trim();
            return first.Length == 0 ? "New note" : first.Length > 60 ? first[..60] : first;
        }
    }
}

/// <summary>To-do lists and notes, kept in a file.</summary>
/// <summary>A site on the Online page: its name and address.</summary>
public sealed record WebLink(string Id, string Name, string Url)
{
    /// <summary>
    /// What was typed as a link: "github.com", "https://arkhins.com/x". Null if it is not a web address.
    /// The name is the site's own name, from its host ("github.com" is "GitHub").
    /// </summary>
    public static WebLink? From(string typed)
    {
        typed = typed.Trim();
        if (typed.Length == 0 || typed.Contains(' ')) return null;
        if (!typed.Contains("://", StringComparison.Ordinal)) typed = "https://" + typed;
        if (!Uri.TryCreate(typed, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https") || !address.Host.Contains('.')) return null;
        var host = address.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? address.Host[4..] : address.Host;
        var first = host.Split('.')[0];
        var name = first switch
        {
            "github" => "GitHub",
            "youtube" => "YouTube",
            "chatgpt" => "ChatGPT",
            "linkedin" => "LinkedIn",
            _ => first.Length > 0 ? char.ToUpperInvariant(first[0]) + first[1..] : host,
        };
        return new WebLink(Guid.NewGuid().ToString("N"), name, address.AbsoluteUri);
    }
}

public sealed class DeckStore
{
    // Todos and Notes are the single list and pad of the first version, read once and moved into Lists and Pages.
    private sealed record Saved(List<TodoList>? Lists, List<Note>? Pages, List<Todo>? Todos = null, string? Notes = null, List<WebLink>? Links = null);

    private readonly string _path;

    public DeckStore(string folder)
    {
        _path = Path.Combine(folder, "deck.json");
        Saved? saved = null;
        try
        {
            if (File.Exists(_path)) saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_path));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable file means an empty deck; it is replaced on the next save.
        }
        Lists = saved?.Lists?.Where(l => l is { Id: not null, Name: not null }).Select(l => l with { Items = l.Items?.Where(t => t is { Id: not null, Text: not null }).ToList() ?? [] }).ToList() ?? [];
        Pages = saved?.Pages?.Where(n => n is { Id: not null, Text: not null }).ToList() ?? [];
        if (Lists.Count == 0)
        {
            var old = saved?.Todos?.Where(t => t is { Id: not null, Text: not null }).ToList() ?? [];
            Lists.Add(new TodoList(NewId(), "To-dos", old));
        }
        if (Pages.Count == 0 && !string.IsNullOrWhiteSpace(saved?.Notes)) Pages.Add(new Note(NewId(), saved.Notes, DateTime.Now));
        Links = saved?.Links?.Where(l => l is { Id: not null, Name: not null, Url: not null }).ToList() ?? [];
    }

    /// <summary>The Online page's links, in the order they were added.</summary>
    public List<WebLink> Links { get; }

    private static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>The to-do lists, in the order they were made. There is always at least one.</summary>
    public List<TodoList> Lists { get; }

    /// <summary>The notes, most recently changed first.</summary>
    public List<Note> Pages { get; private set; }

    public TodoList? List(string id) => Lists.Find(l => l.Id == id);

    public TodoList AddList(string name)
    {
        name = name.Trim();
        var list = new TodoList(NewId(), name.Length == 0 ? $"List {Lists.Count + 1}" : name, []);
        Lists.Add(list);
        return list;
    }

    public void RenameList(string id, string name)
    {
        name = name.Trim();
        var at = Lists.FindIndex(l => l.Id == id);
        if (at >= 0 && name.Length > 0) Lists[at] = Lists[at] with { Name = name };
    }

    /// <summary>Delete a list. The last one is kept, emptied, so there is always somewhere to write.</summary>
    public void RemoveList(string id)
    {
        if (Lists.Count == 1)
        {
            if (Lists[0].Id == id) Lists[0].Items.Clear();
            return;
        }
        Lists.RemoveAll(l => l.Id == id);
    }

    public void Add(string listId, string text)
    {
        text = text.Trim();
        if (text.Length > 0) List(listId)?.Items.Add(new Todo(NewId(), text, false));
    }

    public void Toggle(string listId, string id)
    {
        if (List(listId) is not { } list) return;
        var at = list.Items.FindIndex(t => t.Id == id);
        if (at >= 0) list.Items[at] = list.Items[at] with { Done = !list.Items[at].Done };
    }

    public void Remove(string listId, string id) => List(listId)?.Items.RemoveAll(t => t.Id == id);

    public void ClearDone(string listId) => List(listId)?.Items.RemoveAll(t => t.Done);

    /// <summary>To-dos not yet done, across every list.</summary>
    public int Open => Lists.Sum(l => l.Items.Count(t => !t.Done));

    public Note AddNote(DateTime now)
    {
        var note = new Note(NewId(), "", now);
        Pages.Insert(0, note);
        return note;
    }

    /// <summary>Change a note's text; it moves to the top as the most recently changed.</summary>
    public void SetNote(string id, string text, DateTime now)
    {
        var at = Pages.FindIndex(n => n.Id == id);
        if (at < 0 || Pages[at].Text == text) return;
        var note = Pages[at] with { Text = text, Changed = now };
        Pages.RemoveAt(at);
        Pages.Insert(0, note);
    }

    public void RemoveNote(string id) => Pages.RemoveAll(n => n.Id == id);

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var draft = _path + ".new";
            // Empty notes are not worth keeping.
            var pages = Pages.Where(n => n.Text.Trim().Length > 0).ToList();
            File.WriteAllText(draft, JsonSerializer.Serialize(new Saved(Lists, pages, Links: Links), new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }), new UTF8Encoding(false));
            File.Move(draft, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The disk refused: what is on screen stays for this run.
        }
    }
}

/// <summary>Time in front of each app, per day. Only kept when the user turns it on.</summary>
public sealed class ScreenTime
{
    /// <summary>Days kept.</summary>
    public const int Days = 14;

    private readonly string _path;
    private Dictionary<string, Dictionary<string, double>> _days = [];

    public ScreenTime(string folder)
    {
        _path = Path.Combine(folder, "screentime.json");
        try
        {
            if (File.Exists(_path)) _days = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void Add(string app, double seconds, DateOnly day)
    {
        if (string.IsNullOrWhiteSpace(app) || seconds <= 0) return;
        if (!_days.TryGetValue(Key(day), out var apps)) _days[Key(day)] = apps = [];
        apps[app] = apps.GetValueOrDefault(app) + seconds;
        // Drop days that have aged out.
        var oldest = Key(day.AddDays(-(Days - 1)));
        foreach (var old in _days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList()) _days.Remove(old);
    }

    public TimeSpan Total(DateOnly day) => TimeSpan.FromSeconds(_days.GetValueOrDefault(Key(day))?.Values.Sum() ?? 0);

    /// <summary>The day's apps, longest first.</summary>
    public IReadOnlyList<(string App, TimeSpan Time)> Top(DateOnly day, int count) =>
        (_days.GetValueOrDefault(Key(day)) ?? []).OrderByDescending(p => p.Value).Take(count).Select(p => (p.Key, TimeSpan.FromSeconds(p.Value))).ToList();

    public void Clear() => _days = [];

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (_days.Count == 0) File.Delete(_path);
            else File.WriteAllText(_path, JsonSerializer.Serialize(_days), new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public sealed record CoinPrice(string Symbol, decimal Price, decimal ChangePercent);

public sealed record GitHubProfile(string Login, int Repos, int Followers, int Stars);

public sealed record LeetCodeStats(int Solved, int Easy, int Medium, int Hard);

/// <summary>Reads the replies of the public services the Online tab asks.</summary>
public static class OnlineData
{
    public static string DurationWords(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m" : "under a minute";

    /// <summary>"btc, ETH ,sol" → BTC, ETH, SOL. Letters and digits only, a few at most.</summary>
    public static IReadOnlyList<string> Symbols(string setting, int limit = 6) =>
        setting.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length is >= 2 and <= 10 && s.All(char.IsAsciiLetterOrDigit))
            .Distinct()
            .Take(limit)
            .ToList();

    /// <summary>Binance's 24-hour ticker for pairs against USDT.</summary>
    public static string CoinsUrl(IReadOnlyList<string> symbols) =>
        "https://api.binance.com/api/v3/ticker/24hr?symbols=" + Uri.EscapeDataString("[" + string.Join(",", symbols.Select(s => $"\"{s}USDT\"")) + "]");

    public static IReadOnlyList<CoinPrice> Coins(string reply)
    {
        using var document = JsonDocument.Parse(reply);
        var coins = new List<CoinPrice>();
        if (document.RootElement.ValueKind != JsonValueKind.Array) return coins;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("symbol", out var symbol) && symbol.GetString() is { } pair && pair.EndsWith("USDT", StringComparison.Ordinal) &&
                Number(item, "lastPrice") is { } price && Number(item, "priceChangePercent") is { } change)
            {
                coins.Add(new CoinPrice(pair[..^4], price, change));
            }
        }
        return coins;
    }

    public static string RatesUrl(string from) => "https://open.er-api.com/v6/latest/" + Uri.EscapeDataString(from);

    /// <summary>How much of each wanted currency one unit of the base buys.</summary>
    public static IReadOnlyList<(string Currency, decimal Rate)> Rates(string reply, IReadOnlyList<string> wanted)
    {
        using var document = JsonDocument.Parse(reply);
        var found = new List<(string, decimal)>();
        if (!document.RootElement.TryGetProperty("rates", out var rates) || rates.ValueKind != JsonValueKind.Object) return found;
        foreach (var currency in wanted)
        {
            if (rates.TryGetProperty(currency, out var rate) && rate.TryGetDecimal(out var value)) found.Add((currency, value));
        }
        return found;
    }

    /// <summary>A GitHub login: letters, digits and single hyphens.</summary>
    public static bool IsLogin(string name) => name.Length is > 0 and <= 39 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public static GitHubProfile GitHub(string userReply, string reposReply)
    {
        using var user = JsonDocument.Parse(userReply);
        using var repos = JsonDocument.Parse(reposReply);
        var stars = repos.RootElement.ValueKind == JsonValueKind.Array
            ? repos.RootElement.EnumerateArray().Sum(r => r.TryGetProperty("stargazers_count", out var s) && s.TryGetInt32(out var n) ? n : 0)
            : 0;
        var root = user.RootElement;
        return new GitHubProfile(root.GetProperty("login").GetString() ?? "", Whole(root, "public_repos"), Whole(root, "followers"), stars);
    }

    public const string LeetCodeUrl = "https://leetcode.com/graphql";

    public static string LeetCodeBody(string user) => JsonSerializer.Serialize(new
    {
        query = "query q($u: String!) { matchedUser(username: $u) { submitStatsGlobal { acSubmissionNum { difficulty count } } } }",
        variables = new { u = user },
    });

    public static LeetCodeStats? LeetCode(string reply)
    {
        using var document = JsonDocument.Parse(reply);
        if (!document.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("matchedUser", out var user) || user.ValueKind != JsonValueKind.Object) return null;
        var counts = new Dictionary<string, int>();
        foreach (var row in user.GetProperty("submitStatsGlobal").GetProperty("acSubmissionNum").EnumerateArray())
        {
            counts[row.GetProperty("difficulty").GetString() ?? ""] = row.GetProperty("count").GetInt32();
        }
        return new LeetCodeStats(counts.GetValueOrDefault("All"), counts.GetValueOrDefault("Easy"), counts.GetValueOrDefault("Medium"), counts.GetValueOrDefault("Hard"));
    }

    private static int Whole(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static decimal? Number(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && decimal.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}

/// <summary>
/// An app that has been open. Path is what tells apps apart: the program's file, or an app's
/// own identity when several apps share one program (a website installed as an app, a Store app).
/// Those also carry the Command that starts them and where their Icon comes from.
/// </summary>
public sealed record RecentApp(string Name, string Path, DateTime Seen, string? Command = null, string? Icon = null);

/// <summary>Apps that were open lately, so the Apps tab can offer the ones that are closed now. Kept in a file on this PC.</summary>
public sealed class RecentApps
{
    /// <summary>Apps remembered; the one seen longest ago is dropped first.</summary>
    public const int Kept = 40;

    private readonly string _path;
    private List<RecentApp> _apps = [];
    private bool _changed;

    public RecentApps(string folder)
    {
        _path = Path.Combine(folder, "recent-apps.json");
        try
        {
            if (File.Exists(_path)) _apps = JsonSerializer.Deserialize<List<RecentApp>>(File.ReadAllText(_path))?.Where(a => a is { Name: not null, Path.Length: > 0 }).ToList() ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable file means no history; it is replaced on the next save.
        }
    }

    /// <summary>Note that an app is open now. True when it was not known before.</summary>
    public bool Seen(string name, string path, DateTime now, string? command = null, string? icon = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
        _changed = true;
        var at = _apps.FindIndex(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
        {
            _apps[at] = _apps[at] with { Name = name, Seen = now, Command = command, Icon = icon };
            return false;
        }
        _apps.Add(new RecentApp(name, path, now, command, icon));
        if (_apps.Count > Kept) _apps.Remove(_apps.MinBy(a => a.Seen)!);
        return true;
    }

    /// <summary>Remembered apps that are not among the running ones, most recently seen first.</summary>
    public IReadOnlyList<RecentApp> Closed(IEnumerable<string> running, int count)
    {
        var open = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
        return _apps.Where(a => !open.Contains(a.Path)).OrderByDescending(a => a.Seen).Take(count).ToList();
    }

    public void Forget(string path) => _changed |= _apps.RemoveAll(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)) > 0;

    public void Clear()
    {
        _changed |= _apps.Count > 0;
        _apps = [];
    }

    /// <summary>"just now", "5m ago", "3h ago", "yesterday", "4d ago".</summary>
    public static string AgoWords(TimeSpan ago) =>
        ago.TotalMinutes < 1 ? "just now"
        : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes}m ago"
        : ago.TotalDays < 1 ? $"{(int)ago.TotalHours}h ago"
        : ago.TotalDays < 2 ? "yesterday"
        : $"{(int)ago.TotalDays}d ago";

    public void Save()
    {
        if (!_changed) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (_apps.Count == 0) File.Delete(_path);
            else File.WriteAllText(_path, JsonSerializer.Serialize(_apps), new UTF8Encoding(false));
            _changed = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

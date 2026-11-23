using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Nyatch.Core;

/// <summary>Tokens used in one stretch of time, with the number of replies.</summary>
public sealed record UsageTotal(long Tokens, int Replies);

/// <summary>How much of a plan allowance is used, 0 to 100, and when it resets.</summary>
public sealed record PlanLimit(double Used, DateTimeOffset? ResetsAt);

public sealed record PlanLimits(string Plan, PlanLimit? Session, PlanLimit? Weekly);

/// <summary>
/// Claude Code's usage on this PC.
/// <para>Token counts come from Claude Code's own logs
/// (~/.claude/projects/**/*.jsonl), read-only and offline; only the numbers
/// are read, never the conversation text.</para>
/// <para>Plan limits use the Claude Code sign-in on this PC to ask Anthropic,
/// and are only fetched when the user has turned that on.</para>
/// </summary>
public static class ClaudeUsage
{
    public sealed record Entry(DateTimeOffset At, string Key, long Tokens);

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>Read one log line. Only assistant replies with a usage block count.</summary>
    public static Entry? ParseLine(string line)
    {
        // Most lines are something else; skip them without parsing.
        if (!line.Contains("\"usage\"", StringComparison.Ordinal) || !line.Contains("\"assistant\"", StringComparison.Ordinal)) return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "assistant") return null;
            if (!root.TryGetProperty("message", out var message) || !message.TryGetProperty("usage", out var usage)) return null;
            // Placeholder entries written for errors carry no real usage.
            if (message.TryGetProperty("model", out var model) && model.GetString() == "<synthetic>") return null;
            if (!root.TryGetProperty("timestamp", out var stamp) || !DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) return null;
            long Count(string name) => usage.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
            var id = message.TryGetProperty("id", out var messageId) ? messageId.GetString() : "";
            var request = root.TryGetProperty("requestId", out var requestId) ? requestId.GetString() : "";
            // Cache reads are left out: they repeat the same text and would swamp the total.
            return new Entry(at, $"{id}:{request}", Count("input_tokens") + Count("output_tokens") + Count("cache_creation_input_tokens"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Add up entries since <paramref name="since"/>, counting a repeated reply once.</summary>
    public static UsageTotal Total(IEnumerable<Entry> entries, DateTimeOffset since)
    {
        var seen = new HashSet<string>();
        long tokens = 0;
        foreach (var entry in entries)
        {
            if (entry.At < since || !seen.Add(entry.Key)) continue;
            tokens += entry.Tokens;
        }
        return new UsageTotal(tokens, seen.Count);
    }

    /// <summary>Every reply logged in the last week. Slow enough to run off the main thread.</summary>
    public static List<Entry> ReadWeek(DateTimeOffset now)
    {
        var entries = new List<Entry>();
        var root = Path.Combine(Folder, "projects");
        if (!Directory.Exists(root)) return entries;
        var since = now.AddDays(-7);
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true }))
            {
                if (File.GetLastWriteTimeUtc(file) < since.UtcDateTime) continue;
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    while (reader.ReadLine() is { } line)
                    {
                        if (ParseLine(line) is { } entry && entry.At >= since) entries.Add(entry);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A log in use or out of reach is skipped.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return entries;
    }

    public static string TokenWords(long tokens) =>
        tokens >= 1_000_000 ? (tokens / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M"
        : tokens >= 1_000 ? (tokens / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k"
        : tokens.ToString(CultureInfo.InvariantCulture);

    /// <summary>Pick the session and weekly figures out of Anthropic's usage reply.</summary>
    public static PlanLimits ParseLimits(string reply, string plan)
    {
        using var document = JsonDocument.Parse(reply);
        PlanLimit? Limit(string name)
        {
            if (!document.RootElement.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return null;
            if (!value.TryGetProperty("utilization", out var used) || !used.TryGetDouble(out var percent)) return null;
            DateTimeOffset? resets = value.TryGetProperty("resets_at", out var at) && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when : null;
            return new PlanLimit(percent, resets);
        }
        return new PlanLimits(plan, Limit("five_hour"), Limit("seven_day"));
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    /// <summary>
    /// The plan's session and weekly limits, using the Claude Code sign-in on
    /// this PC. Call only when the user has turned plan limits on. Throws
    /// <see cref="AssistantException"/> with words for the user.
    /// </summary>
    public static async Task<PlanLimits> Limits(CancellationToken cancel)
    {
        string token, plan;
        try
        {
            using var credentials = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Folder, ".credentials.json"), cancel));
            var oauth = credentials.RootElement.GetProperty("claudeAiOauth");
            token = oauth.GetProperty("accessToken").GetString() ?? "";
            plan = oauth.TryGetProperty("subscriptionType", out var type) ? type.GetString() ?? "" : "";
            // An expired sign-in is left for Claude Code itself to renew.
            if (oauth.TryGetProperty("expiresAt", out var expires) && expires.TryGetInt64(out var at) && at <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                throw new AssistantException("The sign-in has expired. Open Claude Code once to renew it.");
            }
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException)
        {
            throw new AssistantException("Claude Code is not signed in on this PC.");
        }
        if (token.Length == 0) throw new AssistantException("Claude Code is not signed in on this PC.");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd("nyatch-widget");
        try
        {
            using var response = await Http.SendAsync(request, cancel);
            if ((int)response.StatusCode is 401 or 403) throw new AssistantException("Anthropic did not accept the sign-in. Open Claude Code once to renew it.");
            if ((int)response.StatusCode == 429) throw new AssistantException("Anthropic is limiting these requests. It will be tried again later.");
            if (!response.IsSuccessStatusCode) throw new AssistantException($"Anthropic answered with an error ({(int)response.StatusCode}).");
            var limits = ParseLimits(await response.Content.ReadAsStringAsync(cancel), plan);
            if (limits.Session is null && limits.Weekly is null) throw new AssistantException("The reply had no limits in it. The service may have changed.");
            return limits;
        }
        catch (HttpRequestException e)
        {
            throw new AssistantException("Could not reach Anthropic: " + Assistant.Short(e.Message));
        }
        catch (JsonException)
        {
            throw new AssistantException("The reply was not understood.");
        }
    }
}

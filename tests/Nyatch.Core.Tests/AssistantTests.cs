using System.Text.Json;
using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

public class AssistantTests
{
    private static readonly ChatTurn[] Turns = [new("user", "hi"), new("assistant", "hello"), new("user", "again")];

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void MapsChoicesToServicesAndKeys()
    {
        Assert.Equal(AssistantEngine.Claude, Assistant.Parse("claude"));
        Assert.Equal(AssistantEngine.Gemini, Assistant.Parse("gemini"));
        Assert.Equal(AssistantEngine.None, Assistant.Parse("googleai"));  // Google AI Mode is gone
        Assert.Equal(AssistantEngine.None, Assistant.Parse("bing"));
        Assert.Equal(Secrets.Anthropic, Assistant.Secret(AssistantEngine.Claude));
        Assert.Equal(Secrets.Gemini, Assistant.Secret(AssistantEngine.Gemini));
        Assert.Null(Assistant.Secret(AssistantEngine.None));
        foreach (var engine in Enum.GetValues<AssistantEngine>()) Assert.Equal(engine, Assistant.Parse(Assistant.Id(engine)));
        // Every assistant's key is one the Keys page offers.
        Assert.All(Enum.GetValues<AssistantEngine>().Where(e => e != AssistantEngine.None), e => Assert.Contains(Secrets.Known, s => s.Id == Assistant.Secret(e)));
    }

    [Fact]
    public void KeepsOnlyPlainModelIds()
    {
        Assert.Equal("gpt-x.1", Assistant.CleanModel(" gpt-x.1 ", AssistantEngine.ChatGpt));
        Assert.Equal("claude-opus-5-5", Assistant.CleanModel("", AssistantEngine.Claude));
        Assert.Equal(Assistant.DefaultModel(AssistantEngine.Gemini), Assistant.CleanModel("a/../b?x=1", AssistantEngine.Gemini));
    }

    [Fact]
    public void BuildsEachRequest()
    {
        var openAi = Json(Assistant.OpenAiBody("m", Turns));
        Assert.Equal("system", openAi.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal(4, openAi.GetProperty("messages").GetArrayLength());
        Assert.True(openAi.GetProperty("stream").GetBoolean());

        Assert.EndsWith("/models/m:streamGenerateContent?alt=sse", Assistant.GeminiUrl("m"));
        var searched = Json(Assistant.GeminiBody(Turns, search: true));
        Assert.Equal("model", searched.GetProperty("contents")[1].GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Object, searched.GetProperty("tools")[0].GetProperty("google_search").ValueKind);
        Assert.False(Json(Assistant.GeminiBody(Turns, search: false)).TryGetProperty("tools", out _));
    }

    [Fact]
    public void ReadsTextFromEachStream()
    {
        Assert.Equal("Hi", Assistant.OpenAiDelta(Json("""{"choices":[{"delta":{"content":"Hi"}}]}""")));
        Assert.Null(Assistant.OpenAiDelta(Json("""{"choices":[{"delta":{}}]}""")));
        Assert.Null(Assistant.OpenAiDelta(Json("""{"choices":[]}""")));

        var gemini = Json("""{"candidates":[{"content":{"parts":[{"text":"H"},{"text":"i"}]},"groundingMetadata":{"groundingChunks":[{"web":{"title":"example.com","uri":"https://x"}}]}}]}""");
        Assert.Equal("Hi", Assistant.GeminiDelta(gemini));
        Assert.Equal(["example.com"], Assistant.GeminiSources(gemini));
        Assert.Null(Assistant.GeminiDelta(Json("""{"candidates":[]}""")));
    }

    [Fact]
    public void ExplainsFailures()
    {
        Assert.Contains("rejected the key", Assistant.Explain(AssistantEngine.ChatGpt, 401, "", "m"));
        Assert.Contains("“m”", Assistant.Explain(AssistantEngine.Gemini, 404, "", "m"));
        Assert.Contains("bad thing", Assistant.Explain(AssistantEngine.Claude, 400, """{"error":{"message":"bad thing"}}""", "m"));
        Assert.Contains("bad thing", Assistant.Explain(AssistantEngine.Gemini, 400, """[{"error":{"message":"bad thing"}}]""", "m"));
        Assert.Contains("(500)", Assistant.Explain(AssistantEngine.ChatGpt, 500, "not json", "m"));
        Assert.Equal("OpenAI: nope", Assistant.StreamError(AssistantEngine.ChatGpt, Json("""{"error":{"message":"nope"}}""")));
        Assert.Throws<AssistantException>(() => Assistant.Check([]));
        Assert.Throws<AssistantException>(() => Assistant.Check(Turns[..2]));
        Assistant.Check(Turns);
    }

    [Fact]
    public void KeepsFindsAndDeletesConversations()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Chat Talk(string id, string question, string answer = "ok") => new(id, "claude", long.Parse(id), [new("user", question), new("assistant", answer)]);
            var history = new ChatHistory(folder);
            history.Save(Talk("1", "  capital of\nFrance ", "Paris"));
            history.Save(Talk("2", "boiling point", "100 degrees"));
            Assert.Equal(["2", "1"], history.All.Select(c => c.Id));
            Assert.Equal("capital of France", history.All[1].Title);

            // Adding to an old conversation moves it to the front, without a copy.
            history.Save(history.All[1] with { At = 3 });
            Assert.Equal(["1", "2"], history.All.Select(c => c.Id));

            // Search looks in questions and answers; a question asked before finds its answer.
            Assert.Equal(["2"], history.Search("DEGREES").Select(c => c.Id));
            Assert.Equal("Paris", history.FindAnswer("Capital   of france")?.Answer);
            Assert.Null(history.FindAnswer("never asked"));

            // It survives a restart, and only the newest are kept.
            Assert.Equal(["1", "2"], new ChatHistory(folder).All.Select(c => c.Id));
            for (var i = 10; i < 10 + ChatHistory.Capacity + 5; i++) history.Save(Talk(i.ToString(), "q" + i));
            Assert.Equal(ChatHistory.Capacity, history.All.Count);

            history.Remove(history.All[0].Id);
            Assert.Equal(ChatHistory.Capacity - 1, history.All.Count);
            history.Clear();
            Assert.Empty(new ChatHistory(folder).All);

            // A damaged file is treated as no history.
            File.WriteAllText(Path.Combine(folder, "ai-history.json"), "{ not json");
            Assert.Empty(new ChatHistory(folder).All);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace Nyatch.Core;

/// <summary>
/// Asks an assistant and hands back its answer a piece at a time. Claude is
/// called through Anthropic's own library; OpenAI and Gemini over plain HTTPS.
/// </summary>
public sealed class AssistantClient
{
    // Long enough for a slow answer, short enough that a dead link gives up.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };

    /// <summary>
    /// Sends the conversation and calls <paramref name="onText"/> with each new
    /// piece of the answer. Returns any closing note (the sources of a searched
    /// answer). Throws <see cref="AssistantException"/> with words for the user.
    /// </summary>
    public async Task<string> Ask(AssistantEngine engine, string model, string key, IReadOnlyList<ChatTurn> turns, Action<string> onText, CancellationToken cancel, string own = "", string? prompt = null)
    {
        Assistant.Check(turns);
        model = Assistant.CleanModel(model, engine);
        try
        {
            switch (engine)
            {
                case AssistantEngine.Claude:
                    await AskClaude(model, key, turns, onText, cancel, own, prompt);
                    return "";
                case AssistantEngine.ChatGpt:
                    await AskOverHttp(engine, model, Request(Assistant.OpenAiUrl, Assistant.OpenAiBody(model, turns, own, prompt), r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key)), Assistant.OpenAiDelta, onText, null, cancel);
                    return "";
                case var _ when Assistant.CompatibleUrl(engine) is { } url:
                    // DeepSeek, Mistral, Grok, Groq, Perplexity, OpenRouter: OpenAI's request, at their own address.
                    await AskOverHttp(engine, model, Request(url, Assistant.OpenAiBody(model, turns, own, prompt), r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key)), Assistant.OpenAiDelta, onText, null, cancel);
                    return "";
                case AssistantEngine.Gemini:
                {
                    var sources = new List<string>();
                    var request = Request(Assistant.GeminiUrl(model), Assistant.GeminiBody(turns, search: false, own, prompt), r => r.Headers.Add("x-goog-api-key", key));
                    await AskOverHttp(engine, model, request, Assistant.GeminiDelta, onText, sources, cancel);
                    return sources.Count == 0 ? "" : "\n\nSources: " + string.Join(" · ", sources);
                }
                default:
                    throw new AssistantException("No assistant is chosen.");
            }
        }
        catch (HttpRequestException e)
        {
            throw new AssistantException($"Could not reach {Assistant.Name(engine)}: {Assistant.Short(e.Message)}");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new AssistantException($"{Assistant.Name(engine)} took too long to answer.");
        }
    }

    private static async Task AskClaude(string model, string key, IReadOnlyList<ChatTurn> turns, Action<string> onText, CancellationToken cancel, string own, string? prompt)
    {
        var client = new AnthropicClient { ApiKey = key };
        var parameters = new MessageCreateParams
        {
            Model = model,
            // Claude's API needs a ceiling: a high one, so no answer is cut short.
            MaxTokens = 32000,
            System = Assistant.InstructionsWith(own, prompt),
            // A chat: some thought, without making the reply slow.
            OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
            // If a safety check declines the question, the API re-runs it on
            // another Claude model inside the same call.
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            Messages = turns.Select(t => t.Role == "assistant" ? (BetaMessageParam)new BetaAssistantMessageParam { Content = t.Text } : new BetaUserMessageParam { Content = t.Text }).ToList(),
        };
        try
        {
            await foreach (var streamEvent in client.Beta.Messages.CreateStreaming(parameters, cancellationToken: cancel))
            {
                if (streamEvent.TryPickContentBlockDelta(out var block) && block.Delta.TryPickText(out var text))
                {
                    if (text.Text.Length > 0) onText(text.Text);
                }
                else if (streamEvent.TryPickDelta(out var end) && end.Delta.StopReason == BetaStopReason.Refusal)
                {
                    throw new AssistantException("Claude declined to answer this one.");
                }
            }
        }
        catch (AnthropicApiException e)
        {
            throw new AssistantException(Assistant.Explain(AssistantEngine.Claude, (int)e.StatusCode, "", model) is var words && (int)e.StatusCode is 401 or 403 or 404 or 429
                ? words
                : "Claude: " + Assistant.Short(e.Message));
        }
    }

    private static HttpRequestMessage Request(string url, string body, Action<HttpRequestMessage> authorise)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        authorise(request);
        return request;
    }

    /// <summary>Reads a server-sent-event stream, one "data:" line at a time.</summary>
    private static async Task AskOverHttp(AssistantEngine engine, string model, HttpRequestMessage request, Func<JsonElement, string?> delta, Action<string> onText, List<string>? sources, CancellationToken cancel)
    {
        using var _ = request;
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
        {
            var reply = await response.Content.ReadAsStringAsync(cancel);
            throw new AssistantException(Assistant.Explain(engine, (int)response.StatusCode, reply, model));
        }
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancel), Encoding.UTF8);
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(data);
            }
            catch (JsonException)
            {
                continue;
            }
            using (document)
            {
                var streamEvent = document.RootElement;
                if (Assistant.StreamError(engine, streamEvent) is { } failure) throw new AssistantException(failure);
                if (delta(streamEvent) is { } text) onText(text);
                if (sources is not null)
                {
                    foreach (var title in Assistant.GeminiSources(streamEvent))
                    {
                        if (!sources.Contains(title)) sources.Add(title);
                    }
                }
            }
        }
    }
}

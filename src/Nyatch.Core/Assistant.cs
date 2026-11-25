using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nyatch.Core;

public enum AssistantEngine
{
    None,
    Claude,
    ChatGpt,
    Gemini,
    // Services spoken to as OpenAI is (the same chat completions request), each at its own address.
    DeepSeek,
    Mistral,
    Grok,
    Groq,
    Perplexity,
    OpenRouter,
}

/// <summary>Something went wrong asking an assistant. The message is written for the user.</summary>
public sealed class AssistantException(string message) : Exception(message);

/// <summary>
/// What each assistant is called, which key it needs, and how its service is
/// spoken to. Nothing here touches the network.
/// </summary>
public static class Assistant
{
    /// <summary>
    /// Nyatch's prompt (the Prompt setting starts from it): only what the panel shows, so the answer is
    /// formatted well. Nothing about how long, or what, an answer should be.
    /// </summary>
    public const string Instructions =
        "Your replies are shown in a chat panel that renders Markdown: headings, bold, lists, code fences and tables show as they should. " +
        "HTML is not shown.";


    public static AssistantEngine Parse(string setting) => setting switch
    {
        "claude" => AssistantEngine.Claude,
        "chatgpt" => AssistantEngine.ChatGpt,
        "gemini" => AssistantEngine.Gemini,
        "deepseek" => AssistantEngine.DeepSeek,
        "mistral" => AssistantEngine.Mistral,
        "grok" => AssistantEngine.Grok,
        "groq" => AssistantEngine.Groq,
        "perplexity" => AssistantEngine.Perplexity,
        "openrouter" => AssistantEngine.OpenRouter,
        _ => AssistantEngine.None,
    };

    public static string Id(AssistantEngine engine) => engine switch
    {
        AssistantEngine.Claude => "claude",
        AssistantEngine.ChatGpt => "chatgpt",
        AssistantEngine.Gemini => "gemini",
        AssistantEngine.DeepSeek => "deepseek",
        AssistantEngine.Mistral => "mistral",
        AssistantEngine.Grok => "grok",
        AssistantEngine.Groq => "groq",
        AssistantEngine.Perplexity => "perplexity",
        AssistantEngine.OpenRouter => "openrouter",
        _ => "none",
    };

    public static string Name(AssistantEngine engine) => engine switch
    {
        AssistantEngine.Claude => "Claude",
        AssistantEngine.ChatGpt => "ChatGPT",
        AssistantEngine.Gemini => "Gemini",
        AssistantEngine.DeepSeek => "DeepSeek",
        AssistantEngine.Mistral => "Mistral",
        AssistantEngine.Grok => "Grok",
        AssistantEngine.Groq => "Groq",
        AssistantEngine.Perplexity => "Perplexity",
        AssistantEngine.OpenRouter => "OpenRouter",
        _ => "",
    };

    /// <summary>The key an assistant needs (a <see cref="Secrets"/> id), or null for none.</summary>
    public static string? Secret(AssistantEngine engine) => engine switch
    {
        AssistantEngine.Claude => Secrets.Anthropic,
        AssistantEngine.ChatGpt => Secrets.OpenAi,
        AssistantEngine.Gemini => Secrets.Gemini,
        AssistantEngine.DeepSeek => Secrets.DeepSeek,
        AssistantEngine.Mistral => Secrets.Mistral,
        AssistantEngine.Grok => Secrets.Xai,
        AssistantEngine.Groq => Secrets.Groq,
        AssistantEngine.Perplexity => Secrets.Perplexity,
        AssistantEngine.OpenRouter => Secrets.OpenRouter,
        _ => null,
    };

    /// <summary>The setting that holds an assistant's model.</summary>
    public static string ModelSetting(AssistantEngine engine) => engine switch
    {
        AssistantEngine.Claude => "Assistant.ClaudeModel",
        AssistantEngine.ChatGpt => "Assistant.ChatGptModel",
        AssistantEngine.DeepSeek => "Assistant.DeepSeekModel",
        AssistantEngine.Mistral => "Assistant.MistralModel",
        AssistantEngine.Grok => "Assistant.GrokModel",
        AssistantEngine.Groq => "Assistant.GroqModel",
        AssistantEngine.Perplexity => "Assistant.PerplexityModel",
        AssistantEngine.OpenRouter => "Assistant.OpenRouterModel",
        _ => "Assistant.GeminiModel",
    };

    /// <summary>The engines whose service is OpenAI's request at another address, with that address.</summary>
    public static string? CompatibleUrl(AssistantEngine engine) => engine switch
    {
        AssistantEngine.DeepSeek => "https://api.deepseek.com/chat/completions",
        AssistantEngine.Mistral => "https://api.mistral.ai/v1/chat/completions",
        AssistantEngine.Grok => "https://api.x.ai/v1/chat/completions",
        AssistantEngine.Groq => "https://api.groq.com/openai/v1/chat/completions",
        AssistantEngine.Perplexity => "https://api.perplexity.ai/chat/completions",
        AssistantEngine.OpenRouter => "https://openrouter.ai/api/v1/chat/completions",
        _ => null,
    };

    /// <summary>Every engine, each with a chat of its own.</summary>
    public static IReadOnlyList<AssistantEngine> Chats { get; } =
        [AssistantEngine.Claude, AssistantEngine.ChatGpt, AssistantEngine.Gemini, AssistantEngine.DeepSeek, AssistantEngine.Mistral, AssistantEngine.Grok, AssistantEngine.Groq, AssistantEngine.Perplexity, AssistantEngine.OpenRouter];

    public static string DefaultModel(AssistantEngine engine) => engine switch
    {
        AssistantEngine.Claude => "claude-opus-5-5",
        AssistantEngine.ChatGpt => "gpt-6-astra",
        AssistantEngine.DeepSeek => "deepseek-chat",
        AssistantEngine.Mistral => "mistral-medium-latest",
        AssistantEngine.Grok => "grok-4",
        AssistantEngine.Groq => "qwen/qwen3.8-27b",
        AssistantEngine.Perplexity => "sonar",
        AssistantEngine.OpenRouter => "nvidia/nemotron-3.5-super-vl:free",
        _ => "gemini-flash-latest",
    };

    /// <summary>
    /// The model to ask: the user's choice if it looks like a model id, or the
    /// default. The id goes into a web address for Gemini, so it is kept to
    /// plain characters.
    /// </summary>
    public static string CleanModel(string chosen, AssistantEngine engine)
    {
        chosen = chosen.Trim();
        var allowed = engine is AssistantEngine.Gemini ? "._-:" : "._-:/";
        var plain = chosen.Length is > 0 and <= 96 && chosen.All(c => char.IsAsciiLetterOrDigit(c) || allowed.Contains(c));
        return plain ? chosen : DefaultModel(engine);
    }

    /// <summary>Throws if the conversation cannot be sent as it is.</summary>
    /// <summary>The instructions, with the user's own (the Chats page's "Instructions") after them.</summary>
    /// <param name="prompt">The prompt from the settings (Assistant.Prompt); empty for Nyatch's own.</param>
    public static string InstructionsWith(string own, string? prompt = null)
    {
        var start = string.IsNullOrWhiteSpace(prompt) ? Instructions : prompt.Trim();
        return string.IsNullOrWhiteSpace(own) ? start : start + " The user asks this of every reply: " + own.Trim();
    }

    public static void Check(IReadOnlyList<ChatTurn> turns)
    {
        // A question may be only a picture or a file.
        if (turns.Count == 0 || turns[^1].Role != "user" || turns.Any(t => string.IsNullOrWhiteSpace(t.Text) && (t.Files?.Count ?? 0) == 0)) throw new AssistantException("Nothing to ask.");
    }

    // ---------- Attachments ----------

    /// <summary>Whether an LLM can be sent pictures (DeepSeek's chat cannot).</summary>
    public static bool SeesPictures(AssistantEngine engine) => engine is not (AssistantEngine.DeepSeek or AssistantEngine.None);

    /// <summary>Whether an LLM reads a PDF as it is (Claude, ChatGPT and Gemini).</summary>
    public static bool ReadsPdfs(AssistantEngine engine) => engine is AssistantEngine.Claude or AssistantEngine.ChatGpt or AssistantEngine.Gemini;

    /// <summary>A question's text, or a word for it when it is only an attachment (the services want some text).</summary>
    private static string TextOf(ChatTurn turn) => string.IsNullOrWhiteSpace(turn.Text) && (turn.Files?.Count ?? 0) > 0 ? "See the attached file." : turn.Text;

    /// <summary>The files of a turn that still have their bytes (saved chats keep only their names).</summary>
    private static IEnumerable<ChatFile> Sent(ChatTurn turn) => (turn.Files ?? []).Where(f => f.Data.Length > 0);

    // ---------- Claude, over HTTPS (for a conversation with pictures or PDFs) ----------

    public const string ClaudeUrl = "https://api.anthropic.com/v1/messages";

    public static string ClaudeBody(string model, IReadOnlyList<ChatTurn> turns, string own = "", string? prompt = null)
    {
        var messages = new JsonArray();
        foreach (var turn in turns)
        {
            var content = new JsonArray();
            foreach (var file in Sent(turn))
            {
                content.Add(new JsonObject
                {
                    ["type"] = file.IsPdf ? "document" : "image",
                    ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = file.MediaType, ["data"] = file.Data },
                });
            }
            content.Add(new JsonObject { ["type"] = "text", ["text"] = TextOf(turn) });
            messages.Add(new JsonObject { ["role"] = turn.Role == "assistant" ? "assistant" : "user", ["content"] = content });
        }
        return new JsonObject
        {
            ["model"] = model, ["max_tokens"] = 32000, ["stream"] = true, ["system"] = InstructionsWith(own, prompt), ["messages"] = messages,
        }.ToJsonString();
    }

    /// <summary>The new text in one event of Claude's stream, if it carries any.</summary>
    public static string? ClaudeDelta(JsonElement streamEvent) =>
        streamEvent.TryGetProperty("type", out var type) && type.GetString() == "content_block_delta" &&
        streamEvent.TryGetProperty("delta", out var delta) && delta.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } words
            ? words
            : null;

    // ---------- OpenAI ----------

    public const string OpenAiUrl = "https://api.openai.com/v1/chat/completions";

    public static string OpenAiBody(string model, IReadOnlyList<ChatTurn> turns, string own = "", string? prompt = null)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = InstructionsWith(own, prompt) } };
        foreach (var turn in turns)
        {
            var files = Sent(turn).ToList();
            if (files.Count == 0)
            {
                messages.Add(new JsonObject { ["role"] = turn.Role == "assistant" ? "assistant" : "user", ["content"] = turn.Text });
                continue;
            }
            // With pictures (or a PDF): the text and each file, as parts.
            var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = TextOf(turn) } };
            foreach (var file in files)
            {
                var data = $"data:{file.MediaType};base64,{file.Data}";
                parts.Add(file.IsPdf
                    ? new JsonObject { ["type"] = "file", ["file"] = new JsonObject { ["filename"] = file.Name, ["file_data"] = data } }
                    : new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = data } });
            }
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = parts });
        }
        return new JsonObject { ["model"] = model, ["stream"] = true, ["messages"] = messages }.ToJsonString();
    }

    /// <summary>The new text in one event of OpenAI's stream, if it carries any.</summary>
    public static string? OpenAiDelta(JsonElement streamEvent)
    {
        if (!streamEvent.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;
        if (!choices[0].TryGetProperty("delta", out var delta) || !delta.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) return null;
        var text = content.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    // ---------- Gemini ----------

    public static string GeminiUrl(string model) => $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse";

    public static string GeminiBody(IReadOnlyList<ChatTurn> turns, bool search, string own = "", string? prompt = null)
    {
        var contents = new JsonArray();
        foreach (var turn in turns)
        {
            var parts = new JsonArray();
            foreach (var file in Sent(turn))
            {
                parts.Add(new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = file.MediaType, ["data"] = file.Data } });
            }
            parts.Add(new JsonObject { ["text"] = TextOf(turn) });
            contents.Add(new JsonObject { ["role"] = turn.Role == "assistant" ? "model" : "user", ["parts"] = parts });
        }
        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = InstructionsWith(own, prompt) } } },
            ["contents"] = contents,
        };
        if (search) body["tools"] = new JsonArray { new JsonObject { ["google_search"] = new JsonObject() } };
        return body.ToJsonString();
    }

    /// <summary>The new text in one event of Gemini's stream, if it carries any.</summary>
    public static string? GeminiDelta(JsonElement streamEvent)
    {
        if (!TryFirstCandidate(streamEvent, out var candidate)) return null;
        if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array) return null;
        var text = string.Concat(parts.EnumerateArray().Select(p => p.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : ""));
        return text.Length == 0 ? null : text;
    }

    /// <summary>The pages a searched answer drew on, by title.</summary>
    public static IEnumerable<string> GeminiSources(JsonElement streamEvent)
    {
        if (!TryFirstCandidate(streamEvent, out var candidate)) yield break;
        if (!candidate.TryGetProperty("groundingMetadata", out var grounding) || !grounding.TryGetProperty("groundingChunks", out var chunks) || chunks.ValueKind != JsonValueKind.Array) yield break;
        foreach (var chunk in chunks.EnumerateArray())
        {
            if (chunk.TryGetProperty("web", out var web) && web.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } name) yield return name;
        }
    }

    private static bool TryFirstCandidate(JsonElement streamEvent, out JsonElement candidate)
    {
        candidate = default;
        if (streamEvent.ValueKind != JsonValueKind.Object || !streamEvent.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return false;
        candidate = candidates[0];
        return true;
    }

    // ---------- Failures ----------

    /// <summary>A failure reported inside a stream, in words for the user.</summary>
    public static string? StreamError(AssistantEngine engine, JsonElement streamEvent) =>
        streamEvent.ValueKind == JsonValueKind.Object && streamEvent.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message) && message.GetString() is { } text
            ? $"{ServiceName(engine)}: {Short(text)}"
            : null;

    /// <summary>Why a request was turned away, in words for the user.</summary>
    public static string Explain(AssistantEngine engine, int status, string body, string model)
    {
        var name = ServiceName(engine);
        var detail = ErrorMessage(body);
        // OpenRouter's free models run only where the account's privacy settings allow them, and it says so with a 404.
        if (status == 404 && detail is not null && detail.Contains("data policy", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} will not run “{model}” with this account's privacy settings. Allow free models at openrouter.ai › Settings › Privacy, or choose another model in Settings › LLM › Services.";
        }
        return status switch
        {
            401 or 403 => $"{name} rejected the key. Check it in Settings › LLM › Services.",
            // The service's own words too, as a 404 is not always an unknown model.
            404 => $"{name} could not use the model “{model}”{(detail is null ? "" : $" ({detail})")}. Change it in Settings › LLM › Services.",
            // The key works, but the account has no plan (or credit) for the API.
            402 when engine == AssistantEngine.Mistral => $"{name}: {detail ?? "the account has no API plan"}. Choose the free Experiment plan at console.mistral.ai › Billing (it may ask to confirm a phone number); the same key then works.",
            402 => $"{name}: {detail ?? "the account has no credit for the API"}. Add a plan or credit on its website; the same key then works.",
            429 => $"{name} is limiting requests, or the account is out of credit.",
            _ => detail is not null ? $"{name}: {detail}" : $"{name} answered with an error ({status}).",
        };
    }

    private static string ServiceName(AssistantEngine engine) => engine switch
    {
        AssistantEngine.Claude => "Claude",
        AssistantEngine.ChatGpt => "OpenAI",
        AssistantEngine.Gemini => "Gemini",
        _ => Name(engine),
    };

    public static string? ErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            // Gemini wraps its error in a list when streaming.
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.GetString() is { } text
                ? Short(text)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Short(string message)
    {
        message = message.Trim();
        return message.Length <= 240 ? message : message[..240] + "…";
    }
}

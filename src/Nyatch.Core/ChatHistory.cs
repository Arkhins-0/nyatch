using System.Text;
using System.Text.Json;

namespace Nyatch.Core;

/// <summary>
/// One message of a conversation. Role is "user" or "assistant"; At is when (Unix ms), 0 if unknown. Files: the
/// pictures and PDFs sent with a question (a text file goes into the text itself).
/// </summary>
public sealed record ChatTurn(string Role, string Text, long At = 0, IReadOnlyList<ChatFile>? Files = null);

/// <summary>
/// A picture or a PDF sent with a question: its name, its type ("image/png", "application/pdf") and its bytes as
/// base64. The bytes are not kept with saved chats, only the name (they would make the history file huge).
/// </summary>
public sealed record ChatFile(string Name, string MediaType, [property: System.Text.Json.Serialization.JsonIgnore] string Data = "")
{
    public bool IsPicture => MediaType.StartsWith("image/", StringComparison.Ordinal);
    public bool IsPdf => MediaType == "application/pdf";
}

/// <summary>A conversation with an assistant.</summary>
public sealed record Chat(string Id, string Engine, long At, IReadOnlyList<ChatTurn> Turns)
{
    /// <summary>A conversation is listed by its first question.</summary>
    public string Title => string.Join(' ', (Turns.FirstOrDefault(t => t.Role == "user")?.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// Past conversations, kept in a file on this PC so the search box can list
/// them and the History page can show, search and delete them.
/// </summary>
public sealed class ChatHistory
{
    /// <summary>The oldest conversations beyond this many are dropped.</summary>
    public const int Capacity = 120;

    /// <summary>Raised after the saved conversations change (on the thread that changed them).</summary>
    public event Action? Changed;

    private readonly string _path;
    private readonly object _lock = new();
    private List<Chat> _chats;

    public ChatHistory(string folder)
    {
        _path = Path.Combine(folder, "ai-history.json");
        _chats = Read(_path);
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Chat> All
    {
        get
        {
            lock (_lock) return _chats.ToList();
        }
    }

    /// <summary>Put <paramref name="chat"/> first, replacing its older copy.</summary>
    public void Save(Chat chat)
    {
        lock (_lock)
        {
            _chats = [chat, .. _chats.Where(c => c.Id != chat.Id)];
            if (_chats.Count > Capacity) _chats.RemoveRange(Capacity, _chats.Count - Capacity);
            Write();
        }
        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            _chats.RemoveAll(c => c.Id == id);
            Write();
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _chats = [];
            Write();
        }
        Changed?.Invoke();
    }

    /// <summary>The newest conversations that mention <paramref name="query"/> anywhere; all of them when it is empty.</summary>
    public IReadOnlyList<Chat> Search(string query)
    {
        query = query.Trim();
        lock (_lock)
        {
            return query.Length == 0 ? _chats.ToList() : _chats.Where(c => c.Turns.Any(t => t.Text.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        }
    }

    /// <summary>The saved answer to a question that was asked before, with the conversation it is in.</summary>
    public (Chat Chat, string Answer)? FindAnswer(string question)
    {
        var wanted = Normalise(question);
        lock (_lock)
        {
            foreach (var chat in _chats)
            {
                for (var i = 0; i + 1 < chat.Turns.Count; i++)
                {
                    if (chat.Turns[i].Role == "user" && chat.Turns[i + 1].Role == "assistant" && Normalise(chat.Turns[i].Text) == wanted)
                    {
                        return (chat, chat.Turns[i + 1].Text);
                    }
                }
            }
        }
        return null;
    }

    private static string Normalise(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private void Write()
    {
        try
        {
            if (_chats.Count == 0)
            {
                File.Delete(_path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var draft = _path + ".new";
            File.WriteAllText(draft, JsonSerializer.Serialize(_chats, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(draft, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The disk refused: the conversations stay in memory for this run.
        }
    }

    private static List<Chat> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var saved = JsonSerializer.Deserialize<List<Chat>>(File.ReadAllText(path)) ?? [];
            // Anything that is not a conversation is dropped.
            return saved
                .Where(c => c is { Id: not null, Engine: not null, Turns: not null } && c.Turns.All(t => t is { Text: not null, Role: "user" or "assistant" }))
                .Take(Capacity)
                .ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

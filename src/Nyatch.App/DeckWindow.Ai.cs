using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.App;

// A chat page for each assistant (Claude, ChatGPT, Gemini), through its API with the key from the Keys
// page. Conversations are saved in the shared history (Settings > History lists them all); each page
// lists its own, to open one again and carry on, or start a new one. The island draws the pages (native);
// this keeps the conversations, talks to the assistants and does what the page's clicks ask.
internal sealed partial class DeckWindow
{
    // The island's reply box on these pages.
    private const int ReplyBox = 40;

    private readonly AssistantClient _assistant = new();

    /// <summary>Open a settings page by name (the app sets this).</summary>
    public Action<string?>? OpenSettingsAt { get; set; }

    /// <summary>One chat page: its conversation, what is being asked now, and what is typed.</summary>
    private sealed class AiPage(AssistantEngine engine)
    {
        public AssistantEngine Engine { get; } = engine;
        public Chat? Current { get; set; }
        public CancellationTokenSource? Running { get; set; }
        public bool History { get; set; }
        public string Draft { get; set; } = "";
        // The question on its way, and its answer so far (or a note: "Thinking…", why it failed).
        public string? Asking { get; set; }
        public long AskedAt { get; set; }
        public string Answer { get; set; } = "";
        public bool AnswerIsNote { get; set; }
        public List<Action> Actions { get; } = [];
    }

    // A chat page for every assistant (the ones beyond these three are on the side panel only).
    private readonly Dictionary<string, AiPage> _aiPages = Assistant.Chats.ToDictionary(Assistant.Id, e => new AiPage(e));

    /// <summary>The chat on show: on the side panel while it is on, else on the second notch.</summary>
    private AiPage? ShownAi => SideOn ? (_sideOpen ? SideAi : null) : DeckAi;

    private bool _aiQueued;

    /// <summary>
    /// A chat page changed: the island gets it if the page is on show. Changes that come close together
    /// (a reply arriving in pieces) are sent once.
    /// </summary>
    private void AiChanged(AiPage page)
    {
        if (!AiShowing || !ReferenceEquals(ShownAi, page) || _aiQueued) return;
        _aiQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _aiQueued = false;
            if (AiShowing && ReferenceEquals(ShownAi, page)) AiSendData(page);
        });
    }

    private static bool HasKey(AiPage page) => Assistant.Secret(page.Engine) is { } id && Secrets.Has(id);

    /// <summary>The chat page for the island (see its DrawDeckChat).</summary>
    private string AiData(AiPage page)
    {
        page.Actions.Clear();
        int Act(Action action)
        {
            page.Actions.Add(action);
            return page.Actions.Count - 1;
        }
        static string Clean(string text) => text.Replace('\u001E', ' ').Replace('\u001F', ' ');
        var name = Assistant.Name(page.Engine);
        var records = new List<string>();
        var hasKey = HasKey(page);
        var history = Act(() =>
        {
            page.History = !page.History;
            AiChanged(page);
        });
        var fresh = Act(() =>
        {
            page.Running?.Cancel();
            page.Current = null;
            page.Asking = null;
            page.History = false;
            AiChanged(page);
            AiFocusField();
        });
        var keys = Act(() => OpenSettingsAt?.Invoke("Keys"));
        var send = Act(() => _ = AiSend(page));
        records.Add(string.Join('\u001F', "A", name, hasKey ? "1" : "0", page.History ? "1" : "0", history.ToString(), fresh.ToString(), keys.ToString(),
            page.Running is not null ? "1" : "0", send.ToString(), Clean(page.Draft),
            page.Running is null && page.Asking is not null && page.AnswerIsNote ? "1" : "0"));
        // On the side panel: the chat on show, as a button in the corner that lists the chats to choose from.
        if (SideOn && _sideTabs.Count > 0)
        {
            var pick = Act(ShowChatPicker);
            records.Add(string.Join('\u001F', "S", _sideTabs[Math.Clamp(_sideTab, 0, _sideTabs.Count - 1)], _sideTab.ToString(System.Globalization.CultureInfo.InvariantCulture), pick.ToString()));
        }
        if (!hasKey) return string.Join('\u001E', records);

        if (page.History)
        {
            var chats = _chats.All.Where(c => c.Engine == Assistant.Id(page.Engine)).ToList();
            if (chats.Count == 0) records.Add($"E\u001FNo chats with {name} yet.");
            foreach (var chat in chats)
            {
                var when = DateTimeOffset.FromUnixTimeMilliseconds(chat.At).LocalDateTime;
                var questions = chat.Turns.Count(t => t.Role == "user");
                var open = Act(() =>
                {
                    page.Running?.Cancel();
                    page.Current = chat;
                    page.Asking = null;
                    page.History = false;
                    AiChanged(page);
                    AiFocusField();
                });
                var delete = Act(() =>
                {
                    _chats.Remove(chat.Id);
                    if (page.Current?.Id == chat.Id) page.Current = null;
                    AiChanged(page);
                });
                records.Add(string.Join('\u001F', "R", Clean(chat.Title.Length > 0 ? chat.Title : "Chat"),
                    $"{Ago(when)} · {questions} {(questions == 1 ? "message" : "messages")}", open.ToString(), delete.ToString()));
            }
            return string.Join('\u001E', records);
        }

        var turns = page.Current?.Turns ?? [];
        if (turns.Count == 0 && page.Asking is null)
        {
            records.Add($"E\u001FAsk {name} anything.");
            return string.Join('\u001E', records);
        }
        int LinkAction(Uri address) => Act(() => OpenAddress(address));
        void Message(string role, string text, long at, bool note)
        {
            if (role == "user" || note)
            {
                records.Add(string.Join('\u001F', "M", role == "user" ? "u" : "n", MessageTime(at), "-1"));
                records.Add(string.Join('\u001F', "B", "P", "0", "", Clean(text), "", "", "-1"));
                return;
            }
            var copy = Act(() =>
            {
                try
                {
                    Clipboard.SetText(text);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                }
            });
            records.Add(string.Join('\u001F', "M", "a", MessageTime(at), text.Length > 0 ? copy.ToString() : "-1"));
            BlockRecords(MarkdownBlocks(text, LinkAction, WebFile), records);
        }
        foreach (var turn in turns) Message(turn.Role, turn.Text, turn.At, note: false);
        if (page.Asking is { } asking)
        {
            Message("user", asking, page.AskedAt, note: false);
            Message("assistant", page.Answer, 0, page.AnswerIsNote);
        }
        return string.Join('\u001E', records);
    }

    /// <summary>A click on a chat page, from the island.</summary>
    private void AiAction(int action)
    {
        if (ShownAi is { } page && action >= 0 && action < page.Actions.Count) page.Actions[action]();
    }

    /// <summary>The reply box, from the island: 0 its text changed, 1 Enter (send).</summary>
    private void AiEdit(int box, int what)
    {
        if (box != ReplyBox || ShownAi is not { } page) return;
        if (what == 0)
        {
            page.Draft = AiEditText();
        }
        else if (what == 1)
        {
            page.Draft = AiEditText(submitted: true);
            if (page.Running is not null)
            {
                // Enter while a reply comes: it stops, and what was typed stays.
                AiSetEditText(page.Draft);
                page.Running.Cancel();
                return;
            }
            _ = AiSend(page);
        }
    }

    private async Task AiSend(AiPage page)
    {
        if (page.Running is not null)
        {
            page.Running.Cancel();
            return;
        }
        var engine = page.Engine;
        var name = Assistant.Name(engine);
        var question = page.Draft.Trim();
        if (question.Length == 0) return;
        var key = Assistant.Secret(engine) is { } id ? Secrets.Get(id) : null;
        if (key is null)
        {
            AiChanged(page);
            return;
        }
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var before = page.Current;
        var chat = page.Current ?? new Chat(Guid.NewGuid().ToString("N"), Assistant.Id(engine), now, []);
        var turns = chat.Turns.Append(new ChatTurn("user", question, now)).ToList();
        page.History = false;
        page.Draft = "";
        page.Asking = question;
        page.AskedAt = now;
        page.Answer = "Thinking…";
        page.AnswerIsNote = true;

        var cancel = new CancellationTokenSource();
        page.Running = cancel;
        AiChanged(page);
        var answer = new StringBuilder();
        var failed = false;
        // Until the first words: "Thinking… 12s", so a slow reply does not look like nothing.
        var asked = DateTime.Now;
        var waiting = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        waiting.Tick += (_, _) =>
        {
            if (answer.Length > 0)
            {
                waiting.Stop();
                return;
            }
            page.Answer = $"Thinking… {(int)(DateTime.Now - asked).TotalSeconds}s";
            AiChanged(page);
        };
        waiting.Start();
        void Show(string text, bool note)
        {
            page.Answer = text;
            page.AnswerIsNote = note;
            AiChanged(page);
        }
        try
        {
            var model = _settings.Get(Assistant.ModelSetting(engine));
            var own = _settings.Get("Assistant.Instructions");
            var prompt = _settings.Get("Assistant.Prompt");
            var note = await Task.Run(() => _assistant.Ask(engine, model, key, turns, piece => Dispatcher.BeginInvoke(() =>
            {
                answer.Append(piece);
                Show(answer.ToString(), false);
            }), cancel.Token, own, prompt));
            // The last pieces may still be on their way to this thread.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            answer.Append(note);
            Show(answer.ToString(), false);
        }
        catch (OperationCanceledException)
        {
            // Stopped: what came so far is kept.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (answer.Length > 0) Show(answer + " …", false);
            else
            {
                Show("Stopped.", true);
                failed = true;
            }
        }
        catch (AssistantException e)
        {
            failed = true;
            Show(e.Message, true);
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or System.IO.IOException)
        {
            failed = true;
            Show($"Could not reach {name}.", true);
        }
        finally
        {
            waiting.Stop();
            if (ReferenceEquals(page.Running, cancel)) page.Running = null;
            cancel.Dispose();
        }

        if (failed)
        {
            // The question stays on screen with why it failed, but not in the chat: send it again to retry.
            page.Current = before;
            if (page.Draft.Length == 0)
            {
                page.Draft = question;
                AiSetEditText(question);
            }
            AiChanged(page);
            return;
        }
        page.Current = chat with { At = now, Turns = [.. turns, new ChatTurn("assistant", answer.ToString(), DateTimeOffset.Now.ToUnixTimeMilliseconds())] };
        page.Asking = null;
        AiChanged(page);
        if (_settings.Get("Assistant.KeepHistory") == "true") _chats.Save(page.Current);
    }

    /// <summary>When a message was sent: the time today, the date as well before that. Empty when unknown.</summary>
    private static string MessageTime(long at)
    {
        if (at <= 0) return "";
        var when = DateTimeOffset.FromUnixTimeMilliseconds(at).LocalDateTime;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return when.Date == DateTime.Today ? when.ToString("t", culture) : when.ToString("d MMM, ", culture) + when.ToString("t", culture);
    }

    /// <summary>When, as a short phrase: "now", "5 min", the time today, the date before.</summary>
    private static string Ago(DateTime when)
    {
        var gap = DateTime.Now - when;
        if (gap < TimeSpan.FromMinutes(1)) return "now";
        if (gap < TimeSpan.FromHours(1)) return $"{(int)gap.TotalMinutes} min";
        if (gap < TimeSpan.FromDays(1) && when.Date == DateTime.Today) return when.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
        return when.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.App;

// The Terminal page: cmd and PowerShell sessions in tabs, running inside the pill. The island draws it
// (native), with its own text boxes for the command line and the output; this runs the shells.
// The shells run with their input and output piped, not in a console, so line-by-line
// commands work well; full-screen programs (vim, htop) and coloured output do not.
internal sealed partial class DeckWindow
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>One shell in a tab.</summary>
    private sealed class Session
    {
        public required ShellKind Kind { get; init; }
        public required int Number { get; init; }
        public Process? Process { get; set; }
        public ShellReader Reader { get; set; } = new();
        public bool Busy { get; set; }
        public string Folder { get; set; } = Home;
        public StringBuilder Inbox { get; } = new();
        public StringBuilder Output { get; } = new();
        public List<string> History { get; } = [];
        public int Recall { get; set; } = -1;
        public string Name => (Kind == ShellKind.Cmd ? "cmd" : "PS") + " " + Number;
    }

    private readonly List<Session> _sessions = [];
    private Session? _session;
    private int _sessionNumber;
    private readonly DispatcherTimer _terminalFlush = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private static string? PowerShellPath()
    {
        // PowerShell 7 when it is installed, else the one that comes with Windows.
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim(), "pwsh.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }

    /// <summary>Where a new tab starts: the folder the settings name, if it is there; else the user's folder.</summary>
    private string StartFolder()
    {
        var chosen = Environment.ExpandEnvironmentVariables(_settings.Get("Deck.TerminalFolder").Trim().Trim('"'));
        return chosen.Length > 0 && Directory.Exists(chosen) ? chosen : Home;
    }

    private Session OpenShell(ShellKind kind, string? folder = null)
    {
        var session = new Session { Kind = kind, Number = ++_sessionNumber, Folder = folder ?? StartFolder() };
        Launch(session);
        _sessions.Add(session);
        _session = session;
        if (!_terminalFlush.IsEnabled)
        {
            _terminalFlush.Tick += (_, _) => FlushTerminals();
            _terminalFlush.Start();
        }
        return session;
    }

    private void Launch(Session session)
    {
        var start = session.Kind == ShellKind.Cmd
            ? new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", "/Q")
            : new ProcessStartInfo(PowerShellPath() ?? "powershell.exe", "-NoLogo -NoProfile -Command -");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = new UTF8Encoding(false);
        start.StandardErrorEncoding = new UTF8Encoding(false);
        start.StandardInputEncoding = new UTF8Encoding(false);
        start.WorkingDirectory = Directory.Exists(session.Folder) ? session.Folder : Home;
        // Plain text out: no colours, and no pager waiting for a key.
        start.Environment["NO_COLOR"] = "1";
        start.Environment["TERM"] = "dumb";
        start.Environment["GIT_PAGER"] = "cat";
        start.Environment["PAGER"] = "cat";
        start.Environment["PYTHONUNBUFFERED"] = "1";
        // cmd prints its prompt before reading each line; this one is a colour reset, which the reader takes out.
        if (session.Kind == ShellKind.Cmd) start.Environment["PROMPT"] = "$E[0m";
        session.Reader = new ShellReader();
        try
        {
            var process = Process.Start(start)!;
            // Closing Nyatch, even by a crash, ends the shell and everything started in it.
            Native.KeepWithApp(process);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (session.Process != process) return;
                session.Process = null;
                session.Busy = false;
                Append(session, "\n[The shell has ended. Press Enter to start it again.]\n");
            });
            session.Process = process;
            session.Busy = true;
            _ = Pump(session, process, process.StandardOutput, marked: true);
            _ = Pump(session, process, process.StandardError, marked: false);
            foreach (var line in Shell.Setup(session.Kind)) process.StandardInput.WriteLine(line);
            process.StandardInput.Flush();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            session.Process = null;
            session.Busy = false;
            Append(session, $"[Could not start {session.Name}: {e.Message}]\n");
        }
    }

    /// <summary>Read one of a shell's outputs into its inbox; the screen takes it from there in small batches.</summary>
    private async Task Pump(Session session, Process process, StreamReader reader, bool marked)
    {
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0) break;
                lock (session.Inbox)
                {
                    // Error output never carries markers; it is marked so the reader passes it straight through.
                    if (marked) session.Inbox.Append(buffer, 0, read);
                    else session.Inbox.Append('\u0001').Append(buffer, 0, read).Append('\u0002');
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    /// <summary>Move what the shells wrote onto the screen.</summary>
    private void FlushTerminals()
    {
        foreach (var session in _sessions)
        {
            string arrived;
            lock (session.Inbox)
            {
                if (session.Inbox.Length == 0) continue;
                arrived = session.Inbox.ToString();
                session.Inbox.Clear();
            }
            var text = new StringBuilder();
            var finished = false;
            var at = 0;
            while (at < arrived.Length)
            {
                var errorStart = arrived.IndexOf('\u0001', at);
                var plain = errorStart < 0 ? arrived[at..] : arrived[at..errorStart];
                var (shown, done) = session.Reader.Feed(plain);
                text.Append(shown);
                finished |= done;
                if (errorStart < 0) break;
                var errorEnd = arrived.IndexOf('\u0002', errorStart);
                if (errorEnd < 0) errorEnd = arrived.Length;
                text.Append(Shell.StripCodes(arrived[(errorStart + 1)..errorEnd].Replace("\r\n", "\n")));
                at = errorEnd + 1;
            }
            if (finished)
            {
                session.Busy = false;
                session.Folder = session.Reader.Folder.Length > 0 ? session.Reader.Folder : session.Folder;
            }
            Append(session, text.ToString(), redraw: finished);
        }
    }

    private void Append(Session session, string text, bool redraw = true)
    {
        if (text.Length > 0)
        {
            session.Output.Append(text);
            // Keep the last 200,000 characters.
            if (session.Output.Length > 250_000) session.Output.Remove(0, session.Output.Length - 200_000);
        }
        if (ReferenceEquals(session, _session) && (text.Length > 0 || redraw)) TerminalChanged();
    }

    private void Send(Session session, string line)
    {
        if (session.Process is not { HasExited: false } process)
        {
            Launch(session);
            return;
        }
        try
        {
            if (session.Busy)
            {
                // A program is running and may be waiting for input: pass the line to it as it is.
                process.StandardInput.WriteLine(line);
            }
            else
            {
                session.Busy = true;
                process.StandardInput.WriteLine(Shell.Command(session.Kind, Shell.ForPipes(session.Kind, line)));
            }
            process.StandardInput.Flush();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            Append(session, "[The shell is not listening any more.]\n");
        }
    }

    /// <summary>Stop whatever is running by starting the shell over, in the same folder.</summary>
    private void Stop(Session session)
    {
        End(session);
        Append(session, "^C\n");
        Launch(session);
    }

    private static void End(Session session)
    {
        var process = session.Process;
        session.Process = null;
        session.Busy = false;
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
        process?.Dispose();
    }

    private void Close(Session session)
    {
        End(session);
        var at = _sessions.IndexOf(session);
        _sessions.Remove(session);
        if (ReferenceEquals(_session, session)) _session = _sessions.Count == 0 ? null : _sessions[Math.Clamp(at - 1, 0, _sessions.Count - 1)];
    }

    private void StopTerminals()
    {
        foreach (var session in _sessions.ToList()) End(session);
    }

    /// <summary>
    /// How long the unfinished last line of the output is, if it looks like a program asking for input
    /// (short and not blank); else 0.
    /// </summary>
    private static int TrailingPart(StringBuilder output)
    {
        var length = 0;
        for (var i = output.Length - 1; i >= 0 && output[i] != '\n'; i--) length++;
        if (length == 0 || length > 60) return 0;
        return output.ToString(output.Length - length, length).Trim().Length == 0 ? 0 : length;
    }

    // The island's text boxes on this page.
    private const int CommandBox = 20;

    /// <summary>What is typed on the command line, and the terminal as last sent to the island.</summary>
    private string _command = "";
    private int _termVersion, _termSent = -1;

    // What each click does, by the action number the island sends back; rebuilt with the data.
    private readonly List<Action> _termActions = [];

    /// <summary>Something on the terminal changed: the island gets it if the page is on show.</summary>
    private void TerminalChanged()
    {
        _termVersion++;
        if (_expanded && _tab >= 0 && _tab < _tabs.Count && _tabs[_tab].Native == "terminal") SendNativeData();
    }

    private static string Prompt(Session session, int partial) =>
        !session.Busy ? (session.Kind == ShellKind.Cmd ? "" : "PS ") + Shell.Squeeze(session.Folder, 34) + ">"
        : partial > 0 ? session.Output.ToString(session.Output.Length - partial, partial).TrimEnd()
        : "…";

    /// <summary>The Terminal page for the island (see its DrawDeckTerminal).</summary>
    private string TerminalData()
    {
        _termSent = _termVersion;
        _termActions.Clear();
        int Act(Action action)
        {
            _termActions.Add(action);
            return _termActions.Count - 1;
        }
        static string Clean(string text) => text.Replace('\u001E', ' ').Replace('\u001F', ' ');
        var records = new List<string>();
        foreach (var session in _sessions)
        {
            var choose = Act(() =>
            {
                _session = session;
                TerminalChanged();
                Island.DeckFocusField(CommandBox);
            });
            var close = Act(() =>
            {
                Close(session);
                TerminalChanged();
            });
            records.Add(string.Join('\u001F', "T", session.Name, ReferenceEquals(session, _session) ? "1" : "0", choose.ToString(), close.ToString()));
        }
        // +: a small menu, Command Prompt or PowerShell.
        var add = Act(() => ItemMenu(("Command Prompt", () => NewShell(ShellKind.Cmd)), ("PowerShell", () => NewShell(ShellKind.PowerShell))));
        records.Add($"B\u001F{add}");

        if (_session is not { } current)
        {
            records.Add("X\u001FOpen a Command Prompt or PowerShell tab with the + button.");
            return string.Join('\u001E', records);
        }
        // While a program runs, the line it left unfinished (">>> ", "Enter name: ") is its prompt:
        // it is shown in front of the command line, and kept out of the output above until the line is done.
        var partial = current.Busy ? TrailingPart(current.Output) : 0;
        var stop = Act(() =>
        {
            Stop(current);
            TerminalChanged();
        });
        records.Add(string.Join('\u001F', "P", Clean(Prompt(current, partial)), current.Busy && current.Process is not null ? "1" : "0", stop.ToString(), Clean(_command)));
        // The last 20,000 characters of what the shell said.
        var visible = current.Output.Length - partial;
        var from = Math.Max(0, visible - 20_000);
        records.Add("O\u001F" + Clean(current.Output.ToString(from, visible - from)));
        return string.Join('\u001E', records);
    }

    private void NewShell(ShellKind kind)
    {
        OpenShell(kind, _session?.Folder);
        _command = "";
        TerminalChanged();
        Island.DeckFocusField(CommandBox);
    }

    /// <summary>A click on the Terminal page, from the island.</summary>
    private void TerminalAction(int action)
    {
        if (action >= 0 && action < _termActions.Count) _termActions[action]();
    }

    /// <summary>
    /// The command line, from the island: 0 its text changed, 1 Enter, 3 Up, 4 Down, 5 Ctrl+C with
    /// nothing selected (stop what runs), 6 Ctrl+L (clear).
    /// </summary>
    private void TerminalEdit(int box, int what)
    {
        if (box != CommandBox || _session is not { } session) return;
        switch (what)
        {
            case 0:
                _command = Island.DeckEditText();
                break;
            case 1:
            {
                var text = Island.DeckEditText(submitted: true);
                _command = "";
                session.Recall = -1;
                if (text.Trim().Length > 0 && (session.History.Count == 0 || session.History[^1] != text)) session.History.Add(text);
                var command = text.Trim().ToLowerInvariant();
                if (!session.Busy && command is "cls" or "clear" or "clear-host")
                {
                    session.Output.Clear();
                    TerminalChanged();
                    return;
                }
                if (!session.Busy && command == "exit")
                {
                    Close(session);
                    TerminalChanged();
                    return;
                }
                var partial = session.Busy ? TrailingPart(session.Output) : 0;
                Append(session, (session.Busy ? "" : Prompt(session, partial) + " ") + text + "\n");
                Send(session, text);
                TerminalChanged();
                break;
            }
            case 3 or 4 when session.History.Count > 0:
            {
                var at = session.Recall < 0 ? session.History.Count : session.Recall;
                at = Math.Clamp(at + (what == 3 ? -1 : 1), 0, session.History.Count);
                session.Recall = at == session.History.Count ? -1 : at;
                _command = at == session.History.Count ? "" : session.History[at];
                Island.DeckSetEditText(CommandBox, _command);
                break;
            }
            case 5:
                Stop(session);
                TerminalChanged();
                break;
            case 6:
                session.Output.Clear();
                TerminalChanged();
                break;
        }
    }
}

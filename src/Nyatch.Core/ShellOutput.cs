using System.Text;
using System.Text.RegularExpressions;

namespace Nyatch.Core;

/// <summary>The two shells the terminal page can run.</summary>
public enum ShellKind { Cmd, PowerShell }

/// <summary>
/// How the terminal page talks to a shell whose input and output are pipes, not a console.
/// After each command it asks the shell to print a marker and then its current folder; the reader
/// takes those lines out of what is shown, and so learns when a command has finished and where it left off.
/// </summary>
public static partial class Shell
{
    public const string Marker = "__NYATCH_CWD__";

    /// <summary>The line written for a command typed at the prompt.</summary>
    public static string Command(ShellKind kind, string command) => kind switch
    {
        // "cd" with nothing after it prints the folder as it is once the command has run.
        ShellKind.Cmd => $"{command} & echo {Marker} & cd",
        _ => $"{command}; Write-Output \"{Marker}\"; (Get-Location).ProviderPath",
    };

    /// <summary>Lines written once, when the shell starts: text as UTF-8, no colour codes or progress bars.</summary>
    public static IReadOnlyList<string> Setup(ShellKind kind) => kind switch
    {
        ShellKind.Cmd => ["chcp 65001 >nul", $"echo {Marker} & cd"],
        _ =>
        [
            "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'; if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }",
            $"Write-Output \"{Marker}\"; (Get-Location).ProviderPath",
        ],
    };

    /// <summary>A long path with its middle cut out: "C:\Users\me\Docu…\Projects\nyatch". Short ones are left whole.</summary>
    public static string Squeeze(string path, int max)
    {
        if (max < 5 || path.Length <= max) return path;
        var head = (max - 1) * 2 / 5;
        var tail = max - 1 - head;
        return path[..head] + "\u2026" + path[^tail..];
    }

    /// <summary>
    /// REPLs started with nothing after them read a whole script from a pipe and so seem to hang.
    /// Started this way they run interactively over the pipe instead, and Python prints as it goes.
    /// </summary>
    public static string ForPipes(ShellKind kind, string command)
    {
        var bare = command.Trim();
        var interactive = bare.ToLowerInvariant() switch
        {
            "python" or "python3" or "py" or "python.exe" or "py.exe" or "python3.exe" => bare + " -i -u",
            "node" or "node.exe" => bare + " -i",
            _ => null,
        };
        if (interactive is null) return command;
        // The REPL's prompts go to its error stream; joined to the output stream they stay in order.
        // (In PowerShell, through cmd: PowerShell would wrap each prompt as an error record.)
        return kind == ShellKind.Cmd ? interactive + " 2>&1" : $"cmd /c \"{interactive} 2>&1\"";
    }

    /// <summary>"C:\Users\me\Projects\x" → "~\Projects\x".</summary>
    public static string ShortFolder(string folder, string home)
    {
        if (home.Length > 0 && folder.StartsWith(home, StringComparison.OrdinalIgnoreCase) && (folder.Length == home.Length || folder[home.Length] == '\\'))
        {
            return "~" + folder[home.Length..];
        }
        return folder;
    }

    /// <summary>Colour and cursor codes, which a plain text box would show as junk.</summary>
    public static string StripCodes(string text) => Codes().Replace(text, "");

    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[@-Z\\-_])")]
    private static partial Regex Codes();
}

/// <summary>Reads a shell's output as it arrives, in pieces of any size, and takes out the markers.</summary>
public sealed class ShellReader
{
    private readonly StringBuilder _pending = new();
    private bool _folderNext;
    private bool _skipBlank;

    /// <summary>The folder the shell was last seen in.</summary>
    public string Folder { get; private set; } = "";

    /// <summary>Feed what was read. Returns the text to show, and whether a command has just finished.</summary>
    public (string Text, bool Finished) Feed(string chunk)
    {
        _pending.Append(chunk.Replace("\r\n", "\n").Replace('\r', '\n'));
        var shown = new StringBuilder();
        var finished = false;
        var text = _pending.ToString();
        var start = 0;
        while (true)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0) break;
            var line = text[start..end];
            start = end + 1;
            var plain = Shell.StripCodes(line);
            if (_folderNext)
            {
                _folderNext = false;
                _skipBlank = true;
                Folder = plain.Trim();
                finished = true;
                continue;
            }
            // cmd puts its (invisible) prompt in front of the marker.
            if (plain.TrimEnd() == Shell.Marker)
            {
                _folderNext = true;
                continue;
            }
            // cmd follows each command with an empty line; one is dropped.
            var skip = _skipBlank && plain.Trim().Length == 0;
            _skipBlank = false;
            if (skip) continue;
            shown.Append(plain).Append('\n');
        }
        var rest = text[start..];
        _pending.Clear();
        // A part line is shown at once (a question waiting for an answer), unless it could be the start of a marker.
        var restPlain = Shell.StripCodes(rest);
        if (rest.Length > 0 && !_folderNext && !rest.EndsWith('\x1B') && !Shell.Marker.StartsWith(restPlain.TrimEnd(), StringComparison.Ordinal))
        {
            shown.Append(restPlain);
            if (restPlain.Length > 0) _skipBlank = false;
        }
        else
        {
            _pending.Append(rest);
        }
        return (shown.ToString(), finished);
    }
}

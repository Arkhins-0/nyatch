using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Nyatch.App;

/// <summary>
/// Reporting a problem and sending feedback. Nothing is sent by Nyatch itself: both open an email to
/// Arkhins, a report with the version, Windows and the last crash filled in, for you to read, edit and
/// send. Crashes are written to a log on this PC so a report can include them.
/// </summary>
internal static class Feedback
{
    private const string Email = "arkhins@arkhins.com";
    private const string Separator = "\n----- ";

    private static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyatch", "crash.log");

    private static string Version
    {
        get
        {
            var v = typeof(Feedback).Assembly.GetName().Version ?? new System.Version(1, 0, 0);
            return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    /// <summary>Write every crash to the log. The crash itself still happens as before.</summary>
    public static void Install(Application app)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Write(ex); };
        app.DispatcherUnhandledException += (_, e) => Write(e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write(e.Exception);
    }

    private static void Write(Exception error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var text = error.ToString();
            if (text.Length > 8000) text = text[..8000] + "\n…";
            File.AppendAllText(LogPath, $"{Separator}{DateTime.Now:yyyy-MM-dd HH:mm:ss} · Nyatch {Version}\n{text}\n");
            // Only the latest few are worth keeping.
            var all = File.ReadAllText(LogPath);
            if (all.Length > 64_000) File.WriteAllText(LogPath, all[^48_000..]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The most recent crash in the last week: when, and what. Null if none.</summary>
    public static (DateTime When, string Text)? LastCrash()
    {
        try
        {
            if (!File.Exists(LogPath)) return null;
            var entries = File.ReadAllText(LogPath).Replace("\r\n", "\n").Split(Separator, StringSplitOptions.RemoveEmptyEntries);
            var last = entries.LastOrDefault()?.Trim();
            if (string.IsNullOrEmpty(last) || last.Length < 19) return null;
            if (!DateTime.TryParse(last[..19], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when)) return null;
            if (DateTime.Now - when > TimeSpan.FromDays(7)) return null;
            var body = last.Contains('\n') ? last[(last.IndexOf('\n') + 1)..] : "";
            return (when, body);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string WindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var display = key?.GetValue("DisplayVersion") as string ?? "";
        var build = key?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString();
        var revision = key?.GetValue("UBR") is int ubr ? $".{ubr}" : "";
        return $"Windows {display} (build {build}{revision})".Replace("  ", " ");
    }

    /// <summary>Open a new GitHub issue with the details filled in, to read and send yourself.</summary>
    public static void ReportProblem()
    {
        var body = new StringBuilder();
        body.Append("What happened?\n\n\nWhat did you expect?\n\n\nHow can it be repeated?\n\n\n—\n");
        body.Append($"Nyatch {Version} · {WindowsVersion()}\n");
        if (LastCrash() is { } crash)
        {
            var lines = crash.Text.Split('\n').Take(30);
            var text = string.Join("\n", lines);
            // An email link holds only so much: the start of the crash is the part that says where.
            if (text.Length > 1200) text = text[..1200] + "\n…";
            body.Append($"\nLast crash, {crash.When:d MMM HH:mm}:\n{text}\n");
        }
        Open($"mailto:{Email}?subject={Uri.EscapeDataString($"Nyatch {Version}: a problem")}&body={Uri.EscapeDataString(body.ToString())}");
    }

    /// <summary>Write to Arkhins: opens an email with the version filled in.</summary>
    public static void SendFeedback()
    {
        var subject = $"Nyatch feedback ({Version})";
        var body = $"\n\n\n—\nNyatch {Version} · {WindowsVersion()}";
        Open($"mailto:{Email}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(target.StartsWith("mailto:", StringComparison.Ordinal)
                ? $"No email app is set up. Write to {Email}."
                : "The browser could not be opened.", "Nyatch", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

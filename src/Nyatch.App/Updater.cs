using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// Keeps Nyatch up to date from its GitHub releases. It looks for a newer release when Nyatch starts and
/// every six hours. A copy installed with the Nyatch installer downloads it, checks its signature against
/// the public key built into Nyatch (so only releases signed by Arkhins are ever installed), and installs
/// it quietly while the PC is idle; the installer closes Nyatch, upgrades
/// it in place and starts it again. A copy run from a build folder is only told that a new version exists.
/// </summary>
internal sealed class Updater
{
    public enum State { Idle, Checking, UpToDate, Available, Downloading, Ready, Installing, Failed }

    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan IdleBeforeInstall = TimeSpan.FromMinutes(2);

    private readonly SettingsStore _settings;
    private readonly Action<string, string> _notify;
    private readonly Action _quit;
    private readonly HttpClient _web;
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _waitForIdle = new() { Interval = TimeSpan.FromMinutes(1) };
    private string? _readyFile;
    private bool _busy;

    public Updater(SettingsStore settings, Action<string, string> notify, Action quit)
    {
        _settings = settings;
        _notify = notify;
        _quit = quit;
        var version = typeof(Updater).Assembly.GetName().Version ?? new Version(1, 0, 0);
        Current = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        _web = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub asks every caller to say who it is.
        _web.DefaultRequestHeaders.UserAgent.ParseAdd($"Nyatch/{Current}");
        _web.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = Interval;
            if (_settings.Get("Updates.AutoCheck") == "true") await CheckAsync(manual: false);
        };
        _waitForIdle.Tick += (_, _) =>
        {
            if (Native.IdleTime() < IdleBeforeInstall) return;
            _waitForIdle.Stop();
            Install();
        };
        CleanOldDownloads();
    }

    /// <summary>The version running now.</summary>
    public Version Current { get; }

    public State Status { get; private set; } = State.Idle;

    /// <summary>One short line about where things are, for the settings page.</summary>
    public string Message { get; private set; } = "";

    public Release? Latest { get; private set; }

    public DateTime? LastChecked { get; private set; }

    /// <summary>0 to 1 while downloading.</summary>
    public double Progress { get; private set; }

    /// <summary>Raised on the screen's thread whenever any of the above changes.</summary>
    public event Action? Changed;

    /// <summary>This copy was put here by the Nyatch installer (and so can be updated by it).</summary>
    public static bool IsInstalledCopy
    {
        get
        {
            // Written by the installer, for the whole PC (older installers wrote it for the user).
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"Software\Nyatch");
            using var user = Registry.CurrentUser.OpenSubKey(@"Software\Nyatch");
            if ((machine?.GetValue("InstallFolder") ?? user?.GetValue("InstallFolder")) is not string folder || folder.Length == 0) return false;
            static string Plain(string path) => Path.GetFullPath(path).TrimEnd('\\');
            try
            {
                return string.Equals(Plain(folder), Plain(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
    }

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyatch", "Updates");

    public void Start()
    {
        _timer.Interval = FirstCheck;
        _timer.Start();
    }

    private void Set(State status, string message)
    {
        Status = status;
        Message = message;
        Changed?.Invoke();
    }

    /// <summary>Ask the site for the newest release. <paramref name="manual"/>: the "Check now" button.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (_busy) return;
        _busy = true;
        Set(State.Checking, "Checking for updates");
        try
        {
            using var reply = await _web.GetAsync(Updates.LatestUrl());
            LastChecked = DateTime.Now;
            if (reply.StatusCode == HttpStatusCode.NotFound)
            {
                Latest = null;
                Set(State.UpToDate, "No releases are published yet.");
                return;
            }
            if (reply.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                Set(State.Failed, "The site asked to wait before checking again.");
                return;
            }
            reply.EnsureSuccessStatusCode();
            var release = Updates.Parse(await reply.Content.ReadAsStringAsync());
            Latest = release;
            if (release is null || release.Version <= Current)
            {
                Set(State.UpToDate, $"Nyatch {Current} is the latest version.");
                return;
            }
            Set(State.Available, $"Nyatch {release.Version} is available.");
            var automatic = _settings.Get("Updates.AutoInstall") == "true" && IsInstalledCopy;
            if (manual || automatic)
            {
                _busy = false;
                await DownloadAsync(installNow: manual);
            }
            else
            {
                _notify("Nyatch update", $"Version {release.Version} is available. Open Nyatch settings, Updates, to install it.");
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            Set(State.Failed, "Could not reach nyatch.arkhins.com. Nyatch will try again later.");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Download the newest release and check it. Installs it straight away, or once the PC is idle.</summary>
    public async Task DownloadAsync(bool installNow)
    {
        if (_busy || Latest is not { } release) return;
        if (!IsInstalledCopy)
        {
            Set(State.Available, $"Nyatch {release.Version} is available. This copy was not installed with the installer; download the new version from its release page.");
            return;
        }
        if (release.Signature is null)
        {
            Set(State.Failed, $"Nyatch {release.Version} is not signed, so it is not installed. Only signed releases are installed.");
            return;
        }
        _busy = true;
        try
        {
            Directory.CreateDirectory(Folder);
            var target = Path.Combine(Folder, Updates.SafeFileName(release.InstallerName));
            var partial = target + ".part";

            Progress = 0;
            Set(State.Downloading, $"Downloading Nyatch {release.Version}");
            var signature = await _web.GetStringAsync(release.Signature);
            var expected = release.Checksum is null ? null : Updates.ChecksumFor(await _web.GetStringAsync(release.Checksum), release.InstallerName);

            using (var reply = await _web.GetAsync(release.Installer, HttpCompletionOption.ResponseHeadersRead))
            {
                reply.EnsureSuccessStatusCode();
                var total = reply.Content.Headers.ContentLength ?? 0;
                await using var from = await reply.Content.ReadAsStreamAsync();
                await using var to = File.Create(partial);
                var buffer = new byte[81920];
                long done = 0;
                var lastShown = DateTime.MinValue;
                int read;
                while ((read = await from.ReadAsync(buffer)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0 && DateTime.UtcNow - lastShown > TimeSpan.FromMilliseconds(250))
                    {
                        lastShown = DateTime.UtcNow;
                        Progress = (double)done / total;
                        Changed?.Invoke();
                    }
                }
            }

            // Installed only if Arkhins signed exactly this file for exactly this version.
            string actual;
            await using (var file = File.OpenRead(partial))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
            }
            if ((expected is not null && actual != expected) || !ReleaseSignature.Verify(ReleaseKey.PublicKeyPem, release.Version, actual, signature))
            {
                File.Delete(partial);
                Set(State.Failed, "The download's signature did not check out, so it was deleted and not installed.");
                return;
            }
            File.Move(partial, target, overwrite: true);
            _readyFile = target;
            Progress = 1;
            if (installNow)
            {
                Install();
            }
            else
            {
                // Installing asks for administrator approval, so it waits for you rather than for an idle PC.
                Set(State.Ready, $"Nyatch {release.Version} is ready to install.");
                _notify("Nyatch update", $"Version {release.Version} is ready. Open Nyatch settings, Updates, and choose Install now.");
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Set(State.Failed, "The download stopped. Nyatch will try again later.");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Run the downloaded installer quietly and close Nyatch; the installer starts the new version.</summary>
    public void Install()
    {
        if (_readyFile is null || !File.Exists(_readyFile)) return;
        try
        {
            var log = Path.Combine(Folder, "install.log");
            // Nyatch is installed for the whole PC, so the installer needs administrator rights: Windows
            // asks (its usual prompt), then it runs without any more questions.
            // The setup program runs itself; an .msi (a release from before it) through msiexec.
            // The installer starts the new version when it is done (AUTOLAUNCH=1), as you and not as
            // administrator. (A helper of Nyatch's own could not wait for it: an administrator's process
            // cannot be waited on from here, so it started Nyatch too soon, and the installer closed it.)
            var start = _readyFile.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("msiexec.exe", $"/i \"{_readyFile}\" AUTOLAUNCH=1 /qn /norestart /l*v \"{log}\"")
                : new ProcessStartInfo(_readyFile, $"/quiet /norestart AUTOLAUNCH=1 /log \"{log}\"");
            start.UseShellExecute = true;
            start.Verb = "runas";
            Process.Start(start);
            Set(State.Installing, "Installing the update. Nyatch restarts by itself.");
            // Out of the installer's way, so it can replace this copy's files.
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            later.Tick += (_, _) =>
            {
                later.Stop();
                _quit();
            };
            later.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Also when the Windows prompt was declined.
            Set(State.Ready, "The update was not installed: it needs your approval at the Windows prompt. Use Install now to try again.");
        }
    }

    /// <summary>Installers of this version or older are not needed any more.</summary>
    private void CleanOldDownloads()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.EnumerateFiles(Folder))
            {
                var version = Updates.ParseVersion(Path.GetFileName(file));
                if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || (version is not null && version <= Current)) File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

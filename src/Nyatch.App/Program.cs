using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Nyatch.Core;
using Forms = System.Windows.Forms;

namespace Nyatch.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Run by the uninstaller: take Nyatch off the programs started at sign-in, and stop.
        if (Array.Exists(args, a => a.Equals("--forget-startup", StringComparison.OrdinalIgnoreCase)))
        {
            SignInLaunch.Apply(false);
            PackFiles.Forget();
            return;
        }
        // Run by the uninstaller (not for updates, not when silent): ask whether your data goes too.
        if (Array.Exists(args, a => a.Equals("--ask-remove-data", StringComparison.OrdinalIgnoreCase)))
        {
            UninstallData.Ask();
            return;
        }

        // For checking the theme editor: its self-test, a picture of it, and its picture tools, then stop.
        //   --theme-editor-test <log>, --theme-editor-shot <theme folder> <png> [slot],
        //   --theme-editor-shrink | -cover | -fill <theme folder> ...
        var editor = Array.FindIndex(args, a => a.StartsWith("--theme-editor-", StringComparison.OrdinalIgnoreCase));
        if (editor >= 0)
        {
            var verb = args[editor]["--theme-editor-".Length..].ToLowerInvariant();
            Environment.ExitCode = Nyatch.ThemeEditor.ThemeEditor.Command(verb == "test" ? "selftest" : verb, args[(editor + 1)..]);
            return;
        }

        // For checking the settings window: draw its pages to pictures in a folder, and stop.
        var shots = Array.FindIndex(args, a => a.Equals("--shots", StringComparison.OrdinalIgnoreCase));
        if (shots >= 0 && shots + 1 < args.Length)
        {
            SettingsShots.Run(args[shots + 1], args[(shots + 2)..]);
            return;
        }

        // For checking the install popup: draw it for a pack file to a picture, and stop.
        var packShot = Array.FindIndex(args, a => a.Equals("--pack-shot", StringComparison.OrdinalIgnoreCase));
        if (packShot >= 0 && packShot + 2 < args.Length)
        {
            PackInstallWindow.Shot(args[packShot + 1], args[packShot + 2]);
            return;
        }

        // Opened with a character pack (a .nyatch file): it is shown first (its name, its pictures, on its
        // colour), and installed, then shown on the Characters page, only if Install is clicked.
        var packOpened = false;
        if (Array.Find(args, a => a.EndsWith(CharacterPacks.Extension, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(a)) is { } packFile)
        {
            if (!PackInstallWindow.Ask(packFile)) return;
            try
            {
                new CharacterPacks().InstallFile(packFile);
                packOpened = true;
            }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                MessageBox.Show($"{System.IO.Path.GetFileName(packFile)} was not installed.\n\n{error.Message}", "Nyatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // One Nyatch per user session. Opening it again asks the running one
        // to show its settings.
        using var only = new Mutex(initiallyOwned: true, @"Local\Nyatch.Running", out var first);
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Nyatch.ShowSettings");
        using var showPacks = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Nyatch.ShowPacks");
        if (!first)
        {
            if (packOpened) showPacks.Set();
            else showSettings.Set();
            return;
        }
        var app = new NyatchApp();
        var waiting = ThreadPool.RegisterWaitForSingleObject(showSettings, (_, _) => app.Dispatcher.BeginInvoke(app.ShowSettings), null, Timeout.Infinite, executeOnlyOnce: false);
        var waitingPacks = ThreadPool.RegisterWaitForSingleObject(showPacks, (_, _) => app.Dispatcher.BeginInvoke(app.PackArrived), null, Timeout.Infinite, executeOnlyOnce: false);
        if (packOpened) app.Dispatcher.BeginInvoke(app.PackArrived, DispatcherPriority.ApplicationIdle);
        app.Run();
        waiting.Unregister(null);
        waitingPacks.Unregister(null);
    }
}

/// <summary>
/// The host: starts the island, answers its questions, sits in the
/// notification area, and opens the settings window.
/// </summary>
internal sealed class NyatchApp : Application
{
    private readonly SettingsStore _settings = new(SettingsSchema.Load(), SettingsStore.DefaultFolder);
    private readonly ChatHistory _history = new(SettingsStore.DefaultFolder);
    private readonly DeckStore _deckStore = new(SettingsStore.DefaultFolder);
    private readonly ClipHistory _clips = new(SettingsStore.DefaultFolder);
    private readonly RecentApps _recentApps = new(SettingsStore.DefaultFolder);
    private DeckWindow? _deck;
    private readonly DispatcherTimer _apply;
    private Forms.NotifyIcon? _tray;
    private SettingsWindow? _window;
    private Updater? _updater;
    private ExplorerSizes? _explorer;
    private PackUpdater? _packUpdater;

    public NyatchApp()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ThemeMode = ThemeMode.Dark;
        // Several changes in a row (typing a number) reach the island once.
        _apply = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _apply.Tick += (_, _) =>
        {
            _apply.Stop();
            Save();
            Island.Reload();
            _deck?.Apply();
            _ = _explorer?.ApplyAsync();
        };
    }

    /// <summary>Write the settings for the island, with what the app works out for it.</summary>
    private void Save()
    {
        // The assistants answer in the second notch's chat pages now, not in the island's search box.
        var packs = new CharacterPacks();
        var pack = _settings.Get("Characters.Pack");
        var derived = new Dictionary<string, string>
        {
            ["Assistant.Ready"] = "false",
            // Changes when the character pack's files do, so the notches load its pictures again.
            ["Characters.Stamp"] = packs.Stamp(pack),
        };
        // The pack's colour, over the accent the user chose, while the pack is in use (written after the
        // settings, so it is what the island reads; the user's own choice is kept as it is).
        if (PackAccent(_settings, packs) is { } colour)
        {
            derived["Themes.AccentColorMode"] = "custom";
            derived["Themes.CustomAccentHex"] = colour;
        }
        // The theme's font, the same way: over the font the user chose, while the theme is in use.
        if (ThemeFonts.Ready(_settings, packs) is { } font) derived["Themes.FontFamily"] = font;
        // The second notch and the LLM panel, turned off but with their hotkeys on: hidden until a hotkey opens them.
        NotchUse.Derive(_settings, derived);
        _settings.Save(derived);
        SignInLaunch.Apply(_settings.Get("General.LaunchAtStartup") == "true");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Feedback.Install(this);
        PackFiles.Register();
        // The island reads its settings from the file, so it is written first.
        SignInLaunch.TakeInstallerChoices(_settings);
        Save();
        var started = Island.Start();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Check for updates", null, async (_, _) =>
        {
            ShowSettings("Updates");
            if (_updater is not null) await _updater.CheckAsync(manual: true);
        });
        menu.Items.Add("Report a problem…", null, (_, _) => Feedback.ReportProblem());
        menu.Items.Add("Send feedback…", null, (_, _) => Feedback.SendFeedback());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Nyatch", null, (_, _) => Shutdown());
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "Nyatch",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, click) =>
        {
            if (click.Button == Forms.MouseButtons.Left) ShowSettings();
        };

        // The second pill, with its own notices for timers.
        _deck = new DeckWindow(_settings, _deckStore, _clips, _recentApps, _history, (title, text) => _tray?.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.None));
        _deck.SettingsChanged = () =>
        {
            _apply.Stop();
            _apply.Start();
        };
        _deck.OpenSettings = ShowSettings;
        _deck.OpenSettingsAt = ShowSettings;
        _deck.Apply();

        // Updates from the cloud: checked a little after start, then every few hours.
        _updater = new Updater(_settings, (title, text) => _tray?.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None), Shutdown);
        _updater.Start();

        // Character packs on this PC: newer versions installed by themselves, when switched on.
        _packUpdater = new PackUpdater(_settings, () =>
        {
            _apply.Stop();
            _apply.Start();
            _window?.RefreshPage("characters");
        });
        _packUpdater.Start();

        // File Explorer folder sizes, when switched on.
        _explorer = new ExplorerSizes(_settings);
        _ = _explorer.ApplyAsync();
        _tray.BalloonTipClicked += (_, _) => ShowSettings("Updates");

        if (!started)
        {
            MessageBox.Show("The island could not start. Nyatch will stay in the notification area; try quitting and opening it again.", "Nyatch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_apply.IsEnabled) Save();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _deck?.Close();
        _explorer?.StopAll();  // out of Explorer, so nothing of Nyatch stays in it
        Island.Stop();
        base.OnExit(e);
    }

    public void ShowSettings() => ShowSettings(null);

    /// <summary>The accent the character pack gives the notches now, or null (characters off, the switch off, or no colour in the pack).</summary>
    public static string? PackAccent(SettingsStore settings, CharacterPacks packs) =>
        settings.Get("Characters.Enabled") == "true" && settings.Get("Characters.UseAccent") == "true"
            ? packs.InstalledPack(settings.Get("Characters.Pack"))?.AccentColour
            : null;

    /// <summary>A character pack was just installed from a file: it is put to use and shown on the Characters page.</summary>
    public void PackArrived()
    {
        var packs = new CharacterPacks();
        var newest = packs.Installed().OrderByDescending(p => packs.Stamp(p.Id), StringComparer.Ordinal).FirstOrDefault();
        if (newest is not null) _settings.Set("Characters.Pack", newest.Id);
        _apply.Stop();
        _apply.Start();
        ShowSettings("characters");
        _window?.RefreshPage("characters");
    }

    /// <summary>Open the settings window, on the named page if one is given.</summary>
    public void ShowSettings(string? page)
    {
        if (_window is null)
        {
            _window = new SettingsWindow(_settings, _history, _updater!, changed: () =>
            {
                _apply.Stop();
                _apply.Start();
            });
            _window.Explorer = _explorer;
            // The File Explorer page's status follows the feature as it starts and stops.
            void ExplorerChanged() => _window?.Dispatcher.BeginInvoke(() => _window?.RefreshPage("File Explorer"));
            if (_explorer is not null) _explorer.Changed += ExplorerChanged;
            _window.Closed += (_, _) =>
            {
                if (_explorer is not null) _explorer.Changed -= ExplorerChanged;
                _window = null;
            };
        }
        if (page is not null) _window.OpenPage(page);
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nyatch.Core;

namespace Nyatch.App;

// Pages with something of their own to show: the chats (each service's key and model), saved
// conversations, updates, the File Explorer feature's status, and on General: backup and help.
internal sealed partial class SettingsWindow
{
    // ---------- Chats ----------

    /// <summary>The setting that holds a service's model.</summary>
    private static string ModelOf(string secret) =>
        Assistant.ModelSetting(Assistant.Chats.FirstOrDefault(e => Assistant.Secret(e) == secret, AssistantEngine.Gemini));

    /// <summary>Each service in a card of its own: its key (connect, replace, disconnect) and its model. Then the conversations.</summary>
    private void ShowChats()
    {
        Head(_page, ModelsButton());
        // Connected services first, then the rest, each in its own card with its logo.
        var services = Secrets.Known.OrderByDescending(s => Secrets.Has(s.Id)).ToList();
        var connectedCount = services.Count(s => Secrets.Has(s.Id));
        SectionStrip("Services", connectedCount == 0 ? services.Count : connectedCount);
        Note(connectedCount == 0
            ? "Connect a key below and its chat comes into the LLM panel. Keys stay in Windows Credential Manager."
            : $"{connectedCount} connected. Keys stay in Windows Credential Manager.");
        if (_content.Children[^1] is TextBlock note) note.Margin = new Thickness(2, 0, 2, 12);
        foreach (var secret in services)
        {
            var rows = Rows();
            var connected = Secrets.Has(secret.Id);
            var chip = Chip(connected ? "Connected" : "Not connected", connected ? _accent : _faint, connected ? _accentTint : _high);
            chip.VerticalAlignment = VerticalAlignment.Top;
            var row = Row(rows, secret.Name, secret.What, chip, hover: false);
            // The service's logo beside its name (the same pictures the LLM panel shows).
            var engine = Assistant.Chats.FirstOrDefault(e => Assistant.Secret(e) == secret.Id, AssistantEngine.None);
            var logoFile = System.IO.Path.Combine(AppContext.BaseDirectory, "ai", Assistant.Id(engine) + ".png");
            if (WordsOf(row) is { } head && head.Children.Count > 0 && head.Children[0] is TextBlock title && System.IO.File.Exists(logoFile))
            {
                head.Children.RemoveAt(0);
                var named = new StackPanel { Orientation = Orientation.Horizontal };
                var logo = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                try
                {
                    logo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(logoFile));
                }
                catch (Exception error) when (error is IOException or NotSupportedException)
                {
                }
                RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
                named.Children.Add(new Border { Child = logo, Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)), CornerRadius = new CornerRadius(7), Padding = new Thickness(5), Margin = new Thickness(0, 0, 10, 0) });
                logo.Margin = new Thickness(0);
                title.VerticalAlignment = VerticalAlignment.Center;
                named.Children.Add(title);
                head.Children.Insert(0, named);
            }

            // The entry line under the row's words: the key, then its buttons.
            var entry = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var problem = new TextBlock { FontSize = 12.5, Foreground = _danger, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
            var box = new PasswordBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = connected ? "Paste a new key to replace the saved one" : secret.Placeholder };
            box.SetValue(AutomationName, secret.Label);
            var save = GhostButton(connected ? "Replace" : "Connect");
            save.Margin = new Thickness(8, 0, 0, 0);
            save.IsEnabled = false;
            box.PasswordChanged += (_, _) => save.IsEnabled = box.Password.Trim().Length > 0;
            void Store()
            {
                try
                {
                    Secrets.Set(secret.Id, box.Password);
                    Changed();
                    ShowPage(keepScroll: true);
                }
                catch (ArgumentException e)
                {
                    problem.Text = e.Message;
                    problem.Visibility = Visibility.Visible;
                }
            }
            save.Click += (_, _) => Store();
            box.KeyDown += (_, key) =>
            {
                if (key.Key == Key.Enter && save.IsEnabled) Store();
            };
            DockPanel.SetDock(save, Dock.Right);
            if (connected)
            {
                var remove = GhostButton("Disconnect");
                remove.Margin = new Thickness(8, 0, 0, 0);
                remove.Foreground = _danger;
                remove.Click += (_, _) =>
                {
                    if (MessageBox.Show(this, $"Remove the {secret.Name} key from this PC? Answers from {secret.Name} stop until a key is connected again.", "Nyatch", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                    Secrets.Delete(secret.Id);
                    Changed();
                    ShowPage(keepScroll: true);
                };
                DockPanel.SetDock(remove, Dock.Right);
                entry.Children.Add(remove);
            }
            entry.Children.Add(save);
            entry.Children.Add(box);
            if (WordsOf(row) is { } words)
            {
                Grid.SetColumnSpan(words, 2);
                words.Margin = new Thickness(0);
                words.Children.Add(new TextBlock { Text = secret.Help, FontSize = 12, Foreground = _faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
                words.Children.Add(entry);
                words.Children.Add(problem);
            }
            if (_settings.Schema.Find(ModelOf(secret.Id)) is { } model) SettingRow(rows, model, []);
        }

        foreach (var section in _page.Sections)
        {
            SectionStrip(section.Title, null);
            var talk = Rows();
            foreach (var name in section.Settings)
            {
                if (_settings.Schema.Find(name) is { } setting) SettingRow(talk, setting, section.Settings);
            }
            if (section.Title != "Conversations") continue;
            var kept = _history.All.Count;
            Row(talk, "Saved conversations", kept == 0 ? "None yet." : kept == 1 ? "One kept on this PC." : $"{kept} kept on this PC.", GoButton("Open Chat history", () => OpenPage("history")));
        }
    }

    // ---------- History ----------

    /// <summary>Saved conversations: read, search, delete, clear.</summary>
    private void ShowHistory()
    {
        var all = _history.All;
        Button? clear = null;
        if (all.Count > 0)
        {
            clear = GhostButton("Clear all");
            clear.Foreground = _danger;
            clear.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"Delete all {all.Count} saved conversations? They cannot be brought back.", "Nyatch", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                _history.Clear();
                ShowPage();
            };
        }
        PageStrip(_page.Title, "", clear is null ? [] : [clear]);
        if (all.Count == 0)
        {
            EmptyState(IconHistory, "No conversations yet", "Chats in the LLM panel are kept here.");
            return;
        }

        var filter = new TextBox { Text = _historyFilter };
        filter.SetValue(AutomationName, "Search conversations");
        var filterBox = Field(filter, IconSearch, "Search conversations");
        filterBox.Margin = new Thickness(0, 0, 0, 16);
        _content.Children.Add(filterBox);
        var listHost = new StackPanel();
        _content.Children.Add(listHost);
        void Draw()
        {
            listHost.Children.Clear();
            var chats = _history.Search(_historyFilter);
            // Built into the host so the search box keeps the keyboard while typing.
            SectionStrip(listHost, "Conversations", chats.Count);
            if (chats.Count == 0)
            {
                listHost.Children.Add(new TextBlock { Text = $"No conversation mentions “{_historyFilter.Trim()}”.", Foreground = _soft, Margin = new Thickness(2, 4, 0, 0) });
                return;
            }
            var rows = Rows(listHost);
            foreach (var chat in chats)
            {
                var when = DateTimeOffset.FromUnixTimeMilliseconds(chat.At).LocalDateTime;
                var questions = chat.Turns.Count(t => t.Role == "user");
                var delete = IconButton(IconDelete, "Delete this conversation", () =>
                {
                    _history.Remove(chat.Id);
                    ShowPage(keepScroll: true);
                }, _danger);
                var row = Row(rows, chat.Title, $"{Assistant.Name(Assistant.Parse(chat.Engine))} · {when:d MMM yyyy, HH:mm} · {questions} {(questions == 1 ? "question" : "questions")}", delete);
                // The whole conversation, which the notch may have cut short: each question in a bubble of its
                // own, each answer as it reads (its Markdown drawn, not shown), selectable and copyable.
                var talk = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                var assistant = Assistant.Name(Assistant.Parse(chat.Engine));
                foreach (var turn in chat.Turns)
                {
                    var at = turn.At > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(turn.At).LocalDateTime.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture) : "";
                    var label = new TextBlock { FontSize = 11.5, Foreground = _faint, Margin = new Thickness(2, 10, 0, 4) };
                    label.Inlines.Add(new System.Windows.Documents.Run(turn.Role == "user" ? "You" : assistant) { FontWeight = FontWeights.SemiBold, Foreground = turn.Role == "user" ? _soft : _accent });
                    if (at.Length > 0) label.Inlines.Add(new System.Windows.Documents.Run("  ·  " + at));
                    talk.Children.Add(label);
                    if (turn.Role == "user")
                    {
                        talk.Children.Add(new Border
                        {
                            Background = _accentTint,
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(12, 8, 12, 9),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Child = new TextBox { Text = turn.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Foreground = _text, FontWeight = FontWeights.SemiBold, FontSize = 13.5 },
                        });
                    }
                    else
                    {
                        var answer = DeckWindow.MarkdownView(turn.Text, _text, _soft, _accent, _high);
                        answer.Margin = new Thickness(2, 0, 0, 0);
                        talk.Children.Add(answer);
                    }
                }
                WordsOf(row)?.Children.Add(new Expander { Header = "Show the conversation", Content = talk, Margin = new Thickness(0, 6, 0, 0), Foreground = _soft });
            }
        }
        filter.TextChanged += (_, _) =>
        {
            _historyFilter = filter.Text;
            Draw();
        };
        Draw();
    }

    // ---------- Updates ----------

    private void ShowUpdates()
    {
        var u = _updater;
        var installable = u.Status is Updater.State.Available or Updater.State.Ready && Updater.IsInstalledCopy && u.Latest is not null;
        var busy = u.Status is Updater.State.Checking or Updater.State.Downloading or Updater.State.Installing;
        Button action;
        if (installable)
        {
            action = MainButton(u.Status == Updater.State.Ready ? "Install now" : $"Install {u.Latest!.Version}");
            action.Click += async (_, _) =>
            {
                if (u.Status == Updater.State.Ready) u.Install();
                else await u.DownloadAsync(installNow: true);
            };
        }
        else
        {
            action = MainButton("Check now");
            action.Click += async (_, _) => await u.CheckAsync(manual: true);
        }
        action.IsEnabled = !busy;
        Head(_page, action);

        SectionStrip("Version", null);
        var rows = Rows();
        Row(rows, "This version", Updater.IsInstalledCopy ? "" : "A build folder: updates are shown, not installed.",
            Chip(u.Current.ToString(), _text, _high, numbers: true), hover: false);
        var status = u.Message.Length > 0 ? u.Message : "Not checked yet.";
        UIElement? latest = u.Latest is { } r ? Chip(r.Version.ToString(), r.Version > u.Current ? _accent : _text, r.Version > u.Current ? _accentTint : _high, numbers: true) : null;
        // With a character on, and nothing newer out: her picture beside it.
        if (u.Latest is { } found && found.Version <= u.Current && !busy && HerPictureIfOn(40, "updates", "cool", "happy", "idle") is { } her)
        {
            var both = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            her.Margin = new Thickness(0, 0, 10, 0);
            both.Children.Add(her);
            if (latest is not null) both.Children.Add(latest);
            latest = both;
        }
        var statusRow = Row(rows, "Latest", status, latest, hover: false);
        if (u.Status == Updater.State.Downloading && WordsOf(statusRow) is { } words)
        {
            var bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = _high, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = _accent, HorizontalAlignment = HorizontalAlignment.Left };
            bar.Child = fill;
            bar.SizeChanged += (_, _) => fill.Width = bar.ActualWidth * Math.Clamp(u.Progress, 0, 1);
            words.Children.Add(bar);
        }
        if (u.LastChecked is { } when)
        {
            Row(rows, "Last checked", when.ToString("d MMM, HH:mm", System.Globalization.CultureInfo.CurrentCulture), null, hover: false);
        }
        if (u.Latest is { } release)
        {
            var open = GhostButton("Release page");
            open.Click += (_, _) => Native.Open(release.Page.ToString());
            Row(rows, $"Nyatch {release.Version}", release.Notes.Trim().Length > 0 ? FirstLines(release.Notes, 4) : "No notes for this release.", open, hover: false);
        }
        Sections(_page);
    }

    private static string FirstLines(string text, int lines)
    {
        var parts = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", parts.Take(lines)) + (parts.Length > lines ? "\n…" : "");
    }

    // ---------- File Explorer ----------

    /// <summary>The File Explorer feature (the app sets it), for its status on that page.</summary>
    public ExplorerSizes? Explorer { get; set; }

    /// <summary>The File Explorer page's status: working, off, or why not.</summary>
    private void ShowExplorerStatus()
    {
        if (Explorer is not { } explorer) return;
        var on = _settings.Get("Explorer.Enabled") == "true";
        string title, line;
        Brush colour;
        if (!on)
        {
            (title, line, colour) = ("Off", "Turn it on below to see folder sizes in File Explorer.", _faint);
        }
        else if (explorer.Problem.Length > 0)
        {
            (title, line, colour) = ("Not working", explorer.Problem, _danger);
        }
        else if (explorer.RunningIn > 0)
        {
            var windows = explorer.RunningIn == 1 ? "File Explorer" : $"{explorer.RunningIn} File Explorer processes";
            (title, line, colour) = ("Working", $"Running in {windows}.", _accent);
        }
        else
        {
            (title, line, colour) = ("Starting", "Getting ready. The first time, Windows' symbols are downloaded, which can take a minute or two.", _faint);
        }
        SectionStrip("Status", null);
        var rows = Rows();
        var again = GhostButton("Check again");
        again.Click += async (_, _) =>
        {
            again.IsEnabled = false;
            await explorer.ApplyAsync();
            ShowPage(keepScroll: true);
        };
        var row = Row(rows, title, line, on ? again : null, hover: false);
        if (WordsOf(row) is { } words && words.Children.Count > 0 && words.Children[0] is TextBlock heading) heading.Foreground = colour;
    }

    // ---------- General: backup and help ----------

    /// <summary>The General page's Backup section: save the settings to a file, bring them back, or start over.</summary>
    private void ShowBackup()
    {
        SectionStrip("Backup", null);
        var rows = Rows();
        var changed = _settings.Changed.Count;

        var export = GhostButton("Save a backup");
        export.Click += (_, _) =>
        {
            var ask = new Microsoft.Win32.SaveFileDialog { Title = "Save a backup of the settings", FileName = $"nyatch-settings-{DateTime.Now:yyyy-MM-dd}.json", Filter = "Nyatch settings (*.json)|*.json", DefaultExt = ".json" };
            if (ask.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(ask.FileName, _settings.Export());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "The backup could not be saved there: " + e.Message, "Nyatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        Row(rows, "Back up", changed == 0 ? "Nothing changed yet." : changed == 1 ? "1 changed setting." : $"{changed} changed settings.", export, hover: false);

        var import = GhostButton("Restore");
        import.Click += (_, _) =>
        {
            var ask = new Microsoft.Win32.OpenFileDialog { Title = "Restore the settings from a backup", Filter = "Nyatch settings (*.json)|*.json|All files|*.*" };
            if (ask.ShowDialog(this) != true) return;
            if (MessageBox.Show(this, "Replace the settings with those of the backup? Settings the backup does not name go back to their defaults.", "Nyatch", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                _settings.Import(File.ReadAllText(ask.FileName));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                MessageBox.Show(this, "That file is not a backup of Nyatch's settings.", "Nyatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Changed(theme: true);
            ShowPage();
        };
        Row(rows, "Restore", "", import, hover: false);

        var reset = GhostButton("Reset");
        reset.Foreground = _danger;
        reset.IsEnabled = changed > 0;
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Put every setting back to its default? Keys, conversations, notes and files stay as they are.", "Nyatch", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            _settings.ResetAll();
            Changed(theme: true);
            ShowPage();
        };
        Row(rows, "Reset everything", "Keys, notes and files stay.", reset, hover: false);
    }

    /// <summary>The General page's Help section: report a problem, send feedback.</summary>
    private void ShowHelp()
    {
        SectionStrip("Help", null);
        var rows = Rows();
        var report = GhostButton("Report");
        report.Click += (_, _) => Feedback.ReportProblem();
        var crash = Feedback.LastCrash();
        Row(rows, "Report a problem",
            crash is { } c ? $"Includes the crash on {c.When:d MMM}." : "",
            report, hover: false);
        var write = GhostButton("Email");
        write.Click += (_, _) => Feedback.SendFeedback();
        Row(rows, "Send feedback", "", write, hover: false);
    }
}

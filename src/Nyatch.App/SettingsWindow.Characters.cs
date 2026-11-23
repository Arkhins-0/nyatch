using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nyatch.Core;

namespace Nyatch.App;

// The Characters page: the switch, and the packs. Packs on this PC can be used or deleted; packs in the
// list (fetched while the page is open) show their cover and details and are downloaded on a click. A
// .nyatch file can also be installed from disk.
internal sealed partial class SettingsWindow
{
    private readonly CharacterPacks _packs = new();
    private IReadOnlyList<CharacterPack>? _packList;      // the packs to download, once fetched
    private bool _packListAsked, _packListFailed;
    private string _packNote = "";                         // what went wrong last, under the list
    private string _packConfirm = "";                      // the pack whose Delete was clicked once
    private readonly Dictionary<string, double> _packBusy = [];  // downloading: id -> how far
    private readonly Dictionary<string, CancellationTokenSource> _packCancel = [];

    private void ShowCharacters()
    {
        Head(_page);
        var inUse = _settings.Get("Characters.Pack");
        if (_packs.IsInstalled(inUse)) Picture(CharacterSample);
        Sections(_page);

        if (!_packListAsked) _ = FetchPackList();
        var installed = _packs.Installed();
        var listed = _packList ?? _packs.LastAvailable();

        // The theme editor: making a theme, or changing one, in a window of its own.
        var editor = MainButton("Theme editor");
        editor.Click += (_, _) => Nyatch.ThemeEditor.ThemeEditor.Open();
        var fromFile = GhostButton("Install from a file");
        fromFile.Margin = new Thickness(6, 0, 0, 0);
        fromFile.Click += (_, _) => InstallPackFromFile();
        var again = IconButton("", "Fetch the list again", () =>
        {
            _packListAsked = false;
            _packNote = "";
            ShowPage(keepScroll: true);
        });
        again.Margin = new Thickness(6, 0, 0, 0);
        SectionStrip("Themes", null, editor, fromFile, again);
        var rows = Rows();
        var ids = installed.Select(p => p.Id).Concat(listed.Select(p => p.Id)).Distinct().ToList();
        foreach (var id in ids)
        {
            var here = installed.FirstOrDefault(p => p.Id == id);
            var there = listed.FirstOrDefault(p => p.Id == id);
            PackRow(rows, here, there, inUse);
        }
        if (ids.Count == 0)
        {
            Row(rows, _packListAsked && _packList is null && !_packListFailed ? "Fetching the list…" : "No themes yet",
                "Install one from a .nyatch file, or fetch the list again.", null, hover: false);
        }
        var note = _packNote.Length > 0 ? _packNote : _packListFailed ? "The theme list could not be reached. Themes on this PC still work." : "";
        if (note.Length > 0) Note(note);
    }

    /// <summary>One pack: its cover, what it is, and what can be done with it here.</summary>
    private void PackRow(StackPanel rows, CharacterPack? here, CharacterPack? there, string inUse)
    {
        var pack = here ?? there!;
        var newer = here is not null && there is not null && Newer(there.Version, here.Version);
        var facts = new List<string>();
        if (pack.Author.Length > 0) facts.Add("By " + pack.Author);
        if ((there ?? pack).Size > 0) facts.Add($"{(there ?? pack).Size / 1024.0 / 1024.0:0.0} MB");
        facts.Add(pack.Version + (newer ? $" (version {there!.Version} is out)" : ""));
        if (pack.Pictures > 0) facts.Add($"{pack.Pictures} pictures");
        var line = string.Join(" · ", facts);
        if (pack.Description.Length > 0) line = pack.Description + "\n" + line;

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Add(FrameworkElement control)
        {
            if (right.Children.Count > 0) control.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(control);
        }
        if (_packBusy.TryGetValue(pack.Id, out var done))
        {
            var bar = new Border { Width = 120, Height = 4, CornerRadius = new CornerRadius(2), Background = _high, VerticalAlignment = VerticalAlignment.Center };
            bar.Child = new Border { Width = 120 * Math.Clamp(done, 0, 1), Height = 4, CornerRadius = new CornerRadius(2), Background = _accent, HorizontalAlignment = HorizontalAlignment.Left };
            Add(bar);
            var stop = GhostButton("Cancel");
            stop.Click += (_, _) => _packCancel.GetValueOrDefault(pack.Id)?.Cancel();
            Add(stop);
        }
        else if (here is null)
        {
            var get = MainButton("Download");
            get.Click += (_, _) => _ = DownloadPack(there!);
            Add(get);
        }
        else if (_packConfirm == pack.Id)
        {
            Add(new TextBlock { Text = "Delete it from this PC?", FontSize = 13, Foreground = _soft, VerticalAlignment = VerticalAlignment.Center });
            var yes = GhostButton("Delete");
            yes.Foreground = _danger;
            yes.Click += (_, _) => DeletePack(pack.Id);
            Add(yes);
            var no = GhostButton("Keep");
            no.Click += (_, _) =>
            {
                _packConfirm = "";
                ShowPage(keepScroll: true);
            };
            Add(no);
        }
        else
        {
            if (newer)
            {
                var update = MainButton("Update");
                update.Click += (_, _) => _ = DownloadPack(there!);
                Add(update);
            }
            if (pack.Id != inUse)
            {
                var use = GhostButton("Use");
                use.Click += (_, _) => UsePack(pack.Id);
                Add(use);
            }
            var delete = GhostButton("Delete");
            delete.Click += (_, _) =>
            {
                _packConfirm = pack.Id;
                ShowPage(keepScroll: true);
            };
            Add(delete);
        }

        var row = Row(rows, pack.Name, line, right, hover: false);
        if (row.Child is not Grid grid || WordsOf(row) is not { } words) return;
        words.Margin = new Thickness(64, 0, 20, 0);
        if (words.Children[0] is TextBlock name)
        {
            // The name, the pack's colour beside it, and "In use".
            words.Children.RemoveAt(0);
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(name);
            if (pack.AccentColour is { } colour)
            {
                var dot = new Border
                {
                    Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)), BorderBrush = _line, BorderThickness = new Thickness(1),
                    ToolTip = $"Its colour: {colour}",
                };
                title.Children.Add(dot);
            }
            if (here is not null && pack.Id == inUse) title.Children.Add(Chip("In use", _accent, _accentTint, margin: new Thickness(8, 1, 0, 0)));
            words.Children.Insert(0, title);
        }
        var cover = new Image { Width = 48, Height = 48, Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(cover, BitmapScalingMode.HighQuality);
        var frame = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(6), Background = _high, Child = cover,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Clip = new RectangleGeometry(new Rect(0, 0, 48, 48), 6, 6),
        };
        grid.Children.Add(frame);
        if (here is not null) cover.Source = LoadPicture(_packs.PreviewOf(pack.Id) ?? _packs.PictureOf(pack.Id, "idle", "happy"), 96);
        else _ = ShowCover(cover, there!);
    }

    private async System.Threading.Tasks.Task ShowCover(Image cover, CharacterPack pack)
    {
        var file = await _packs.PreviewAsync(pack);
        if (file is not null) cover.Source = LoadPicture(file, 96);
    }

    private async System.Threading.Tasks.Task FetchPackList()
    {
        _packListAsked = true;
        _packListFailed = false;
        try
        {
            _packList = await _packs.AvailableAsync();
        }
        catch (Exception error) when (error is IOException or System.Net.Http.HttpRequestException or InvalidDataException or System.Text.Json.JsonException or System.Threading.Tasks.TaskCanceledException)
        {
            _packListFailed = true;
        }
        if (_page.Id == "characters" && _search.Text.Trim().Length == 0) ShowPage(keepScroll: true);
    }

    private async System.Threading.Tasks.Task DownloadPack(CharacterPack pack)
    {
        if (_packBusy.ContainsKey(pack.Id)) return;
        using var cancel = new CancellationTokenSource();
        _packBusy[pack.Id] = 0;
        _packCancel[pack.Id] = cancel;
        _packNote = "";
        void Redraw()
        {
            if (_page.Id == "characters" && _search.Text.Trim().Length == 0) ShowPage(keepScroll: true);
        }
        Redraw();
        var last = 0.0;
        var progress = new Progress<double>(done =>
        {
            _packBusy[pack.Id] = done;
            if (done - last < 0.04 && done < 1) return;
            last = done;
            Redraw();
        });
        try
        {
            await _packs.DownloadAsync(pack, progress, cancel.Token);
            _packBusy.Remove(pack.Id);
            // The first pack is put to use at once.
            if (!_packs.IsInstalled(_settings.Get("Characters.Pack"))) _settings.Set("Characters.Pack", pack.Id);
            Changed();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is IOException or System.Net.Http.HttpRequestException or InvalidDataException or UnauthorizedAccessException)
        {
            _packNote = $"{pack.Name} was not installed: {error.Message}";
        }
        _packBusy.Remove(pack.Id);
        _packCancel.Remove(pack.Id);
        Redraw();
    }

    private void InstallPackFromFile()
    {
        var pick = new Microsoft.Win32.OpenFileDialog { Title = "Install a theme", Filter = "Nyatch themes (*.nyatch)|*.nyatch" };
        if (pick.ShowDialog(this) != true) return;
        // What it is first, on its own colour; installed (and put to use) only when that says so.
        if (!PackInstallWindow.Ask(pick.FileName)) return;
        InstallPackFile(pick.FileName, use: true);
    }

    /// <summary>Install a .nyatch file and show it on the Characters page.</summary>
    internal void InstallPackFile(string file, bool use = false)
    {
        try
        {
            var pack = _packs.InstallFile(file);
            _packNote = "";
            if (use || !_packs.IsInstalled(_settings.Get("Characters.Pack")) || _settings.Get("Characters.Pack") == pack.Id) _settings.Set("Characters.Pack", pack.Id);
            Changed();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _packNote = $"{Path.GetFileName(file)} was not installed: {error.Message}";
        }
        if (_page.Id == "characters") ShowPage(keepScroll: true);
    }

    private void UsePack(string id)
    {
        if (!_settings.Set("Characters.Pack", id)) return;
        Changed();
        ShowPage(keepScroll: true);
    }

    private void DeletePack(string id)
    {
        _packConfirm = "";
        try
        {
            _packs.Delete(id);
            if (_settings.Get("Characters.Pack") == id) _settings.Set("Characters.Pack", _packs.Installed().FirstOrDefault()?.Id ?? "");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _packNote = $"The theme could not be deleted: {error.Message}";
        }
        Changed();
        ShowPage(keepScroll: true);
    }

    private static bool Newer(string version, string than) => PackUpdater.IsNewer(version, than);

    /// <summary>The pack in use, as the time page shows it through the day.</summary>
    private FrameworkElement CharacterSample()
    {
        var on = _settings.Get("Characters.Enabled") == "true";
        var moments = new (string Caption, string[] Names)[]
        {
            ("Morning", ["time-morning", "idle"]),
            ("Day", ["time-day", "idle"]),
            ("Evening", ["time-evening", "idle"]),
            ("Night", ["time-night", "sleeping", "idle"]),
            ("On the hour", ["time-hour", "happy"]),
        };
        var items = new (string, FrameworkElement)[moments.Length];
        for (var i = 0; i < moments.Length; i++)
        {
            FrameworkElement picture = HerPicture(72, moments[i].Names) is { } her
                ? new Border { Child = her, Opacity = on ? 1 : 0.45 }
                : new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) };
            items[i] = (moments[i].Caption, picture);
        }
        return Stage(items);
    }

    /// <summary>
    /// The character's picture for the first of the names her pack has, at a size (a GIF shows its first
    /// frame). Null when characters are off or the pack has none of them.
    /// </summary>
    private Image? HerPicture(double size, params string[] names)
    {
        var pack = _settings.Get("Characters.Pack");
        if (LoadPicture(_packs.PictureOf(pack, names), (int)(size * 2)) is not { } source) return null;
        var image = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        // A sticker with its own background gets round corners, as the notch draws it.
        var radius = size * 0.16;
        image.Loaded += (_, _) => image.Clip = new RectangleGeometry(new Rect(0, 0, image.ActualWidth, image.ActualHeight), radius, radius);
        return image;
    }

    /// <summary>Her picture, only while characters are switched on.</summary>
    private Image? HerPictureIfOn(double size, params string[] names) =>
        _settings.Get("Characters.Enabled") == "true" ? HerPicture(size, names) : null;

    private static BitmapSource? LoadPicture(string? path, int pixels)
    {
        if (path is null) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = pixels;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;  // a picture that cannot be read is treated as missing
        }
    }
}

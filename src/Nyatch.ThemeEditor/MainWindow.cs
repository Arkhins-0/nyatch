using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Nyatch.Core;

namespace Nyatch.ThemeEditor;

/// <summary>
/// Nyatch Packer: make a character pack. The pack's details and cover on the left, every picture slot in
/// the middle (what it shows now, and where), and the chosen slot on the right: its picture as Nyatch
/// draws it, in its place, playing. Pictures are added with a button or by dropping them. "Try in
/// Nyatch" installs the pack and puts it to use; "Make .nyatch file" makes the file to share.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private PackProject? _pack;
    private CharacterSlot? _slot;
    private string _filter = "";
    private int _show;  // index in Shows
    private Dictionary<string, string> _copies = [];
    private static readonly string[] Shows = ["All slots", "Own pictures", "Copies", "Borrowed", "Empty"];

    private enum State { Own, Copy, Borrowed, Empty }

    private State StateOf(PackProject pack, CharacterSlot slot) =>
        _copies.ContainsKey(slot.Name) ? State.Copy
        : pack.Has(slot.Name) ? State.Own
        : pack.Shown(slot) is not null ? State.Borrowed
        : State.Empty;
    private readonly Dictionary<string, Picture?> _pictures = [];  // by path, loaded once

    private readonly TextBlock _where = new();
    private readonly TextBlock _status = new();
    private readonly StackPanel _details = new();
    private readonly StackPanel _slots = new();
    private readonly StackPanel _preview = new();
    private readonly ScrollViewer _slotScroll = new();

    private static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public MainWindow()
    {
        // Windows 11's own look for this window (the app's other windows have their own).
        ThemeMode = ThemeMode.System;
        Title = "Nyatch Theme Editor";
        Width = 1360;
        Height = 880;
        MinWidth = 1100;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;
        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/nyatch.ico"));
        }
        catch (Exception error) when (error is IOException or NotSupportedException or UriFormatException)
        {
        }

        var root = new DockPanel();
        Content = root;

        // ----- Commands -----
        var bar = new DockPanel { Margin = new Thickness(16, 12, 16, 12), LastChildFill = true };
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        commands.Children.Add(Command("", "New", NewPack));
        commands.Children.Add(Command("", "Open", OpenPack));
        commands.Children.Add(Command("", "Fill empty slots", FillAll));
        commands.Children.Add(Command("", "Shrink all", ShrinkAll));
        commands.Children.Add(new Border { Width = 1, Margin = new Thickness(10, 4, 10, 4), Background = Brush("CardStrokeColorDefaultBrush") });
        var tryIt = Command("", "Try in Nyatch", TryInNyatch);
        tryIt.Style = (Style)FindResource("AccentButtonStyle");
        commands.Children.Add(tryIt);
        commands.Children.Add(Command("", "Export", MakeFile));
        commands.Children.Add(Command("", "Add to theme list", MakeIntoList));
        DockPanel.SetDock(commands, Dock.Left);
        bar.Children.Add(commands);
        _where.VerticalAlignment = VerticalAlignment.Center;
        _where.TextAlignment = TextAlignment.Right;
        _where.TextTrimming = TextTrimming.CharacterEllipsis;
        _where.Margin = new Thickness(16, 0, 0, 0);
        _where.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        bar.Children.Add(_where);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        var statusBar = new Border { Padding = new Thickness(16, 8, 16, 10), Child = _status };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);

        // ----- Three columns -----
        var columns = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(470) });
        root.Children.Add(columns);

        var left = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 12) };
        columns.Children.Add(left);

        var middle = new DockPanel { Margin = new Thickness(8, 0, 8, 0) };
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        search.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Find a slot");
        search.TextChanged += (_, _) =>
        {
            _filter = search.Text.Trim();
            DrawSlots();
        };
        var searchBox = new Grid();
        searchBox.Children.Add(search);
        var hint = new TextBlock { Text = "Search slots", IsHitTestVisible = false, Margin = new Thickness(12, 7, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        search.TextChanged += (_, _) => hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchBox.Children.Add(hint);
        var show = new ComboBox { Width = 150, Margin = new Thickness(8, 0, 0, 10), ItemsSource = Shows, SelectedIndex = 0 };
        show.SelectionChanged += (_, _) =>
        {
            _show = show.SelectedIndex;
            DrawSlots();
        };
        var searchLine = new DockPanel();
        DockPanel.SetDock(show, Dock.Right);
        searchLine.Children.Add(show);
        searchLine.Children.Add(searchBox);
        DockPanel.SetDock(searchLine, Dock.Top);
        middle.Children.Add(searchLine);
        DockPanel.SetDock(_selectionBar, Dock.Top);
        middle.Children.Add(_selectionBar);
        _slotScroll.Content = _slots;
        _slotScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        middle.Children.Add(_slotScroll);
        Grid.SetColumn(middle, 1);
        columns.Children.Add(middle);

        var right = new ScrollViewer { Content = _preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12, 0, 14, 12) };
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);

        // Files dropped anywhere go to the chosen slot.
        AllowDrop = true;
        Drop += (_, drop) =>
        {
            if (_slot is not null && Dropped(drop) is { } file) Put(_slot.Name, file);
        };

        ShowAll();
    }

    // ---------- Building blocks ----------

    private Brush Brush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private static Button Command(string glyph, string text, Action click)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
        line.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = line, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 6, 12, 6) };
        button.Click += (_, _) => click();
        return button;
    }

    private static Border Card(UIElement child, Thickness? padding = null)
    {
        var card = new Border { Child = child, Padding = padding ?? new Thickness(16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return card;
    }

    private static TextBlock Heading(string text, double size = 14) =>
        new() { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };

    private static TextBlock Quiet(string text, double size = 12.5)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return block;
    }

    private Picture? Load(string? path)
    {
        if (path is null) return null;
        var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        if (!_pictures.TryGetValue(key, out var picture)) _pictures[key] = picture = Picture.Load(path);
        return picture;
    }

    private Color Accent()
    {
        if (_pack?.Manifest.AccentColour is { } hex) return (Color)ColorConverter.ConvertFromString(hex);
        return (TryFindResource("SystemAccentColor") as Color?) ?? Color.FromRgb(0x60, 0xCD, 0xFF);
    }

    private static string? Dropped(DragEventArgs drop) =>
        drop.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files ? files[0] : null;

    // ---------- Showing ----------

    private void ShowAll()
    {
        DrawDetails();
        DrawSlots();
        DrawPreview();
        _where.Text = _pack?.Folder ?? "";
        Title = _pack is null ? "Nyatch Theme Editor" : $"{_pack.Manifest.Name} · Nyatch Theme Editor";
    }

    private void Say(string text) => _status.Text = text;

    private void DrawDetails()
    {
        _details.Children.Clear();
        if (_pack is not { } pack)
        {
            var start = new StackPanel();
            start.Children.Add(Heading("No theme open", 16));
            start.Children.Add(Quiet("Choose New theme or Open theme to start."));
            _details.Children.Add(Card(start));
            return;
        }

        // ----- About the pack -----
        var about = new StackPanel();
        about.Children.Add(Heading("Theme", 16));
        void Field(string label, string value, Action<string> changed, bool wide = false)
        {
            about.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 4) });
            var box = new TextBox { Text = value, AcceptsReturn = wide, TextWrapping = wide ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = wide ? 64 : 0 };
            box.LostKeyboardFocus += (_, _) =>
            {
                if (box.Text == value) return;
                value = box.Text;
                changed(box.Text.Trim());
                Changed();
            };
            about.Children.Add(box);
        }
        Field("Name", pack.Manifest.Name, v => pack.Manifest = pack.Manifest with { Name = v });
        Field("Id", pack.Manifest.Id, v => pack.Manifest = pack.Manifest with { Id = v.ToLowerInvariant() });
        Field("Version", pack.Manifest.Version, v => pack.Manifest = pack.Manifest with { Version = v.Length == 0 ? "1.0" : v });
        Field("Made by", pack.Manifest.Author, v => pack.Manifest = pack.Manifest with { Author = v });
        Field("Description", pack.Manifest.Description, v => pack.Manifest = pack.Manifest with { Description = v }, wide: true);
        _details.Children.Add(Card(about));

        // ----- Its colour -----
        var colour = new StackPanel();
        colour.Children.Add(Heading("Colour"));
        colour.Children.Add(Quiet("Tints Nyatch while the theme is in use. Optional."));
        var line = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var swatch = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) };
        swatch.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        swatch.Background = pack.Manifest.AccentColour is { } hex ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) : Brushes.Transparent;
        DockPanel.SetDock(swatch, Dock.Left);
        line.Children.Add(swatch);
        var colourBox = new TextBox { Text = pack.Manifest.Accent, VerticalContentAlignment = VerticalAlignment.Center };
        colourBox.LostKeyboardFocus += (_, _) =>
        {
            var text = colourBox.Text.Trim();
            if (text.Length > 0 && !text.StartsWith('#')) text = "#" + text;
            if (text == pack.Manifest.Accent) return;
            pack.Manifest = pack.Manifest with { Accent = text.ToUpperInvariant() };
            Changed();
        };
        line.Children.Add(colourBox);
        colour.Children.Add(line);
        _details.Children.Add(Card(colour));

        // ----- The cover -----
        var cover = new StackPanel();
        cover.Children.Add(Heading("Cover"));
        cover.Children.Add(Quiet("Shown in the theme list. Square, 512 px."));
        var coverPicture = new AnimatedImage { Width = 128, Height = 128, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 10) };
        coverPicture.Picture = Load(pack.PictureOf("preview"));
        var coverFrame = new Border { Child = coverPicture, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(8), AllowDrop = true };
        coverFrame.Drop += (_, drop) =>
        {
            drop.Handled = true;
            if (Dropped(drop) is { } file) Put("preview", file);
        };
        cover.Children.Add(coverFrame);
        var coverButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var chooseCover = new Button { Content = "Choose…", Margin = new Thickness(0, 0, 6, 0) };
        chooseCover.Click += (_, _) => Choose("preview");
        coverButtons.Children.Add(chooseCover);
        var makeCover = new Button { Content = "Make from selected", Margin = new Thickness(0, 0, 6, 0) };
        makeCover.Click += (_, _) => MakeCover();
        coverButtons.Children.Add(makeCover);
        cover.Children.Add(coverButtons);
        var tint = new CheckBox { Content = "Tint with the theme colour", IsChecked = _coverTint, Margin = new Thickness(0, 10, 0, 0) };
        tint.Click += (_, _) => _coverTint = tint.IsChecked == true;
        cover.Children.Add(tint);
        cover.Children.Add(new Border { Height = 6 });
        cover.Children.Add(Quiet("Tick up to 4 slots in the list for the cover; none ticked uses idle.", 12));
        _details.Children.Add(Card(cover));

        // ----- The font -----
        var font = new StackPanel();
        font.Children.Add(Heading("Font"));
        font.Children.Add(Quiet("The notches write in it while the theme is in use. Optional."));
        if (pack.FontPath is { } fontFile && pack.Manifest.Font.Length > 0)
        {
            // A line in the font itself.
            var sample = new TextBlock { Text = "Monday 23 · 10:42 · Sunny", FontSize = 20, Margin = new Thickness(0, 10, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            try
            {
                sample.FontFamily = new FontFamily(new Uri(System.IO.Path.GetDirectoryName(fontFile)! + System.IO.Path.DirectorySeparatorChar), "./#" + pack.Manifest.Font);
            }
            catch (Exception error) when (error is ArgumentException or UriFormatException)
            {
            }
            font.Children.Add(sample);
            font.Children.Add(Quiet($"{pack.Manifest.Font} · {new FileInfo(fontFile).Length / 1024} KB", 12));
        }
        var fontButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var chooseFont = new Button { Content = pack.FontPath is null ? "Choose a font…" : "Replace…", Margin = new Thickness(0, 0, 6, 0) };
        chooseFont.Click += (_, _) =>
        {
            var pick = new OpenFileDialog { Title = "A font for the theme", Filter = "Fonts (TrueType or OpenType)|*.ttf;*.otf" };
            if (pick.ShowDialog(this) != true) return;
            try
            {
                Say($"The font is {pack.SetFont(pick.FileName)}.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Say(error.Message);
            }
            Changed();
        };
        fontButtons.Children.Add(chooseFont);
        if (pack.FontPath is not null)
        {
            var removeFont = new Button { Content = "Remove" };
            removeFont.Click += (_, _) =>
            {
                pack.RemoveFont();
                Say("The theme has no font of its own now.");
                Changed();
            };
            fontButtons.Children.Add(removeFont);
        }
        font.Children.Add(fontButtons);
        _details.Children.Add(Card(font));

        // ----- Checks -----
        var checks = new StackPanel();
        var found = pack.Check();
        var problems = found.Count(f => f.Problem);
        checks.Children.Add(Heading(problems > 0 ? $"{problems} to fix" : "Ready to publish", 16));
        var copiesNow = pack.Copies();
        int own = 0, copied = 0, borrowed = 0;
        foreach (var s in CharacterSlots.All)
        {
            if (copiesNow.ContainsKey(s.Name)) copied++;
            else if (pack.Has(s.Name)) own++;
            else if (pack.Shown(s) is not null) borrowed++;
        }
        checks.Children.Add(Quiet($"{own} own pictures · {copied} copies · {borrowed} borrowed · {CharacterSlots.All.Count} slots"));
        checks.Children.Add(new Border { Height = 8 });
        foreach (var finding in found.Take(8))
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4), Cursor = finding.Slot is null ? null : Cursors.Hand, Background = Brushes.Transparent };
            var mark = new TextBlock { Text = finding.Problem ? "" : "", FontFamily = Glyphs, FontSize = 14, Margin = new Thickness(0, 2, 8, 0), Foreground = finding.Problem ? new SolidColorBrush(Color.FromRgb(0xE0, 0x56, 0x4F)) : Brush("TextFillColorSecondaryBrush") };
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            row.Children.Add(new TextBlock { Text = finding.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            if (finding.Slot is { } name && CharacterSlots.Find(name) is { } target) row.MouseLeftButtonUp += (_, _) => Pick(target);
            checks.Children.Add(row);
        }
        if (found.Count > 8) checks.Children.Add(Quiet($"And {found.Count - 8} more."));
        _details.Children.Insert(0, Card(checks));
    }

    private void DrawSlots()
    {
        _slots.Children.Clear();
        if (_pack is not { } pack) return;
        _copies = pack.Copies();
        var filter = _filter.ToLowerInvariant();
        foreach (var place in CharacterSlots.Places)
        {
            var all = CharacterSlots.All.Where(s => s.Place == place).ToList();
            var slots = all
                .Where(s => filter.Length == 0 || s.Name.Contains(filter) || s.When.ToLowerInvariant().Contains(filter) || place.ToLowerInvariant().Contains(filter))
                .Where(s => _show == 0 || (int)StateOf(pack, s) == _show - 1)
                .ToList();
            if (slots.Count == 0) continue;
            // A place with several pages (the second notch) is shown page by page.
            foreach (var page in all.Select(CharacterSlots.PageOf).Distinct())
            {
                var inPage = all.Where(s => CharacterSlots.PageOf(s) == page).ToList();
                var shownHere = slots.Where(s => CharacterSlots.PageOf(s) == page).ToList();
                if (shownHere.Count == 0) continue;
                var own = inPage.Count(s => StateOf(pack, s) == State.Own);
                var copies = inPage.Count(s => StateOf(pack, s) == State.Copy);
                var head = new DockPanel { Margin = new Thickness(2, _slots.Children.Count == 0 ? 0 : 14, 2, 6) };
                var count = Quiet(copies > 0 ? $"{own} own · {copies} copies · {inPage.Count}" : $"{own} own · {inPage.Count}", 12);
                DockPanel.SetDock(count, Dock.Right);
                head.Children.Add(count);
                var title = new TextBlock { FontWeight = FontWeights.SemiBold };
                title.Inlines.Add(new System.Windows.Documents.Run(place));
                if (page is not null) title.Inlines.Add(new System.Windows.Documents.Run($"  ·  {page}") { FontWeight = FontWeights.Normal });
                head.Children.Add(title);
                _slots.Children.Add(head);
                foreach (var slot in shownHere) _slots.Children.Add(SlotRow(pack, slot));
            }
        }
        if (_slots.Children.Count == 0) _slots.Children.Add(Quiet("No slots match."));
        DrawSelectionBar();
    }


    private void Pick(CharacterSlot slot)
    {
        if (_slot?.Name != slot.Name) _crop = null;
        _slot = slot;
        DrawSlots();
        DrawPreview();
    }

    private void DrawPreview()
    {
        _preview.Children.Clear();
        if (_pack is not { } pack) return;
        _slot ??= CharacterSlots.All[0];
        var slot = _slot;
        var shown = pack.Shown(slot);
        var picture = Load(shown?.Path);

        var head = new StackPanel();
        head.Children.Add(Heading(slot.Name, 20));
        head.Children.Add(Quiet($"{slot.Place} · {slot.When}"));
        var falls = slot.Chain.Skip(1).ToList();
        if (falls.Count > 0) head.Children.Add(Quiet($"Falls back to {string.Join(", then ", falls)}"));
        _preview.Children.Add(new Border { Child = head, Margin = new Thickness(0, 0, 0, 12) });

        // ----- In its place, as Nyatch draws it -----
        _sceneCard = Card(Scene(slot, CropShown(slot) ?? picture), new Thickness(0));
        _preview.Children.Add(_sceneCard);

        // ----- What it shows -----
        var about = new StackPanel();
        var state = StateOf(pack, slot);
        about.Children.Add(new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Text = state switch
            {
                State.Own => "Own picture.",
                State.Copy => $"Copy of {_copies[slot.Name]}. Replace it to make it unique.",
                State.Borrowed => $"Uses {shown!.Value.Name}. Add a picture to change it.",
                _ => slot.Core ? "Needed. Add a picture." : "Empty. Nothing shows here.",
            },
        });
        if (picture is not null)
        {
            var facts = new List<string>
            {
                System.IO.Path.GetFileName(picture.Path),
                $"{picture.TrimmedWidth} × {picture.TrimmedHeight} px",
                picture.IsGif ? $"{picture.FramesInFile} frames" : "Still",
                $"{picture.Bytes / 1024.0:0} KB",
            };
            if (pack.IsCropped(slot.Name)) facts.Add("edited");
            about.Children.Add(Quiet(string.Join(" · ", facts)));
        }
        var best = pack.Has(slot.Name) ? pack.BestSide(slot.Name) : Math.Max(slot.Width, slot.Height) * 2;
        about.Children.Add(Quiet($"Best size: {best} px on the longest side."));
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var choose = new Button { Content = pack.Has(slot.Name) ? "Replace…" : "Add picture…", Style = (Style)FindResource("AccentButtonStyle"), Margin = new Thickness(0, 0, 8, 8) };
        choose.Click += (_, _) => Choose(slot.Name);
        buttons.Children.Add(choose);
        if (pack.Has(slot.Name))
        {
            var remove = new Button { Content = "Remove", Margin = new Thickness(0, 0, 8, 8) };
            remove.Click += (_, _) =>
            {
                pack.RemovePicture(slot.Name);
                Say($"{slot.Name} removed.");
                Changed();
            };
            buttons.Children.Add(remove);
            if (_crop is null)
            {
                var crop = new Button { Content = "Crop…", Margin = new Thickness(0, 0, 8, 8) };
                crop.Click += (_, _) => StartCrop(pack, slot);
                buttons.Children.Add(crop);
            }
            if (picture is not null && (picture.Bytes > CharacterPacks.MaxPicture || picture.FramesInFile > Picture.MaxFrames || Math.Max(picture.TrimmedWidth, picture.TrimmedHeight) > best * 1.25))
            {
                var smaller = new Button { Content = "Make smaller", IsEnabled = !_busy.Contains(slot.Name), Margin = new Thickness(0, 0, 8, 8) };
                smaller.Click += (_, _) => _ = Work([slot.Name], "Making smaller", ShrinkOne);
                buttons.Children.Add(smaller);
            }
            if (pack.IsCropped(slot.Name))
            {
                var uncrop = new Button { Content = "Restore original", Margin = new Thickness(0, 0, 8, 8) };
                uncrop.Click += (_, _) =>
                {
                    pack.Uncrop(slot.Name);
                    _crop = null;
                    Say($"{slot.Name} is back to its original picture.");
                    Changed();
                };
                buttons.Children.Add(uncrop);
            }
        }
        about.Children.Add(buttons);
        about.Children.Add(Quiet("Or drop a PNG or GIF here."));
        _preview.Children.Add(Card(about));

        // ----- Cropping, or the picture close up -----
        if (_crop is not null && _crop.Slot == slot.Name)
        {
            var cropCard = Card(CropEditor(pack, slot));
            cropCard.Loaded += (_, _) => cropCard.BringIntoView();
            _preview.Children.Add(cropCard);
        }
        else if (picture is not null)
        {
            var big = new AnimatedImage { MaxWidth = 300, MaxHeight = 260, Picture = picture };
            var checker = new Border
            {
                Child = big, Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
                Background = new DrawingBrush
                {
                    TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute,
                    Drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x64)), null, Geometry.Parse("M0,0 H8 V8 H0Z M8,8 H16 V16 H8Z")),
                },
            };
            checker.Background.Freeze();
            var close = new StackPanel();
            close.Children.Add(Heading("Full size"));
            close.Children.Add(new Border { Child = checker, Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x4C)), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left });
            _preview.Children.Add(Card(close));
        }
    }

    // ---------- The place, drawn ----------

    /// <summary>The slot's place, as Nyatch lays it out (at its real size, scaled to fit here), with the picture in it.</summary>
    private FrameworkElement Scene(CharacterSlot slot, Picture? picture)
    {
        var accent = Accent();
        var accentBrush = new SolidColorBrush(accent);
        var ink = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF4));
        var soft = new SolidColorBrush(Color.FromRgb(0x5E, 0x60, 0x6A));
        var raised = new SolidColorBrush(Color.FromRgb(0x18, 0x19, 0x1E));
        var panel = slot.Place is CharacterSlots.Second or CharacterSlots.Side;
        double w = panel ? 448 : slot.Place == CharacterSlots.Settings ? 460 : 460, h = panel ? 336 : 184;
        var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true };
        var surface = new Rectangle { Width = w, Height = h, RadiusX = panel ? 18 : 26, RadiusY = panel ? 18 : 26, Fill = slot.Place == CharacterSlots.Settings ? new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x27)) : Brushes.Black };
        canvas.Children.Add(surface);
        void Bar(double x, double y, double bw, double bh, Brush fill)
        {
            var bar = new Rectangle { Width = bw, Height = bh, RadiusX = bh / 2, RadiusY = bh / 2, Fill = fill };
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
            canvas.Children.Add(bar);
        }
        // Her picture: fitted in a box, standing on its bottom edge, as Nyatch draws it.
        FrameworkElement Her(double x, double y, double bw, double bh)
        {
            var image = new AnimatedImage { Picture = picture, Width = bw, Height = bh, VerticalAlignment = VerticalAlignment.Bottom };
            FrameworkElement shown = image;
            if (picture is not null)
            {
                var fit = Math.Min(bw / picture.TrimmedWidth, bh / picture.TrimmedHeight);
                double pw = picture.TrimmedWidth * fit, ph = picture.TrimmedHeight * fit;
                image.Width = pw;
                image.Height = ph;
                x += (bw - pw) / 2;
                y += bh - ph;
                if (picture.Tile && slot.Shape != SlotShape.Round)
                {
                    // Drawn a little larger than its tile, the corners rounded.
                    image.RenderTransformOrigin = new Point(0.5, 0.5);
                    image.RenderTransform = new ScaleTransform(1.06, 1.06);
                    var holder = new Border { Child = image, Width = pw, Height = ph, ClipToBounds = true, CornerRadius = new CornerRadius(Math.Min(pw, ph) * 0.16) };
                    holder.Clip = new RectangleGeometry(new Rect(0, 0, pw, ph), Math.Min(pw, ph) * 0.16, Math.Min(pw, ph) * 0.16);
                    shown = holder;
                }
            }
            Canvas.SetLeft(shown, x);
            Canvas.SetTop(shown, y);
            canvas.Children.Add(shown);
            return shown;
        }

        switch (slot.Place)
        {
            case CharacterSlots.Time:
            case CharacterSlots.General when slot.Shape != SlotShape.Face:
                Bar(30, 52, 60, 6, soft);
                Bar(30, 68, 190, 40, ink);
                Bar(30, 118, 130, 8, soft);
                Her(w - 20 - 132, h - 12 - 132, 132, 132);
                break;
            case CharacterSlots.Calendar:
                var card = new Rectangle { Width = 93, Height = 148, RadiusX = 12, RadiusY = 12, Fill = raised };
                Canvas.SetLeft(card, 22);
                Canvas.SetTop(card, 16);
                canvas.Children.Add(card);
                Bar(40, 36, 56, 5, accentBrush);
                Bar(40, 70, 56, 34, ink);
                for (var row = 0; row < 6; row++)
                {
                    for (var day = 0; day < 7; day++) Bar(134 + day * 28 + 8, 22 + row * 24, 12, 5, row == 0 ? soft : ink);
                }
                Her(w - 24 - 100, 12, 100, h - 24);
                break;
            case CharacterSlots.Weather:
                Bar((w - 70) / 2, 10, 70, 6, accentBrush);
                Bar(22, 28, 30, 28, soft);
                Bar(60, 26, 64, 34, ink);
                Bar(136, 36, 52, 4, accentBrush);
                Bar(60, 66, 70, 5, soft);
                for (var i = 0; i < 5; i++)
                {
                    Bar(22 + i * 42 + 12, 98, 18, 5, ink);
                    Bar(22 + i * 42 + 10, 120, 22, 18, soft);
                    Bar(22 + i * 42 + 12, 152, 18, 4, soft);
                }
                for (var i = 0; i < 3; i++)
                {
                    Bar(258, 104 + i * 22, 40, 4, soft);
                    Bar(350, 104 + i * 22, 40, 4, soft);
                }
                Her(w - 24 - 88, 4, 88, 88);
                break;
            case CharacterSlots.Hardware:
                Bar(24, 18, 110, 6, ink);
                for (var i = 0; i < 3; i++)
                {
                    var c1 = new Rectangle { Width = 137, Height = 38, RadiusX = 9, RadiusY = 9, Fill = raised };
                    Canvas.SetLeft(c1, 24);
                    Canvas.SetTop(c1, 38 + i * 44);
                    canvas.Children.Add(c1);
                    var c2 = new Rectangle { Width = 137, Height = 38, RadiusX = 9, RadiusY = 9, Fill = raised };
                    Canvas.SetLeft(c2, w - 24 - 137);
                    Canvas.SetTop(c2, 38 + i * 44);
                    canvas.Children.Add(c2);
                }
                var cx = w / 2;
                var cy = 38 + 63.0;
                var ring = new Ellipse { Width = 114, Height = 114, Stroke = soft, StrokeThickness = 4 };
                Canvas.SetLeft(ring, cx - 57);
                Canvas.SetTop(ring, cy - 57);
                canvas.Children.Add(ring);
                var arc = new System.Windows.Shapes.Path
                {
                    Stroke = accentBrush, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Data = Geometry.Parse(FormattableString.Invariant($"M {cx},{cy - 57} A 57,57 0 0 1 {cx + 54.2},{cy + 17.6}")),
                };
                canvas.Children.Add(arc);
                var inner = 57 - 4 - 3.0;
                var image = Her(cx - inner, cy - inner, inner * 2, inner * 2);
                image.Clip = new EllipseGeometry(new Point(cx - Canvas.GetLeft(image), cy - Canvas.GetTop(image)), inner, inner);
                break;
            case CharacterSlots.Tray:
                Bar(26, 22, 70, 6, ink);
                if (slot.Name == "tray-end")
                {
                    var row = new Rectangle { Width = w - 52, Height = 30, RadiusX = 9, RadiusY = 9, Fill = raised };
                    Canvas.SetLeft(row, 26);
                    Canvas.SetTop(row, 46);
                    canvas.Children.Add(row);
                    Her(w / 2 - 64, h - 16 - 64, 64, 64);
                    Bar(w / 2 + 10, h - 16 - 34, 56, 4, soft);
                }
                else
                {
                    var drop = new Rectangle { Width = w - 52, Height = h - 62, RadiusX = 12, RadiusY = 12, Stroke = soft, StrokeDashArray = [4, 3] };
                    Canvas.SetLeft(drop, 26);
                    Canvas.SetTop(drop, 46);
                    canvas.Children.Add(drop);
                    Her(w / 2 - 35, 60, 70, 70);
                    Bar(w / 2 - 44, 140, 88, 4, soft);
                }
                break;
            case CharacterSlots.Player:
                var cardP = new Rectangle { Width = w - 40, Height = h - 50, RadiusX = 14, RadiusY = 14, Fill = raised };
                Canvas.SetLeft(cardP, 20);
                Canvas.SetTop(cardP, 40);
                canvas.Children.Add(cardP);
                var art = new Rectangle { Width = 56, Height = 56, RadiusX = 12, RadiusY = 12, Fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0x8F, 0xB1), accent, 45) };
                Canvas.SetLeft(art, 32);
                Canvas.SetTop(art, 52);
                canvas.Children.Add(art);
                Bar(102, 58, 140, 8, ink);
                Bar(102, 76, 90, 5, soft);
                Bar(48, 156, w - 96, 4, soft);
                Bar(48, 156, 160, 4, ink);
                Her(w - 20 - 14 - 52, 49, 52, 52);
                break;
            case CharacterSlots.Lookup:
                var field = new Rectangle { Width = w - 32, Height = 36, RadiusX = 12, RadiusY = 12, Fill = raised };
                Canvas.SetLeft(field, 16);
                Canvas.SetTop(field, 12);
                canvas.Children.Add(field);
                Bar(60, 28, 150, 5, ink);
                for (var i = 0; i < 3; i++) Bar(24, 70 + i * 30, 240 - i * 40, 6, soft);
                Her(21, 16, 28, 28);
                break;
            case CharacterSlots.Notices:
            case CharacterSlots.General:
                // The open notch with a notice under it: her face at the notice's right end.
                Bar(24, 18, 80, 10, ink);
                Bar(w - 70, 18, 40, 10, accentBrush);
                Bar(20, 52, w - 40, 1, soft);
                var badge = new Rectangle { Width = 34, Height = 34, RadiusX = 12, RadiusY = 12, Fill = raised };
                Canvas.SetLeft(badge, 24);
                Canvas.SetTop(badge, 70);
                canvas.Children.Add(badge);
                Bar(72, 80, 150, 7, ink);
                Bar(72, 94, 100, 5, soft);
                Her(w - 14 - 44, 65, 44, 44);
                break;
            case CharacterSlots.Settings:
                Bar(24, 24, 120, 14, ink);
                for (var i = 0; i < 3; i++)
                {
                    var rowS = new Rectangle { Width = w - 48, Height = 40, RadiusX = 4, RadiusY = 4, Fill = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)) };
                    Canvas.SetLeft(rowS, 24);
                    Canvas.SetTop(rowS, 56 + i * 44);
                    canvas.Children.Add(rowS);
                }
                Her(w - 24 - slot.Width, 20, slot.Width, slot.Height);
                break;
            default:
                // A page of the second notch or the side panel: where her picture sits there.
                if (slot.Shape == SlotShape.Face)
                {
                    // The closed notch or tab: a small pill.
                    surface.Width = slot.Place == CharacterSlots.Side ? 30 : 170;
                    surface.Height = slot.Place == CharacterSlots.Side ? 120 : 36;
                    surface.RadiusX = surface.RadiusY = slot.Place == CharacterSlots.Side ? 15 : 18;
                    Canvas.SetLeft(surface, (w - surface.Width) / 2);
                    Canvas.SetTop(surface, (h - surface.Height) / 2);
                    if (slot.Place != CharacterSlots.Side) Bar(w / 2 - 20, h / 2 - 4, 70, 8, ink);
                    Her(slot.Place == CharacterSlots.Side ? w / 2 - 12 : w / 2 - 54, h / 2 - 12, 24, 24);
                    canvas.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x28));
                    break;
                }
                Bar(14, 16, 110, 10, ink);
                if (slot.Width >= 100)
                {
                    Her(w / 2 - slot.Width / 2.0, 70, slot.Width, slot.Height);
                    Bar(w / 2 - 110, 70 + slot.Height + 14, 220, 5, soft);
                }
                else
                {
                    for (var i = 0; i < 4; i++)
                    {
                        var rowD = new Rectangle { Width = w - 40, Height = 44, RadiusX = 9, RadiusY = 9, Fill = raised };
                        Canvas.SetLeft(rowD, 14);
                        Canvas.SetTop(rowD, 40 + i * 50);
                        canvas.Children.Add(rowD);
                    }
                    Her(w / 2 - slot.Width / 2.0, 250, slot.Width, slot.Height);
                }
                break;
        }

        var wallpaper = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(8),
            Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x23, 0x46), Color.FromRgb(0x17, 0x22, 0x30), 35),
            Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
        };
        return wallpaper;
    }

    // ---------- Doing ----------

    private void Changed()
    {
        if (_pack is null) return;
        try
        {
            _pack.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Say($"Could not save: {error.Message}");
        }
        ShowAll();
    }

    private void Put(string name, string file)
    {
        if (_pack is null) return;
        try
        {
            _pack.SetPicture(name, file);
            Say($"Added to {name}.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Say(error.Message);
        }
        Changed();
    }

    private void Choose(string name)
    {
        var pick = new OpenFileDialog { Title = $"A picture for {name}", Filter = "Pictures (PNG or GIF)|*.png;*.gif" };
        if (pick.ShowDialog(this) == true) Put(name, pick.FileName);
    }

    private void NewPack()
    {
        var pick = new OpenFolderDialog { Title = "Choose an empty folder" };
        if (pick.ShowDialog(this) != true) return;
        if (File.Exists(System.IO.Path.Combine(pick.FolderName, "pack.json")))
        {
            Say("Opened the theme already in that folder.");
        }
        OpenFolder(pick.FolderName);
    }

    private void OpenPack()
    {
        var pick = new OpenFolderDialog { Title = "Choose a theme folder" };
        if (pick.ShowDialog(this) == true) OpenFolder(pick.FolderName);
    }

    public void OpenFolder(string folder)
    {
        try
        {
            _pack = PackProject.Open(folder);
            _pack.Save();
            _slot = null;
            Remember(folder);
            Say($"Opened {_pack.Manifest.Name}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Say($"Could not open {folder}: {error.Message}");
        }
        ShowAll();
    }

    private static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{bytes / 1024.0:0} KB";

    private async void ShrinkAll()
    {
        if (_pack is not { } pack)
        {
            Say("Open a theme first.");
            return;
        }
        Say("Looking for pictures that are too big…");
        var names = await Task.Run(() => CharacterSlots.All.Select(s => s.Name).Append("preview").Where(n => !_busy.Contains(n) && pack.TooBig(n)).ToList());
        if (names.Count == 0)
        {
            Say("Every picture is already a good size.");
            return;
        }
        await Work(names, "Making smaller", ShrinkOne);
        Say($"Done: {names.Count} pictures made smaller. Restore original undoes one.");
    }

    private void FillAll()
    {
        if (_pack is null)
        {
            Say("Open a theme first.");
            return;
        }
        try
        {
            var made = _pack.FillAll();
            Say(made == 0 ? "No empty slots." : $"Filled {made} slots with copies. Replace them one by one.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Say($"Failed: {error.Message}");
        }
        Changed();
    }

    /// <summary>Make the pack and install it in Nyatch on this PC, in use, as a user would get it.</summary>
    private void TryInNyatch()
    {
        if (!Ready()) return;
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{_pack!.Manifest.Id}-try.nyatch");
        try
        {
            _pack.MakeFile(file);
            var installed = new CharacterPacks().InstallFile(file);
            // A running Nyatch puts the newest pack to use and shows it (the same as opening a .nyatch file).
            if (EventWaitHandle.TryOpenExisting(@"Local\Nyatch.ShowPacks", out var arrived))
            {
                using (arrived) arrived.Set();
                Say($"{installed.Name} is now in use in Nyatch.");
            }
            else Say($"{installed.Name} is installed. Start Nyatch to see it.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Say($"Failed: {error.Message}");
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    private void MakeFile()
    {
        if (!Ready()) return;
        var pick = new SaveFileDialog { Title = "Save the theme", FileName = _pack!.Manifest.Id + CharacterPacks.Extension, Filter = "Nyatch theme (*.nyatch)|*.nyatch" };
        if (pick.ShowDialog(this) != true) return;
        try
        {
            var made = _pack.MakeFile(pick.FileName);
            Say($"Saved {System.IO.Path.GetFileName(pick.FileName)} ({new FileInfo(pick.FileName).Length / 1024.0 / 1024.0:0.0} MB).");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Say($"Failed: {error.Message}");
        }
    }

    private void MakeIntoList()
    {
        if (!Ready()) return;
        var pick = new OpenFolderDialog { Title = "Choose the theme list folder (the nyatch-themes branch, checked out)" };
        if (pick.ShowDialog(this) != true) return;
        try
        {
            var made = _pack!.MakeInto(pick.FolderName);
            Say($"Added {made.Name} {made.Version} to the theme list.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Say($"Failed: {error.Message}");
        }
    }

    /// <summary>Whether the pack can be made: a pack open, and nothing the checks call a problem.</summary>
    private bool Ready()
    {
        if (_pack is null)
        {
            Say("Open a theme first.");
            return false;
        }
        var problems = _pack.Check().Where(f => f.Problem).ToList();
        if (problems.Count == 0) return true;
        Say($"Fix first: {problems[0].Text}");
        return false;
    }

    // ---------- The last pack opened ----------

    private static string Memory => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyatch", "theme-editor-last.txt");

    private static void Remember(string folder)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Memory)!);
            File.WriteAllText(Memory, folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string? LastFolder()
    {
        try
        {
            return File.Exists(Memory) && File.ReadAllText(Memory).Trim() is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>For the self-test and pictures of the window: choose a slot by name.</summary>
    public void PickSlot(string name)
    {
        if (CharacterSlots.Find(name) is { } slot) Pick(slot);
    }
}

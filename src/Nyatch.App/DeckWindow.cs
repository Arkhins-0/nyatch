using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// The second pill: a small pill that opens into pages of widgets, turned with the mouse wheel. It takes the
/// island's look (true black, the same springs) and is placed the same way,
/// from the "Second pill" page of settings.
/// </summary>
internal sealed partial class DeckWindow : Window
{
    private const double PillWidth = 168, PillHeight = 36, OpenWidth = 448, OpenHeight = 336, Margin_ = 16, EdgeGap = 8;

    // One set of brushes, shared by everything in the pill and recoloured in
    // place when the island's theme changes, so nothing needs rebuilding.
    private static SolidColorBrush Ink = new(Colors.White);
    private static SolidColorBrush Muted = new(Color.FromRgb(0x9B, 0x9B, 0xA5));
    private static SolidColorBrush Surface = new(Color.FromRgb(0x08, 0x08, 0x0A));
    private static SolidColorBrush Raised = new(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
    private static SolidColorBrush RaisedStrong = new(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static SolidColorBrush Hairline = new(Color.FromRgb(0x1E, 0x1E, 0x22));
    private static SolidColorBrush Accent = new(Color.FromRgb(0x4C, 0xC9, 0xF0));

    /// <summary>The island's theme presets: background, text, secondary text, border (from its own table).</summary>
    internal static readonly Dictionary<string, (string Background, string Text, string Secondary, string Border)> Palettes = new()
    {
        ["obsidian"] = ("#08080A", "#FFFFFF", "#9B9BA5", "#1E1E22"),
        ["graphite"] = ("#1C1C1E", "#FFFFFF", "#A8A8AE", "#323236"),
        ["slate"] = ("#111721", "#E9EEF6", "#92A2B8", "#232C3A"),
        ["nord"] = ("#2E3440", "#ECEFF4", "#A7B0C0", "#3B4252"),
        ["evergreen"] = ("#0C1512", "#E4F1EA", "#8CAE9D", "#1A2A23"),
        ["espresso"] = ("#1A1512", "#F6EEE7", "#B7A395", "#2E2420"),
        ["plum"] = ("#16111C", "#F1EAF6", "#AC9BB9", "#281F33"),
        ["porcelain"] = ("#F5F6F8", "#14161A", "#5B6270", "#D8DBE1"),
    };

    internal static Color Hex(string text, Color fallback)
    {
        text = text.Trim().TrimStart('#');
        return text.Length >= 6 && uint.TryParse(text[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : fallback;
    }

    // Flat controls in the island's manner: a lifted fill, no outline, an
    // accent ring only for keyboard focus.
    private const string ControlStyles = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource DeckInk}"/>
            <Setter Property="Background" Value="{DynamicResource DeckRaised}"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="face" Background="{TemplateBinding Background}" CornerRadius="9" Padding="{TemplateBinding Padding}" BorderThickness="1.2" BorderBrush="Transparent">
                    <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="False"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="face" Property="Background" Value="{DynamicResource DeckRaisedStrong}"/></Trigger>
                    <Trigger Property="IsPressed" Value="True"><Setter TargetName="face" Property="Opacity" Value="0.72"/></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="face" Property="BorderBrush" Value="{DynamicResource DeckAccent}"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="TextBox">
            <Setter Property="Foreground" Value="{DynamicResource DeckInk}"/>
            <Setter Property="CaretBrush" Value="{DynamicResource DeckAccent}"/>
            <Setter Property="SelectionBrush" Value="{DynamicResource DeckAccent}"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                  <Border x:Name="field" Background="{DynamicResource DeckRaisedStrong}" CornerRadius="10" BorderThickness="1.2" BorderBrush="Transparent">
                    <ScrollViewer x:Name="PART_ContentHost" Margin="9,6" VerticalScrollBarVisibility="{TemplateBinding VerticalScrollBarVisibility}"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="field" Property="BorderBrush" Value="{DynamicResource DeckAccent}"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="PlainBox" TargetType="TextBox">
            <Setter Property="Foreground" Value="{DynamicResource DeckInk}"/>
            <Setter Property="CaretBrush" Value="{DynamicResource DeckAccent}"/>
            <Setter Property="SelectionBrush" Value="{DynamicResource DeckAccent}"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                  <ScrollViewer x:Name="PART_ContentHost" Background="Transparent" VerticalScrollBarVisibility="{TemplateBinding VerticalScrollBarVisibility}" HorizontalScrollBarVisibility="Disabled"/>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="{DynamicResource DeckInk}"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                  <DockPanel Background="Transparent">
                    <Border x:Name="box" DockPanel.Dock="Left" Width="16" Height="16" CornerRadius="5" BorderThickness="1.4" BorderBrush="{DynamicResource DeckMuted}" VerticalAlignment="Top" Margin="0,1,9,0">
                      <TextBlock x:Name="tick" Text="&#xE73E;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="10" Foreground="{DynamicResource DeckSurface}" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
                    </Border>
                    <ContentPresenter VerticalAlignment="Center"/>
                  </DockPanel>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="box" Property="Background" Value="{DynamicResource DeckAccent}"/>
                      <Setter TargetName="box" Property="BorderBrush" Value="{DynamicResource DeckAccent}"/>
                      <Setter TargetName="tick" Property="Visibility" Value="Visible"/>
                    </Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="{DynamicResource DeckAccent}"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;
    private static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly FontFamily Face = new("Segoe UI Variable Display, Segoe UI");

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>A page of the pill. It is built the first time it is shown and then kept, so turning pages is instant and a terminal keeps running.</summary>
    private sealed class Page(string name, Func<UIElement> build, string? native = null)
    {
        public string Name { get; } = name;

        /// <summary>For a page the island draws itself: its id there ("notifications"). Null for this window's pages.</summary>
        public string? Native { get; } = native;
        public Func<UIElement> Build { get; } = build;
        public UIElement? View { get; set; }
        /// <summary>Run twice a second while the page is on show.</summary>
        public List<Action> Updates { get; } = [];
        /// <summary>Run each time the page comes into view.</summary>
        public Action? Shown { get; set; }
    }

    private readonly SettingsStore _settings;
    private readonly DeckStore _deck;
    private readonly ClipHistory _clips;
    private readonly RecentApps _recentApps;
    private readonly ChatHistory _chats;
    private readonly Action<string, string> _notify;

    private readonly Countdown _timer = new(TimeSpan.FromMinutes(10));
    private readonly Stopwatch2 _watch = new();
    private readonly Pomodoro _pomodoro = new();

    private readonly Border _pill = new();
    private readonly TextBlock _status = new();
    private readonly StackPanel _closed = new();
    private readonly Grid _open = new();
    private readonly StackPanel _dots = new();
    private readonly ScrollViewer _body = new();
    // Every page, by its id in Deck.Pages; the ones on show, in the chosen order, are _tabs.
    private readonly Dictionary<string, Page> _pagesById = new(StringComparer.Ordinal);
    private readonly List<Page> _tabs = [];
    /// <summary>The updates of the page on show (or being built).</summary>
    private List<Action> _everySecond = [];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _appsWatch = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _saveSoon = new() { Interval = TimeSpan.FromMilliseconds(600) };

    // The island's material: depth shading and an accent bloom over the fill.
    private readonly Rectangle _depth = new() { IsHitTestVisible = false, Opacity = 0.085 };
    private readonly Rectangle _bloom = new() { IsHitTestVisible = false, Opacity = 0.055 };
    private readonly DropShadowEffect _shadow = new() { BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.35, Color = Colors.Black };
    /// <summary>The island's shape setting: "default" (pill), "w11" (rounded box) or "notch" (flush to the edge).</summary>
    private string _shape = "default";

    private int _tab;
    private TimeSpan _animationStart;
    private bool _expanded;
    private bool _top;
    private bool _animating;
    private double _w = PillWidth, _h = PillHeight, _vw, _vh;
    /// <summary>The closed pill's width, which follows what it says.</summary>
    private double _closedWidth = PillWidth;
    private TimeSpan _lastFrame;
    private int _appTicks;
    private string _appsShown = "";
    private List<ActiveApp>? _lastActive;
    private DateTime _lastScroll, _lastTabTurn;

    public DeckWindow(SettingsStore settings, DeckStore deck, ClipHistory clips, RecentApps recentApps, ChatHistory chats, Action<string, string> notify)
    {
        _settings = settings;
        _deck = deck;
        _clips = clips;
        _recentApps = recentApps;
        _chats = chats;
        // Browser pages were listed as apps of their own by an earlier version; they are not any more.
        foreach (var page in _recentApps.Closed([], RecentApps.Kept).Where(a => a.Path.StartsWith("page:", StringComparison.Ordinal)).ToList())
        {
            _recentApps.Forget(page.Path);
        }
        _recentApps.Save();
        try
        {
            var icons = System.IO.Path.Combine(SettingsStore.DefaultFolder, "favicons");
            if (Directory.Exists(icons)) Directory.Delete(icons, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        _notify = notify;
        _pagesById["apps"] = new("Apps", () => new System.Windows.Controls.Grid(), native: "apps");
        _pagesById["tray"] = new("Tray", () => new Grid(), native: "tray");
        _pagesById["clipboard"] = new("Clipboard", () => new Grid(), native: "clipboard");
        // Drawn by the island (native), like the top notch's pages: this window steps aside for it.
        _pagesById["notifications"] = new("Notifications", () => new Grid(), native: "notifications");
        foreach (var (id, name) in new[] { ("claude", "Claude"), ("chatgpt", "ChatGPT"), ("gemini", "Gemini") })
        {
            // A key added in settings shows the chat the next time the page is on show.
            var chat = _aiPages[id];
            _pagesById[id] = new(name, () => new Grid(), native: id) { Shown = () => AiChanged(chat) };
        }
        _pagesById["focus"] = new("Focus", () => new Grid(), native: "focus");
        _pagesById["notes"] = new("Notes & to-dos", () => new Grid(), native: "notes");
        _pagesById["terminal"] = new("Terminal", () => new Grid(), native: "terminal")
        {
            // The first visit opens a tab (the shell the settings name), so the page is ready to type in.
            Shown = () =>
            {
                if (_sessions.Count == 0) OpenShell(_settings.Get("Deck.TerminalShell") == "powershell" ? ShellKind.PowerShell : ShellKind.Cmd);
                TerminalChanged();
            },
        };
        _pagesById["online"] = new("Online", () =>
        {
            _everySecond.Add(OnlineEverySecond);
            return new Grid();
        }, native: "online")
        { Shown = OnlineOnShow };
        // The page picker the hotkey opens: never among the pages the wheel turns.
        _pagesById["keys"] = new("Open a page", () => new Grid(), native: "keys");
        ApplyPageOrder();

        Title = "Nyatch second pill";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = OpenWidth + Margin_ * 2;
        Height = OpenHeight + Margin_ * 2;
        FontFamily = Face;
        Foreground = Ink;
        UseLayoutRounding = true;

        // ----- The pill -----
        _pill.Background = Surface;
        _pill.BorderBrush = Hairline;
        _pill.BorderThickness = new Thickness(0.8);
        _pill.Margin = new Thickness(Margin_);
        _pill.Effect = _shadow;
        var styles = (ResourceDictionary)XamlReader.Parse(ControlStyles);
        Resources.MergedDictionaries.Add(styles);
        ShareBrushes();
        // Darker toward the bottom: 0, 25% at 45% of the way, full at the end.
        _depth.Fill = new LinearGradientBrush(
            new GradientStopCollection { new(Color.FromArgb(0, 0, 0, 0), 0), new(Color.FromArgb(64, 0, 0, 0), 0.45), new(Colors.Black, 1) },
            new Point(0, 0), new Point(0, 1));
        _pill.Cursor = Cursors.Arrow;

        _closed.Orientation = Orientation.Horizontal;
        _closed.HorizontalAlignment = HorizontalAlignment.Center;
        _closed.VerticalAlignment = VerticalAlignment.Center;
        _closed.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs, FontSize = 13, Foreground = Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
        _status.FontSize = 13;
        _status.FontWeight = FontWeights.SemiBold;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _closed.Children.Add(_status);

        // No tab bar: the pages are turned with the wheel, and the island's page dots on the right show where you are.
        _body.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _body.Padding = new Thickness(14, PagePadTop, 26, PagePadBottom);
        _body.Focusable = false;
        _open.Children.Add(_body);
        _dots.HorizontalAlignment = HorizontalAlignment.Right;
        _dots.VerticalAlignment = VerticalAlignment.Center;
        _dots.Margin = new Thickness(0, 0, 7, 0);
        _open.Children.Add(_dots);
        _open.Width = OpenWidth;
        _open.Height = OpenHeight;
        _open.HorizontalAlignment = HorizontalAlignment.Left;
        _open.VerticalAlignment = VerticalAlignment.Top;
        _open.Visibility = Visibility.Collapsed;

        var layers = new Grid();
        layers.Children.Add(_depth);
        layers.Children.Add(_bloom);
        layers.Children.Add(_open);
        layers.Children.Add(_closed);
        _pill.Child = layers;
        var root = new Grid { Children = { _pill } };
        Content = root;
        StartNativeOutline();
        StartSide();

        // ----- Opening and closing -----
        // The island decides, with the top notch's rule: open while the pointer is on the notch, closed
        // when it leaves (see DeckWindow.Native.cs). This window follows, and holds it open while
        // something is being typed in it.
        _pill.MouseEnter += (_, _) => NoteForeground();
        PreviewGotKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, HoldWhileTyping);
        PreviewLostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, HoldWhileTyping);
        Deactivated += (_, _) =>
        {
            Island.DeckHold(false);
            if (!_pill.IsMouseOver) Collapse();
        };
        PreviewKeyDown += (_, key) =>
        {
            if (key.Key == Key.Escape) Collapse();
        };
        _pill.PreviewMouseWheel += (_, wheel) =>
        {
            if (!_expanded || wheel.Delta == 0) return;
            var down = wheel.Delta < 0;
            var now = DateTime.UtcNow;
            // Over something that scrolls (a long page, the terminal, a note), it scrolls until it reaches its end...
            if (CanScrollUnderPointer(down))
            {
                _lastScroll = now;
                return;
            }
            // A text box being typed in keeps the wheel to itself.
            if (Keyboard.FocusedElement is TextBoxBase { IsMouseOver: true }) return;
            wheel.Handled = true;
            // ...and a moment must pass after scrolling, so reaching the end does not flip the tab by accident.
            if (now - _lastScroll < TimeSpan.FromMilliseconds(450) || now - _lastTabTurn < TimeSpan.FromMilliseconds(160)) return;
            _lastTabTurn = now;
            ShowTab(TurnFrom(_tab, down), down ? 1 : -1);
        };
        SourceInitialized += (_, _) =>
        {
            Native.SetToolWindow(this, takesKeyboard: false);
            RegisterDeckHotkey();
        };
        StartClipboard();
        StartMenu();

        _pill.AllowDrop = true;
        _pill.DragEnter += (_, drag) =>
        {
            if (!drag.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var tray = _tabs.FindIndex(t => t.Name == "Tray");
            if (tray < 0) return;  // the Tray page is turned off
            _tab = tray;
            if (_expanded) ShowTab(_tab, 0);
            else Expand();
        };
        _pill.DragOver += (_, drag) =>
        {
            drag.Effects = drag.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
            drag.Handled = true;
        };
        _pill.Drop += (_, drop) =>
        {
            if (drop.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            foreach (var path in paths) Island.TrayAdd(path);
            var tray = _tabs.FindIndex(t => t.Name == "Tray");
            if (tray >= 0) ShowTab(tray, 0);
        };

        _clock.Tick += (_, _) => EverySecond();
        _clock.Start();
        // Running apps are noted while the pill is shown, so the Apps tab can list the ones closed since.
        _appsWatch.Tick += (_, _) => WatchApps();
        _saveSoon.Tick += (_, _) =>
        {
            _saveSoon.Stop();
            _deck.Save();
        };
        Closed += (_, _) =>
        {
            _deck.Save();
            _clips.Save();
            _recentApps.Save();
            StopTerminals();
        };

        Paint();
        UpdateStatus();
        // Build the pages while nothing else is happening, so the first opening is as quick as the rest.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            foreach (var page in _tabs) Build(page);
        });
    }

    /// <summary>The height a page has to fill (a terminal or a note fills it exactly).</summary>
    private const double PagePadTop = 14, PagePadBottom = 12;
    private static double PageHeight => OpenHeight - PagePadTop - PagePadBottom;

    // ---------- Pages ----------

    /// <summary>
    /// The pages and their order, from Deck.Pages ("apps,~gemini,-online": a tilde for hotkey only, a
    /// minus hides one; see DeckPages). The pages on the notch come first, then the hotkey-only ones,
    /// then the page picker.
    /// </summary>
    private void ApplyPageOrder()
    {
        var current = _tab >= 0 && _tab < _tabs.Count ? _tabs[_tab] : null;
        // While the side panel is on, the chats are there, not here.
        var order = DeckPages.Order(_settings.Get("Deck.Pages")).Where(o => _pagesById.ContainsKey(o.Id) && !(SideOn && AiIds.Contains(o.Id))).ToList();
        var shown = order.Where(o => o.Show == PageShow.Shown).Select(o => _pagesById[o.Id]).ToList();
        if (shown.Count == 0) shown.Add(_pagesById["apps"]);  // never no page at all
        var quick = order.Where(o => o.Show == PageShow.HotkeyOnly).Select(o => _pagesById[o.Id]).Where(p => !shown.Contains(p));
        var pages = shown.Concat(quick).Append(_pagesById["keys"]).ToList();
        _turnCount = shown.Count;
        if (pages.SequenceEqual(_tabs)) return;
        _tabs.Clear();
        _tabs.AddRange(pages);
        _tab = current is not null && _tabs.IndexOf(current) is var at && at >= 0 ? at : 0;
        if (_expanded) ShowTab(_tab, 0);
    }

    // ---------- Placement ----------

    /// <summary>Apply the "Second pill" settings: shown or not, where, on top or not.</summary>
    public void Apply()
    {
        ApplySide();
        if (!NotchUse.DeckInUse(_settings))
        {
            _appsWatch.Stop();
            ListenToClipboard(false);
            Hide();
            return;
        }
        _appsWatch.Start();
        ListenToClipboard(true);
        ApplyPageOrder();
        RegisterDeckHotkey();
        ApplyFeatures();
        _status.Text = "";
        UpdateStatus();
        ApplyTheme();
        Topmost = _settings.Get("Deck.AlwaysOnTop") == "true";
        var position = _settings.Get("Deck.Position");
        _top = position.StartsWith("top", StringComparison.Ordinal);
        var side = position.EndsWith("left", StringComparison.Ordinal) ? HorizontalAlignment.Left
            : position.EndsWith("right", StringComparison.Ordinal) ? HorizontalAlignment.Right
            : HorizontalAlignment.Center;
        _pill.HorizontalAlignment = side;
        _pill.VerticalAlignment = _top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        // The notch shape sits flush against its edge, with no outline there.
        var gap = _shape == "notch" ? 0 : EdgeGap;
        _pill.BorderThickness = _shape == "notch" ? (_top ? new Thickness(0.8, 0, 0.8, 0.8) : new Thickness(0.8, 0.8, 0.8, 0)) : new Thickness(0.8);

        // The work area leaves out the taskbar (or a dock that reserves its edge).
        var area = SystemParameters.WorkArea;
        double.TryParse(_settings.Get("Deck.OffsetX"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dx);
        double.TryParse(_settings.Get("Deck.OffsetY"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dy);
        Left = dx + side switch
        {
            HorizontalAlignment.Left => area.Left + gap - Margin_,
            HorizontalAlignment.Right => area.Right - gap + Margin_ - Width,
            _ => area.Left + (area.Width - Width) / 2,
        };
        Top = dy + (_top ? area.Top + gap - Margin_ : area.Bottom - gap + Margin_ - Height);
        Paint();
        if (!IsVisible) Show();
        PlaceOverNative();
    }

    /// <summary>Take the island's look: its theme preset, accent, shape, material and font.</summary>
    /// <summary>Hand the shared brushes to the window, its styles and the pill.</summary>
    private void ShareBrushes()
    {
        Resources["DeckInk"] = Ink;
        Resources["DeckMuted"] = Muted;
        Resources["DeckSurface"] = Surface;
        Resources["DeckRaised"] = Raised;
        Resources["DeckRaisedStrong"] = RaisedStrong;
        Resources["DeckAccent"] = Accent;
        Foreground = Ink;
        _pill.Background = Surface;
        _pill.BorderBrush = Hairline;
    }

    /// <summary>
    /// Give a shared brush a new colour. WPF sometimes locks a brush that something on screen
    /// has drawn with, and a locked brush cannot change; then a new one takes its place.
    /// </summary>
    private bool Recolor(ref SolidColorBrush brush, Color color)
    {
        if (brush.Color == color) return false;
        if (!brush.IsFrozen)
        {
            brush.Color = color;
            return false;
        }
        brush = new SolidColorBrush(color);
        return true;
    }

    private void ApplyTheme()
    {
        var palette = Palettes.GetValueOrDefault(_settings.Get("Themes.ThemePreset"), Palettes["obsidian"]);
        var background = Hex(palette.Background, Color.FromRgb(0x08, 0x08, 0x0A));
        var text = Hex(palette.Text, Colors.White);
        int.TryParse(_settings.Get("Themes.PillOpacity"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var opacity);
        var replaced = false;
        replaced |= Recolor(ref Surface, Color.FromArgb((byte)(255 * Math.Clamp(opacity <= 0 ? 96 : opacity, 20, 100) / 100), background.R, background.G, background.B));
        replaced |= Recolor(ref Ink, text);
        replaced |= Recolor(ref Muted, Hex(palette.Secondary, Color.FromRgb(0x9B, 0x9B, 0xA5)));
        replaced |= Recolor(ref Hairline, Hex(palette.Border, Color.FromRgb(0x1E, 0x1E, 0x22)));
        // Cards are a lift of the text colour over the surface: 8.5%, and 15% for fields and hover.
        replaced |= Recolor(ref Raised, Color.FromArgb(22, text.R, text.G, text.B));
        replaced |= Recolor(ref RaisedStrong, Color.FromArgb(38, text.R, text.G, text.B));

        // "Auto" takes the playing album's colour on the island; here, with no
        // album, it is the island's default accent.
        var glass = SystemParameters.WindowGlassColor;
        replaced |= Recolor(ref Accent, _settings.Get("Themes.AccentColorMode") switch
        {
            "custom" => Hex(_settings.Get("Themes.CustomAccentHex"), Color.FromRgb(0x4C, 0xC9, 0xF0)),
            "system" => Color.FromRgb(glass.R, glass.G, glass.B),
            _ => Color.FromRgb(0x4C, 0xC9, 0xF0),
        });
        if (replaced)
        {
            // Pages already built hold the old brushes; they are built again with the new ones.
            // (The terminal keeps its view: its sessions and their output live on regardless.)
            ShareBrushes();
            foreach (var page in _pagesById.Values)
            {
                if (page.Name == "Terminal") continue;
                page.View = null;
                page.Updates.Clear();
            }
            if (_expanded) ShowTab(_tab, 0);
        }

        var material = _settings.Get("Themes.MaterialDepth") == "true";
        _depth.Visibility = _bloom.Visibility = material ? Visibility.Visible : Visibility.Collapsed;
        // A soft wash of the accent from the top centre: 75% of the width, 115% of the height.
        _bloom.Fill = new RadialGradientBrush(Accent.Color, Color.FromArgb(0, Accent.Color.R, Accent.Color.G, Accent.Color.B))
        {
            Center = new Point(0.5, 0),
            GradientOrigin = new Point(0.5, 0),
            RadiusX = 0.75,
            RadiusY = 1.15,
        };
        _pill.Effect = _settings.Get("Themes.DropShadow") == "true" ? _shadow : null;

        var family = _settings.Get("Themes.FontFamily").Trim();
        FontFamily = family.Length > 0 ? new FontFamily(family + ", Segoe UI Variable Display, Segoe UI") : Face;
        _shape = _settings.Get("Appearance.ShapeStyle");
    }

    // ---------- The spring ----------

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        // While open, clicking a text box may take the keyboard.
        Native.SetToolWindow(this, takesKeyboard: true);
        NoteForeground();
        ShowTab(_tab, 0);
        _open.Visibility = Visibility.Visible;
        ShowOverNative();
        Animate();
        // Fresh window list for the Apps page, once the first frame is out.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => NoteActiveApps());
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        // A hotkey-only page (or the picker) is left: the next opening is on the notch's pages.
        if (_tab >= _turnCount) _tab = 0;
        Native.SetToolWindow(this, takesKeyboard: false);
        Keyboard.ClearFocus();
        _deck.Save();
        Island.DeckHold(false);
        Island.DeckOpen(false);
        Animate();
    }

    private void Animate()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        _animationStart = TimeSpan.Zero;
        CompositionTarget.Rendering += Frame;
    }

    // The island's own numbers: a stiffer, quicker spring while growing and a
    // softer one while shrinking, so the pill pops open and eases shut.
    private void Frame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_animationStart == TimeSpan.Zero) _animationStart = now;
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _lastFrame).TotalSeconds, 0.001, 0.05);
        _lastFrame = now;
        var (tw, th) = _expanded ? (OpenWidth, OpenHeight) : (_closedWidth, PillHeight);
        var (stiffness, damping) = _expanded ? (380.0, 26.0) : (200.0, 28.0);
        for (var left = dt; left > 0; left -= 0.001)
        {
            var step = Math.Min(left, 0.001);
            _vw += (-stiffness * (_w - tw) - damping * _vw) * step;
            _w += _vw * step;
            _vh += (-stiffness * (_h - th) - damping * _vh) * step;
            _h += _vh * step;
        }
        // The pages follow the island's outline, so the frames go on until it has finished too
        // (or a second and a half, in case it never does).
        var outline = Island.DeckOpenness();
        var outlineDone = Math.Abs(outline - (_expanded ? 1 : 0)) < 0.01 || now - _animationStart > TimeSpan.FromSeconds(1.5);
        if (outlineDone && Math.Abs(_w - tw) < 0.05 && Math.Abs(_h - th) < 0.05 && Math.Abs(_vw) < 0.05 && Math.Abs(_vh) < 0.05)
        {
            (_w, _h, _vw, _vh) = (tw, th, 0, 0);
            _animating = false;
            CompositionTarget.Rendering -= Frame;
            // The pages are kept (a terminal goes on running); only the view is hidden.
            if (!_expanded)
            {
                _open.Visibility = Visibility.Collapsed;
                HideOverNative();
            }
        }
        Paint();
    }

    private void Paint()
    {
        var w = Math.Max(PillHeight, _w);
        var h = Math.Max(PillHeight, _h);
        _pill.Width = w;
        _pill.Height = h;
        // The island's three shapes. Pill: fully round, then a fixed radius once
        // it is a panel. Windows 11: a rounded box. Notch: square against the
        // screen edge, rounded on the far side.
        var radius = _shape == "w11" ? Math.Min(h / 2, 10) : _shape == "notch" ? Math.Min(h / 2, 18) : Math.Min(h / 2, 24);
        var corners = _shape == "notch" ? (_top ? new CornerRadius(0, 0, radius, radius) : new CornerRadius(radius, radius, 0, 0)) : new CornerRadius(radius);
        _pill.CornerRadius = corners;
        _pill.Clip = Rounded(w, h, corners);
        var opened = Math.Clamp((h - PillHeight) / (OpenHeight - PillHeight), 0, 1);
        // The content shows early in the opening, so the pill never looks empty while it grows.
        _open.Opacity = Math.Clamp((opened - 0.12) / 0.45, 0, 1);
        _closed.Opacity = Math.Clamp(1 - opened * 4, 0, 1);
        _closed.Visibility = _closed.Opacity <= 0 ? Visibility.Collapsed : Visibility.Visible;
        // Opening upward from a bottom edge: the content stays pinned to the pill's top-left as it grows.
        _open.Margin = new Thickness(0);
        PaintOverNative(opened);
    }

    /// <summary>A rectangle with its own radius at each corner.</summary>
    private static Geometry Rounded(double w, double h, CornerRadius r)
    {
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            void Corner(Point to, double radius)
            {
                if (radius > 0) pen.ArcTo(to, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                else pen.LineTo(to, true, false);
            }
            pen.BeginFigure(new Point(r.TopLeft, 0), true, true);
            pen.LineTo(new Point(w - r.TopRight, 0), true, false);
            Corner(new Point(w, r.TopRight), r.TopRight);
            pen.LineTo(new Point(w, h - r.BottomRight), true, false);
            Corner(new Point(w - r.BottomRight, h), r.BottomRight);
            pen.LineTo(new Point(r.BottomLeft, h), true, false);
            Corner(new Point(0, h - r.BottomLeft), r.BottomLeft);
            pen.LineTo(new Point(0, r.TopLeft), true, false);
            Corner(new Point(r.TopLeft, 0), r.TopLeft);
        }
        shape.Freeze();
        return shape;
    }

    // ---------- Once a second ----------

    private void EverySecond()
    {
        var now = DateTime.Now;
        if (_timer.TakeFinished(now)) Ring("Timer finished", "Your timer has run out.");
        if (_pomodoro.Advance(now) is { } ended) Ring(ended, _pomodoro.OnBreak ? "Time for a break." : "Back to focus.");
        UpdateStatus();
        NoteForeground();
        SendNativeData(tick: true);
        if (_expanded)
        {
            foreach (var update in _everySecond.ToArray()) update();
        }
    }

    private void Ring(string title, string text)
    {
        if (_settings.Get("Deck.FocusSound") == "true") System.Media.SystemSounds.Asterisk.Play();
        if (_settings.Get("Deck.FocusNotify") == "true") _notify(title, text);
    }

    /// <summary>What the closed pill says: a running clock first, then the to-do count.</summary>
    private void UpdateStatus()
    {
        var now = DateTime.Now;
        var text =
            _pomodoro.Clock.Running ? (_pomodoro.OnBreak ? "Break " : "Focus ") + Clock(_pomodoro.Clock.Left(now))
            : _timer.Running ? Clock(_timer.Left(now))
            : _watch.Running ? Clock(_watch.Elapsed(now))
            : DateAndTime(now);
        if (_status.Text == text) return;
        _status.Text = text;
        Island.DeckSetText(text);

        // The closed pill is as wide as what it says, within reason.
        _closed.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wide = Math.Clamp(Math.Ceiling(_closed.DesiredSize.Width) + 36, 120, 340);
        if (Math.Abs(wide - _closedWidth) < 1) return;
        _closedWidth = wide;
        if (!_expanded) Animate();
    }

    /// <summary>The date and/or time, in the clock and date layout both notches share.</summary>
    private string DateAndTime(DateTime now)
    {
        var (date, time) = DateTimeWords(_settings, now);
        return _settings.Get("Deck.Shows") switch
        {
            "time" => time,
            "date-time" => date + "  \u00B7  " + time,
            "time-date" => time + "  \u00B7  " + date,
            _ => date,
        };
    }

    /// <summary>The date and the time as the settings write them (the settings window shows them too).</summary>
    internal static (string Date, string Time) DateTimeWords(SettingsStore settings, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        string date;
        var style = settings.Get("Modules.DateStyle");
        var pattern = style == "custom" ? settings.Get("Modules.DateFormat").Trim() : style == "default" ? "" : style;
        if (pattern.Length == 0) pattern = "ddd d MMM";  // the second notch's usual, short layout
        try
        {
            date = now.ToString(pattern, culture);
        }
        catch (FormatException)
        {
            date = now.ToString("ddd d MMM", culture);
        }

        var seconds = settings.Get("Modules.ShowSeconds") == "true";
        var timePattern = settings.Get("Modules.ClockFormat") switch
        {
            "12h" => seconds ? "h:mm:ss tt" : "h:mm tt",
            "24h" => seconds ? "HH:mm:ss" : "HH:mm",
            _ => seconds ? culture.DateTimeFormat.LongTimePattern : culture.DateTimeFormat.ShortTimePattern,
        };
        return (date, now.ToString(timePattern, culture));
    }

    private static string Clock(TimeSpan time)
    {
        // Rounded up, so a countdown shows 0:01 until it is really over.
        var seconds = (long)Math.Ceiling(time.TotalSeconds - 0.05);
        if (seconds < 0) seconds = 0;
        return seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";
    }

    // ---------- Pages ----------

    private UIElement Build(Page page)
    {
        if (page.View is not null) return page.View;
        var was = _everySecond;
        _everySecond = page.Updates;
        try
        {
            page.View = page.Build();
        }
        finally
        {
            _everySecond = was;
        }
        page.View.RenderTransform = new TranslateTransform();
        return page.View;
    }

    /// <summary>Show a page. <paramref name="direction"/> is 1 when turning forward, -1 back, 0 for no slide.</summary>
    private void ShowTab(int index, int direction)
    {
        if (index < 0) index = 0;
        var page = _tabs[index];
        var view = Build(page);
        var same = ReferenceEquals(_body.Content, view);
        _tab = index;
        _everySecond = page.Updates;
        if (!same)
        {
            _body.Content = view;
            _body.ScrollToTop();
        }
        DrawDots();
        ShowNativePage(page);
        page.Shown?.Invoke();
        foreach (var update in page.Updates.ToArray()) update();
        if (same || direction == 0 || !_expanded) return;

        // A short slide and fade, in the direction the wheel turned.
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var time = TimeSpan.FromMilliseconds(170);
        ((TranslateTransform)view.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(18 * direction, 0, time) { EasingFunction = ease });
        view.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, time) { EasingFunction = ease });
    }

    /// <summary>The island's page dots: a column at the right edge, the current page a taller pill. Each can be clicked.</summary>
    private void DrawDots()
    {
        _dots.Children.Clear();
        for (var i = 0; i < _turnCount && i < _tabs.Count; i++)
        {
            var at = i;
            var chosen = i == _tab;
            var dot = new Border
            {
                Width = 4,
                Height = chosen ? 14 : 4,
                CornerRadius = new CornerRadius(2),
                Background = Ink,
                Opacity = chosen ? 0.85 : 0.28,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var hit = new Border { Child = dot, Padding = new Thickness(5, 2.5, 5, 2.5), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = _tabs[i].Name };
            System.Windows.Automation.AutomationProperties.SetName(hit, _tabs[i].Name);
            hit.MouseLeftButtonUp += (_, up) =>
            {
                up.Handled = true;
                ShowTab(at, at > _tab ? 1 : at < _tab ? -1 : 0);
            };
            _dots.Children.Add(hit);
        }
    }

    /// <summary>Whether something under the pointer can still scroll that way (so the wheel scrolls it rather than turning the page).</summary>
    private bool CanScrollUnderPointer(bool down)
    {
        DependencyObject? at = Mouse.DirectlyOver as DependencyObject;
        while (at is not null && !ReferenceEquals(at, _pill))
        {
            if (at is ScrollViewer viewer && viewer.ScrollableHeight > 0.5 && (down ? viewer.VerticalOffset < viewer.ScrollableHeight - 0.5 : viewer.VerticalOffset > 0.5)) return true;
            at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) ?? LogicalTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at);
        }
        return false;
    }

    /// <summary>A page's title, with whatever belongs beside it on the right.</summary>
    private static DockPanel PageTitle(string text, params UIElement[] right)
    {
        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 10), LastChildFill = true };
        foreach (var item in right.Reverse())
        {
            DockPanel.SetDock(item, Dock.Right);
            header.Children.Add(item);
        }
        var title = Words(text, 13, strong: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        return header;
    }

    /// <summary>A small rounded box with words in it, as the Windows 11 controls are; lit when <paramref name="chosen"/>.</summary>
    private static Border Chip(string text, bool chosen, Action press, string? tip = null, string? glyph = null)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null) label.Children.Add(new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, text.Length > 0 ? 5 : 0, 0), Foreground = chosen ? Ink : Muted });
        if (text.Length > 0) label.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal, Foreground = chosen ? Ink : Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 });
        var chip = new Border { Child = label, Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 5, 5), CornerRadius = new CornerRadius(6), Background = chosen ? RaisedStrong : Raised, Cursor = Cursors.Hand, Focusable = true, ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(chip, text.Length > 0 ? text : tip ?? "");
        if (!chosen)
        {
            chip.MouseEnter += (_, _) => chip.Background = RaisedStrong;
            chip.MouseLeave += (_, _) => chip.Background = Raised;
        }
        OnPress(chip, press);
        return chip;
    }

    /// <summary>A large glyph with a line under it, for a page with nothing on it yet.</summary>
    private static FrameworkElement Empty(string glyph, string text, double height = 150)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        words.Children.Add(new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = 30, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center });
        words.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Margin = new Thickness(0, 10, 0, 0) });
        return new Grid { Height = height, Children = { words } };
    }

    // ----- Small pieces the tabs are made of -----

    private static TextBlock Words(string text, double size = 12.5, bool strong = false, Brush? colour = null) =>
        new() { Text = text, FontSize = size, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, Foreground = colour ?? Ink, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Muted, Margin = new Thickness(2, 10, 0, 6) };

    private static Border Card(UIElement child, Thickness? margin = null) =>
        new() { Child = child, Background = Raised, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = margin ?? new Thickness(0, 0, 0, 6) };

    private static Button Push(string text, Action click, string? tip = null)
    {
        var button = new Button { Content = text, FontSize = 12, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
        button.Click += (_, _) => click();
        return button;
    }

    // ---------- Apps ----------

    /// <summary>The running apps, each noted as seen now so it can be offered under Recent once it is closed.</summary>
    private List<ActiveApp> NoteActiveApps()
    {
        var active = Native.ActiveApps();
        _lastActive = active;
        var added = false;
        foreach (var app in active)
        {
            if (app.Id is not null) added |= _recentApps.Seen(app.Name, app.Id, DateTime.Now, app.Launch, app.IconSource);
        }
        if (added || ++_appTicks % 12 == 0) _recentApps.Save();
        return active;
    }

    /// <summary>What the Apps tab would draw for these apps, so it is only redrawn when that changes.</summary>
    private static string AppsSignature(List<ActiveApp> active) =>
        string.Join("\n", active.Select(a => $"{a.Id}|{a.Name}|{a.IconSource}|{a.Windows.Count}|{a.Windows[0].Title}"));

    /// <summary>Every few seconds: note what is running, and bring an open Apps tab up to date (a page's icon arrives a moment after its window).</summary>
    private void WatchApps()
    {
        var active = NoteActiveApps();
        // The Apps page (the island's) gets the new list with its next update.
        if (_expanded && _tabs[_tab].Native == "apps" && AppsSignature(active) != _appsShown) SendNativeData();
    }

    private static FrameworkElement IconOrLetter(string? path, string name, double size) => IconOrLetter(Native.Icon(path), name, size);

    private static FrameworkElement IconOrLetter(ImageSource? icon, string name, double size)
    {
        if (icon is not null)
        {
            var image = new Image { Source = icon, Width = size, Height = size };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(4),
            Background = Raised,
            Child = new TextBlock { Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?", FontSize = size * 0.6, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }

    // ---------- Tray: the file tray, shared with the island ----------

    private UIElement TrayTab()
    {
        // Laid out as the island's own File Tray: a title with the count and a bin, then the files,
        // or a dashed place to drop them when there are none.
        var panel = new StackPanel();

        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 8) };
        var clear = TrayButton("bin", 24, "Empty the tray", "Empty the tray (the files themselves are not touched)");
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.Add(clear);
        var paste = TrayButton("paste", 24, "Paste into the tray", "Paste what you copied into the tray: files, or text");
        paste.Margin = new Thickness(0, 0, 4, 0);
        DockPanel.SetDock(paste, Dock.Right);
        header.Children.Add(paste);
        var count = Words("", 11.5, colour: Muted);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        var title = Words("File Tray", 13, strong: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        panel.Children.Add(header);

        var list = new StackPanel();
        var shown = "";
        void Draw()
        {
            var items = Island.Tray();
            // Redrawn only when the tray has changed (the island can change it too).
            var now = string.Join("\n", items.Select(i => i.Kind + i.Path + i.Name + i.Text.Length));
            if (now == shown && list.Children.Count > 0) return;
            shown = now;
            list.Children.Clear();
            count.Text = items.Count == 0 ? "" : items.Count == 1 ? "1 item" : $"{items.Count} items";
            clear.IsEnabled = items.Count > 0;
            if (items.Count == 0)
            {
                list.Children.Add(DropHere());
                return;
            }
            // Newest first.
            foreach (var item in Enumerable.Reverse(items))
            {
                // The island's row: one raised card 30 high, with the icon, the name, the size and the cross all inside it.
                var isText = item.Kind == "T";
                var line = new DockPanel();
                if (item.Path.Length > 0)
                {
                    var remove = TrayButton("remove", 18, "Remove " + item.Name, "Take out of the tray (the file itself is not touched)");
                    remove.Margin = new Thickness(4, 0, 7, 0);
                    OnPress(remove, () =>
                    {
                        Island.TrayRemove(item.Path);
                        Draw();
                    });
                    DockPanel.SetDock(remove, Dock.Right);
                    line.Children.Add(remove);
                }
                var size = new TextBlock { Text = TraySize(item), FontSize = 11.5, Foreground = Muted, Opacity = 0.9, Width = 66, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, item.Path.Length > 0 ? 0 : 11, 0) };
                DockPanel.SetDock(size, Dock.Right);
                line.Children.Add(size);
                FrameworkElement icon = isText
                    ? new TextBlock { Text = "", FontFamily = Glyphs, FontSize = 14, Foreground = Accent, Opacity = 0.85, Width = 18, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    : IconOrLetter(item.Path, item.Name, 18);
                icon.Margin = new Thickness(8, 0, 6, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(icon, Dock.Left);
                line.Children.Add(icon);
                line.Children.Add(new TextBlock { Text = isText ? item.Text.ReplaceLineEndings(" ") : item.Name, FontSize = 12, Foreground = Ink, Opacity = 0.94, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

                var row = new Border
                {
                    Child = line,
                    Height = 30,
                    CornerRadius = new CornerRadius(9),
                    Background = Raised,
                    Margin = new Thickness(0, 0, 0, 6),
                    Cursor = Cursors.Hand,
                    Focusable = true,
                    ToolTip = isText ? "Copy this text" : item.Path + "\nClick to open, drag to drop it somewhere",
                };
                System.Windows.Automation.AutomationProperties.SetName(row, isText ? "Copied text" : item.Name);
                row.MouseEnter += (_, _) => row.Background = RaisedStrong;
                row.MouseLeave += (_, _) => row.Background = Raised;
                OnPress(row, () =>
                {
                    if (isText) Clipboard.SetText(item.Text);
                    else Native.Open(item.Path);
                });
                // Dragging a file out of the tray hands the file itself to wherever it is dropped.
                var pressedAt = new Point();
                row.PreviewMouseLeftButtonDown += (_, press) => pressedAt = press.GetPosition(this);
                row.PreviewMouseMove += (_, move) =>
                {
                    if (isText || move.LeftButton != MouseButtonState.Pressed) return;
                    var moved = move.GetPosition(this) - pressedAt;
                    if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    DragDrop.DoDragDrop(row, new DataObject(DataFormats.FileDrop, new[] { item.Path }), DragDropEffects.Copy | DragDropEffects.Link);
                };
                list.Children.Add(row);
            }
        }
        OnPress(clear, () =>
        {
            Island.TrayClear();
            Draw();
        });
        OnPress(paste, () =>
        {
            Island.TrayPaste();
            Draw();
        });
        panel.Children.Add(list);
        Draw();
        _everySecond.Add(Draw);
        return panel;
    }

    private static readonly Color Danger = Color.FromRgb(0xFF, 0x61, 0x5C);

    /// <summary>
    /// One of the File Tray's small buttons, drawn stroke for stroke as the island draws them:
    /// "bin" (empty the tray), "paste" (a clipboard) or "remove" (a small cross).
    /// Quiet until the pointer is on it; then the bin and the cross turn red and the clipboard takes the accent.
    /// </summary>
    private static Border TrayButton(string kind, double size, string name, string tip)
    {
        var rest = kind == "remove" ? Color.FromArgb(0x8C, Muted.Color.R, Muted.Color.G, Muted.Color.B) : Muted.Color;
        var lit = kind == "paste" ? Accent.Color : Danger;
        var ink = new SolidColorBrush(rest);
        var round = new Func<double, Shape, Shape>((width, shape) =>
        {
            shape.Stroke = ink;
            shape.StrokeThickness = width;
            shape.StrokeStartLineCap = shape.StrokeEndLineCap = PenLineCap.Round;
            shape.StrokeLineJoin = PenLineJoin.Round;
            return shape;
        });
        var s = size;
        var c = size / 2;
        Shape Stroke(double width, double x1, double y1, double x2, double y2) =>
            round(width, new Line { X1 = c + x1 * s, Y1 = c + y1 * s, X2 = c + x2 * s, Y2 = c + y2 * s });
        Rectangle Box(double left, double top, double right, double bottom, double radius)
        {
            var box = new Rectangle { Width = (right - left) * s, Height = (bottom - top) * s, RadiusX = radius * s, RadiusY = radius * s };
            Canvas.SetLeft(box, c + left * s);
            Canvas.SetTop(box, c + top * s);
            return box;
        }

        var art = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        if (kind == "bin")
        {
            var w = Math.Max(1.2, 0.075 * s);
            // Lid and handle.
            art.Children.Add(Stroke(w, -0.31, -0.17, 0.31, -0.17));
            art.Children.Add(round(w, Box(-0.11, -0.30, 0.11, -0.17, 0.04)));
            // Tapered body.
            art.Children.Add(round(w, new Polyline
            {
                Points = [new Point(c - 0.23 * s, c - 0.07 * s), new Point(c - 0.18 * s, c + 0.30 * s), new Point(c + 0.18 * s, c + 0.30 * s), new Point(c + 0.23 * s, c - 0.07 * s)],
            }));
            // Slats.
            art.Children.Add(Stroke(w * 0.85, -0.08, 0.03, -0.08, 0.20));
            art.Children.Add(Stroke(w * 0.85, 0.08, 0.03, 0.08, 0.20));
        }
        else if (kind == "paste")
        {
            var w = Math.Max(1.2, 0.075 * s);
            // Board, clip, and two lines of text.
            art.Children.Add(round(w, Box(-0.25, -0.24, 0.25, 0.31, 0.07)));
            var clip = Box(-0.11, -0.32, 0.11, -0.15, 0.05);
            clip.Fill = ink;
            art.Children.Add(clip);
            art.Children.Add(Stroke(w * 0.85, -0.12, 0.00, 0.12, 0.00));
            art.Children.Add(Stroke(w * 0.85, -0.12, 0.14, 0.05, 0.14));
        }
        else
        {
            var w = Math.Max(1.2, s * 0.085);
            art.Children.Add(Stroke(w, -0.22, -0.22, 0.22, 0.22));
            art.Children.Add(Stroke(w, -0.22, 0.22, 0.22, -0.22));
        }

        var button = new Border
        {
            Child = art,
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(kind == "remove" ? size / 2 : size * 0.32),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = tip,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        void Light(bool on)
        {
            ink.Color = on ? lit : rest;
            button.Background = on ? new SolidColorBrush(Color.FromArgb(kind == "remove" ? (byte)0x38 : (byte)0x33, lit.R, lit.G, lit.B)) : Brushes.Transparent;
        }
        button.MouseEnter += (_, _) => Light(true);
        button.MouseLeave += (_, _) => Light(button.IsKeyboardFocused);
        button.GotKeyboardFocus += (_, _) => Light(true);
        button.LostKeyboardFocus += (_, _) => Light(button.IsMouseOver);
        button.IsEnabledChanged += (_, _) =>
        {
            button.Opacity = button.IsEnabled ? 1 : 0.35;
            if (!button.IsEnabled) Light(false);
        };
        return button;
    }

    /// <summary>Run something when a drawn button is clicked, or pressed with Enter or Space.</summary>
    private static void OnPress(Border button, Action press)
    {
        button.MouseLeftButtonUp += (_, up) =>
        {
            up.Handled = true;
            press();
        };
        button.KeyDown += (_, key) =>
        {
            if (key.Key is not (Key.Enter or Key.Space)) return;
            key.Handled = true;
            press();
        };
    }

    /// <summary>The empty tray: a dashed place to drop files, a big glyph in the middle and the hint under it.</summary>
    private static FrameworkElement DropHere()
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        words.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs, FontSize = 34, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center });
        words.Children.Add(new TextBlock { Text = "Drag files here", FontSize = 13, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        var box = new Grid { Height = 150 };
        box.Children.Add(new Rectangle { RadiusX = 12, RadiusY = 12, Stroke = RaisedStrong, StrokeThickness = 1.4, StrokeDashArray = [4, 3], StrokeDashCap = PenLineCap.Round, SnapsToDevicePixels = true });
        box.Children.Add(words);
        return box;
    }

    /// <summary>What the island's tray writes at the end of a row: a file's size, a dash for a folder, the length of a text.</summary>
    private static string TraySize(TrayItem item)
    {
        if (item.Kind == "T") return $"{item.Text.Length} ch";
        if (item.Kind == "D") return "—";
        try
        {
            var bytes = new FileInfo(item.Path).Length;
            return bytes >= 1073741824 ? $"{bytes / 1073741824.0:0.0} GB"
                : bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB"
                : bytes >= 1024 ? $"{bytes / 1024.0:0} KB"
                : $"{bytes} B";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "";
        }
    }

}

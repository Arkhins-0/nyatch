using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// The settings window: a calm work tool (the ClickUp / Linear family) with a quiet sidebar, hairline
/// cards, text in three greys and one accent. Light, dark and black themes.
///
/// What is on each page comes from <see cref="SettingsLayout"/>: every setting is in one place only.
/// A page is a title, an optional picture of what its settings change, and sections of rows. Pages
/// with something of their own (the page lists of the two notches, the hotkeys, the assistants) draw
/// it in their own file. A change applies straight away.
/// </summary>
internal sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly ChatHistory _history;
    private readonly Updater _updater;
    private readonly Action _changed;
    private readonly StackPanel _nav = new();
    private readonly TextBox _search = new();
    private readonly StackPanel _content = new();
    private readonly ScrollViewer _scroll = new();
    private LayoutPage _page = SettingsLayout.Pages[0];
    private string _historyFilter = "";

    /// <summary>Draws the page's picture again after one of its settings changes.</summary>
    private Action? _refreshPicture;

    /// <summary>The sections drawn on the page so far, for the "on this page" row.</summary>
    private readonly List<(string Title, FrameworkElement Head)> _anchors = [];

    // The theme's colours, by role (set in Paint()).
    private readonly SolidColorBrush _pageBack = new(Colors.White);
    private readonly SolidColorBrush _panel = new(Colors.White);
    private readonly SolidColorBrush _high = new(Color.FromRgb(0xF7, 0xF8, 0xFA));
    private readonly SolidColorBrush _rail = new(Color.FromRgb(0xF9, 0xFA, 0xFB));
    private readonly SolidColorBrush _line = new(Color.FromRgb(0xE7, 0xE9, 0xEE));
    private readonly SolidColorBrush _layerLine = new(Color.FromRgb(0xE5, 0xE5, 0xE5));
    private readonly SolidColorBrush _text = new(Color.FromRgb(0x1E, 0x20, 0x28));
    private readonly SolidColorBrush _soft = new(Color.FromRgb(0x52, 0x57, 0x66));
    private readonly SolidColorBrush _faint = new(Color.FromRgb(0x80, 0x85, 0x94));
    private readonly SolidColorBrush _accent = new(Color.FromRgb(0x7B, 0x68, 0xEE));
    private readonly SolidColorBrush _accentDeep = new(Color.FromRgb(0x6A, 0x57, 0xE0));
    private readonly SolidColorBrush _accentTint = new(Color.FromArgb(0x1A, 0x7B, 0x68, 0xEE));
    private readonly SolidColorBrush _onAccent = new(Colors.White);
    private readonly SolidColorBrush _danger = new(Color.FromRgb(0xDC, 0x35, 0x45));

    private static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly FontFamily Body = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily Numbers = new("Segoe UI Variable Display, Segoe UI");
    private static readonly DependencyProperty AutomationName = System.Windows.Automation.AutomationProperties.NameProperty;

    // Icons (Segoe Fluent Icons), by what they mean here.
    private const string IconSearch = "", IconReset = "", IconDelete = "", IconChevron = "", IconLock = "",
        IconGrip = "", IconClear = "", IconAdd = "", IconHistory = "";

    // The two button styles: the accent for the one main action, outlined for the rest.
    private const string ButtonStyles = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="Main" TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource NyOnAccent}"/>
            <Setter Property="Background" Value="{DynamicResource NyAccent}"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Height" Value="32"/>
            <Setter Property="Padding" Value="14,0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="face" Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}" BorderThickness="2" BorderBrush="Transparent">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="False"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="face" Property="Background" Value="{DynamicResource NyAccentDeep}"/></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="face" Property="BorderBrush" Value="{DynamicResource NyAccentTint}"/></Trigger>
                    <Trigger Property="IsEnabled" Value="False"><Setter TargetName="face" Property="Opacity" Value="0.45"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="Ghost" TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource NyText}"/>
            <Setter Property="Background" Value="{DynamicResource NyPanel}"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="FontWeight" Value="Normal"/>
            <Setter Property="Height" Value="32"/>
            <Setter Property="Padding" Value="12,0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="face" Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}" BorderThickness="1" BorderBrush="{DynamicResource NyLine}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="False"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="face" Property="Background" Value="{DynamicResource NyHigh}"/></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="face" Property="BorderBrush" Value="{DynamicResource NyAccent}"/></Trigger>
                    <Trigger Property="IsEnabled" Value="False"><Setter TargetName="face" Property="Opacity" Value="0.45"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <!-- A bare text field, for fields that draw their own frame. -->
          <Style x:Key="BareField" TargetType="TextBox">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                  <ScrollViewer x:Name="PART_ContentHost" Background="Transparent" VerticalAlignment="Center" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;

    public SettingsWindow(SettingsStore settings, ChatHistory history, Updater updater, Action changed)
    {
        _settings = settings;
        _history = history;
        _updater = updater;
        _changed = changed;

        // A conversation saved, or an update found, while its page is open appears on it.
        void Refresh(string page) => Dispatcher.BeginInvoke(() =>
        {
            if (_page.Id == page && _search.Text.Trim().Length == 0) ShowPage(keepScroll: true);
        });
        void HistoryChanged() => Refresh("history");
        void UpdatesChanged() => Refresh("updates");
        history.Changed += HistoryChanged;
        updater.Changed += UpdatesChanged;
        Closed += (_, _) =>
        {
            history.Changed -= HistoryChanged;
            updater.Changed -= UpdatesChanged;
        };

        Title = "Nyatch settings";
        Width = Math.Min(1100, Math.Max(760, SystemParameters.WorkArea.Width - 40));
        Height = Math.Min(760, Math.Max(500, SystemParameters.WorkArea.Height - 40));
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/nyatch.ico"));
        FontFamily = Body;
        FontSize = 13;
        Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(ButtonStyles));
        Paint();

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        Content = layout;

        // ----- The sidebar: the name, search, the pages in groups -----
        var side = new DockPanel { Margin = new Thickness(8, 12, 8, 12) };
        layout.Children.Add(new Border { Background = _rail, Child = side });

        var brand = new DockPanel { Margin = new Thickness(10, 2, 4, 16) };
        var logo = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/nyatch-logo.png")), Width = 20, Height = 20, Margin = new Thickness(0, 0, 12, 0) };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        DockPanel.SetDock(logo, Dock.Left);
        brand.Children.Add(logo);
        var version = new TextBlock { Text = updater.Current.ToString(), Foreground = _faint, FontSize = 11.5, FontFamily = Numbers, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(version, Dock.Right);
        brand.Children.Add(version);
        brand.Children.Add(new TextBlock { Text = "Nyatch", FontSize = 12, Foreground = _text, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(brand, Dock.Top);
        side.Children.Add(brand);

        _search.SetValue(AutomationName, "Search settings");
        _search.TextChanged += (_, _) => ShowPage();
        var searchBox = Field(_search, IconSearch, "Search settings");
        searchBox.Margin = new Thickness(4, 0, 4, 12);
        DockPanel.SetDock(searchBox, Dock.Top);
        side.Children.Add(searchBox);
        // The pane scrolls with the wheel; its bar stays out of sight, as in Windows' own panes.
        side.Children.Add(new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false });

        // ----- The page: a centred column on a layer, its top-left corner round (as Windows' settings apps) -----
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.Background = Brushes.Transparent;
        _scroll.Focusable = false;
        _content.MaxWidth = 1000;
        _content.Margin = new Thickness(40, 32, 40, 40);
        _scroll.Content = _content;
        // A text or key box under the pointer does not stop the page scrolling: WPF's boxes keep the wheel
        // even with nothing to scroll. Only a box being typed in that has more to scroll keeps it.
        _scroll.PreviewMouseWheel += (_, wheel) =>
        {
            DependencyObject? at = wheel.OriginalSource as DependencyObject;
            while (at is not null && at is not System.Windows.Controls.Primitives.TextBoxBase && at is not PasswordBox && !ReferenceEquals(at, _scroll))
            {
                at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at);
            }
            if (at is not (System.Windows.Controls.Primitives.TextBoxBase or PasswordBox)) return;
            if (at is TextBox { IsKeyboardFocusWithin: true, AcceptsReturn: true } box && box.ExtentHeight > box.ViewportHeight + 1)
            {
                var down = wheel.Delta < 0;
                if (down ? box.VerticalOffset + box.ViewportHeight < box.ExtentHeight - 1 : box.VerticalOffset > 0) return;  // it scrolls itself
            }
            wheel.Handled = true;
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - wheel.Delta);
        };
        var layer = new Border { Child = _scroll, Background = _pageBack, BorderBrush = _layerLine, BorderThickness = new Thickness(1, 1, 0, 0), CornerRadius = new CornerRadius(8, 0, 0, 0), Margin = new Thickness(0, 12, 0, 0) };
        layer.SizeChanged += (_, _) => _scroll.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, layer.ActualWidth - 1), Math.Max(0, layer.ActualHeight - 1)), 7, 7);
        Grid.SetColumn(layer, 1);
        layout.Children.Add(layer);

        // Ctrl+F (or Ctrl+K) goes to the search box; Esc leaves a search.
        PreviewKeyDown += (_, key) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && key.Key is Key.F or Key.K)
            {
                key.Handled = true;
                _search.Focus();
                _search.SelectAll();
            }
            else if (key.Key == Key.Escape && _search.IsKeyboardFocused && _search.Text.Length > 0)
            {
                key.Handled = true;
                _search.Text = "";
            }
        };

        ShowPage();
    }

    /// <summary>Show a page by its id or name ("updates", "Shortcuts", "Keys"...), leaving any search.</summary>
    public void OpenPage(string name)
    {
        if (PageNamed(name) is not { } page) return;
        _page = page;
        if (_search.Text.Length > 0) _search.Text = "";
        else ShowPage();
    }

    /// <summary>Show a page again if it is the one open (its contents changed elsewhere).</summary>
    public void RefreshPage(string name)
    {
        if (PageNamed(name) is { } page && page.Id == _page.Id && _search.Text.Trim().Length == 0) ShowPage(keepScroll: true);
    }

    /// <summary>A page by its id, its title or its name; names from before the pages were rearranged still work.</summary>
    private static LayoutPage? PageNamed(string name)
    {
        var id = name.Trim().ToLowerInvariant() switch
        {
            "keys" or "assistant" or "assistants" => "chats",
            "pages" or "second notch pages" => "deck-pages",
            "file explorer" => "explorer",
            var other => other,
        };
        return SettingsLayout.Page(id)
            ?? SettingsLayout.Pages.FirstOrDefault(p => p.Title.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? SettingsLayout.Pages.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string GlyphFor(LayoutPage page) => page.Id switch
    {
        "characters" => "\uE76E",
        "appearance" => "",
        "motion" => "",
        "clock" => "",
        "top-layout" or "deck-layout" or "side-layout" => "",
        "top-pages" or "deck-pages" => "",
        "media" => "",
        "alerts" => "",
        "tools" or "deck-tools" => "",
        "general" => "",
        "shortcuts" => "",
        "deck-files" => "",
        "chats" => "",
        "history" => IconHistory,
        "explorer" => "",
        "updates" => "",
        "about" => "",
        "terms" => "",
        "notice" => "",
        _ => "",
    };

    // ---------- Theme ----------

    /// <summary>Light, dark or black, as chosen on the General page (or as Windows has it).</summary>
    private void Paint()
    {
        var choice = _settings.Get("General.SettingsTheme");
        if (choice is not ("light" or "dark" or "black"))
        {
            using var personalize = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            choice = personalize?.GetValue("AppsUseLightTheme") is int light && light == 0 ? "dark" : "light";
        }
        static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        var (page, panel, high, rail, line, layerLine, text, soft, faint) = choice switch
        {
            "dark" => ("#272727", "#2D2D2D", "#323232", "#202020", "#1D1D1D", "#1C1C1C", "#FFFFFF", "#C5C5C5", "#9A9A9A"),
            "black" => ("#000000", "#111111", "#1A1A1A", "#000000", "#242424", "#1A1A1A", "#FFFFFF", "#BDBDBD", "#8E8E8E"),
            _ => ("#F9F9F9", "#FFFFFF", "#F6F6F6", "#F3F3F3", "#E5E5E5", "#E5E5E5", "#1A1A1A", "#5D5D5D", "#8A8A8A"),
        };
        // The accent is Windows' own, in the shade its apps use on this background.
        var isLight = choice == "light";
        var (accent, deep) = WindowsAccent(isLight);
        _pageBack.Color = C(page);
        _panel.Color = C(panel);
        _high.Color = C(high);
        _rail.Color = C(rail);
        _line.Color = C(line);
        _layerLine.Color = C(layerLine);
        _text.Color = C(text);
        _soft.Color = C(soft);
        _faint.Color = C(faint);
        _accent.Color = accent;
        _accentDeep.Color = deep;
        _onAccent.Color = isLight ? Colors.White : Colors.Black;
        _accentTint.Color = Color.FromArgb(isLight ? (byte)0x1A : (byte)0x2E, _accent.Color.R, _accent.Color.G, _accent.Color.B);
        Background = _rail;
        Foreground = _text;
        ThemeMode = choice == "light" ? ThemeMode.Light : ThemeMode.Dark;

        // The resources get copies, made fresh on every change. A brush handed to the built-in control
        // styles is frozen when those styles are sealed (the dark theme does it), and the next change of
        // theme would then fail re-colouring it. This window's own controls keep the live brushes above.
        void Use(string key, object value) => Resources[key] = value is SolidColorBrush brush ? new SolidColorBrush(brush.Color) : value;
        Use("NyText", _text);
        Use("NyPanel", _panel);
        Use("NyHigh", _high);
        Use("NyLine", _line);
        Use("NyAccent", _accent);
        Use("NyAccentDeep", _accentDeep);
        Use("NyAccentTint", _accentTint);
        Use("NyOnAccent", _onAccent);
        // The stock controls (lists, text boxes, sliders) take the same colours, corners and lines.
        Use("TextFillColorPrimaryBrush", _text);
        Use("TextFillColorSecondaryBrush", _soft);
        Use("TextFillColorTertiaryBrush", _faint);
        Use("ControlFillColorDefaultBrush", _panel);
        Use("ControlFillColorSecondaryBrush", _high);
        Use("ControlFillColorTertiaryBrush", _high);
        Use("ControlFillColorInputActiveBrush", _panel);
        Use("ControlStrokeColorDefaultBrush", _line);
        Use("ControlStrokeColorSecondaryBrush", _line);
        Use("ControlElevationBorderBrush", _line);
        Use("TextControlElevationBorderBrush", _line);
        Use("TextControlElevationBorderFocusedBrush", _accent);
        Use("TextControlBorderBrushFocused", _accent);
        Use("CardBackgroundFillColorDefaultBrush", _panel);
        Use("CardStrokeColorDefaultBrush", _line);
        Use("FlyoutBackgroundFillColorDefaultBrush", _panel);
        Use("AcrylicBackgroundFillColorDefaultBrush", _panel);
        Use("SolidBackgroundFillColorBaseBrush", _pageBack);
        Use("SubtleFillColorSecondaryBrush", _high);
        Use("SubtleFillColorTertiaryBrush", _high);
        Use("AccentFillColorDefaultBrush", _accent);
        Use("AccentFillColorSecondaryBrush", _accentDeep);
        Use("AccentFillColorTertiaryBrush", _accentDeep);
        Use("AccentTextFillColorPrimaryBrush", _accent);
        Use("TextOnAccentFillColorPrimaryBrush", _onAccent);
        Use("SystemAccentColor", _accent.Color);
        Use("SystemAccentColorLight1", _accent.Color);
        Use("SystemAccentColorLight2", _accent.Color);
        Use("SystemAccentColorDark1", _accentDeep.Color);
        Use("ControlCornerRadius", new CornerRadius(4));
        Use("OverlayCornerRadius", new CornerRadius(8));
    }

    /// <summary>
    /// Windows' accent colour for this background, as its own apps take it: the lighter shade on a dark
    /// background, the deeper one on a light one; and the shade under the pointer.
    /// </summary>
    private static (Color Accent, Color Deep) WindowsAccent(bool light)
    {
        try
        {
            return light
                ? (SystemColors.AccentColorDark1, SystemColors.AccentColorDark2)
                : (SystemColors.AccentColorLight2, SystemColors.AccentColorLight1);
        }
        catch (Exception)
        {
            return light ? (Color.FromRgb(0x00, 0x5F, 0xB8), Color.FromRgb(0x00, 0x4C, 0x99)) : (Color.FromRgb(0x60, 0xCD, 0xFF), Color.FromRgb(0x4C, 0xC2, 0xFF));
        }
    }

    // ---------- Sidebar ----------

    // The groups that are open in the sidebar, and the page they were last opened for.
    private readonly HashSet<string> _navOpen = [];
    private string _navSeen = "";

    private static string GlyphFor(string group) => group switch
    {
        SettingsLayout.Both => "\uE771",
        SettingsLayout.Top => "\uE7F4",
        SettingsLayout.Second => "\uE7C4",
        SettingsLayout.Side => "\uE8BD",
        _ => "\uE713",
    };

    /// <summary>
    /// The sidebar, as Windows' navigation pane: each group is an item that opens to show its pages. The
    /// group of the page on show is opened; a closed group holding that page carries its mark.
    /// </summary>
    private void DrawNav()
    {
        _nav.Children.Clear();
        var searching = _search.Text.Trim().Length > 0;
        // Terms and Notice are reached from About, which stands for them here.
        var current = _page.Id is "terms" or "notice" ? "about" : _page.Id;
        if (_navSeen != _page.Id)
        {
            _navSeen = _page.Id;
            _navOpen.Add(_page.Group);
        }
        foreach (var group in SettingsLayout.Groups)
        {
            var pages = SettingsLayout.Pages.Where(p => p.Group == group && p.Id is not ("terms" or "notice")).ToList();
            var open = _navOpen.Contains(group);
            var holds = !searching && pages.Any(p => p.Id == current);
            _nav.Children.Add(NavLine(GlyphFor(group), group, group, chosen: holds && !open, child: false, open ? "\uE70E" : "\uE70D", () =>
            {
                if (!_navOpen.Remove(group)) _navOpen.Add(group);
                DrawNav();
            }));
            if (!open) continue;
            foreach (var page in pages)
            {
                _nav.Children.Add(NavLine(GlyphFor(page), page.Name, page.Title, !searching && page.Id == current, child: true, "", () =>
                {
                    _page = page;
                    // Choosing a page leaves the search.
                    if (_search.Text.Length > 0) _search.Text = "";
                    else ShowPage();
                }));
            }
        }
    }

    /// <summary>One line of the sidebar: an icon and a name, the accent mark when it is the one on show, a chevron for a group.</summary>
    private Border NavLine(string glyph, string name, string says, bool chosen, bool child, string chevron, Action press)
    {
        var line = new DockPanel { VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };
        var icon = new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = 16, Foreground = _text, VerticalAlignment = VerticalAlignment.Center, Width = 16, Margin = new Thickness(0, 0, 14, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        if (chevron.Length > 0)
        {
            var arrow = new TextBlock { Text = chevron, FontFamily = Glyphs, FontSize = 10, Foreground = _soft, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 4, 0) };
            DockPanel.SetDock(arrow, Dock.Right);
            line.Children.Add(arrow);
        }
        line.Children.Add(new TextBlock { Text = name, FontSize = 14, Foreground = _text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var whole = new Grid { Children = { line } };
        var inset = child ? 28.0 : 0.0;
        if (chosen) whole.Children.Add(new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), Background = _accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-12 - inset, 0, 0, 0) });
        var item = new Border { Child = whole, Height = 36, Padding = new Thickness(12 + inset, 0, 8, 0), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(4), Background = chosen ? _high : Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true };
        item.SetValue(AutomationName, says);
        if (chosen) item.SetValue(System.Windows.Automation.AutomationProperties.ItemStatusProperty, "current");
        else
        {
            item.MouseEnter += (_, _) => item.Background = _high;
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        }
        item.MouseLeftButtonUp += (_, _) => press();
        item.KeyDown += (_, key) =>
        {
            if (key.Key is not (Key.Enter or Key.Space)) return;
            key.Handled = true;
            press();
        };
        return item;
    }

    // ---------- Drawing a page ----------

    private void ShowPage(bool keepScroll = false)
    {
        var offset = _scroll.VerticalOffset;
        DrawNav();
        _content.Children.Clear();
        _anchors.Clear();
        _refreshPicture = null;
        var query = _search.Text.Trim();
        if (query.Length > 0) ShowSearch(query);
        else
        {
            switch (_page.Id)
            {
                case "appearance":
                    Head(_page);
                    Picture(() => Scene(top: true, second: true));
                    Sections(_page);
                    break;
                case "clock":
                    Head(_page);
                    Picture(ClockSample);
                    Sections(_page);
                    break;
                case "characters":
                    ShowCharacters();
                    break;
                case "top-layout":
                    Head(_page);
                    Picture(() => Scene(top: true, second: false));
                    Sections(_page);
                    break;
                case "deck-layout":
                    ShowDeckLayout();
                    break;
                case "side-layout":
                    Head(_page);
                    Sections(_page);
                    break;
                case "top-pages":
                    ShowNotchPages(top: true);
                    break;
                case "deck-pages":
                    ShowNotchPages(top: false);
                    break;
                case "shortcuts":
                    ShowShortcuts();
                    break;
                case "media":
                    Head(_page);
                    Picture(() => Stage(("Player", Small(TopPagePicture(PrimaryPage))), ("Lyrics", Small(LyricsPicture()))));
                    Sections(_page);
                    break;
                case "alerts":
                    Head(_page);
                    Picture(AlertsStage);
                    Sections(_page);
                    break;
                case "tools":
                    Head(_page);
                    Picture(ToolsStage);
                    Sections(_page);
                    break;
                case "deck-files":
                    Head(_page);
                    Picture(() => PageStage("apps", "tray", "clipboard"));
                    Sections(_page);
                    break;
                case "deck-tools":
                    Head(_page);
                    Picture(() => PageStage("focus", "terminal", "online"));
                    Sections(_page);
                    break;
                case "chats":
                    ShowChats();
                    break;
                case "about":
                    ShowAbout();
                    break;
                case "terms":
                    ShowTerms();
                    break;
                case "notice":
                    ShowNotice();
                    break;
                case "history":
                    ShowHistory();
                    break;
                case "updates":
                    ShowUpdates();
                    break;
                case "explorer":
                    Head(_page);
                    ShowExplorerStatus();
                    Sections(_page);
                    break;
                case "general":
                    Head(_page);
                    Sections(_page);
                    ShowBackup();
                    ShowHelp();
                    break;
                default:
                    Head(_page);
                    Sections(_page);
                    break;
            }
            ShowUnplaced();
        }
        if (keepScroll) _scroll.ScrollToVerticalOffset(offset);
        else _scroll.ScrollToTop();
    }

    /// <summary>The page's title and line, with "Reset page" while something on it differs from the defaults.</summary>
    private void Head(LayoutPage page, params UIElement[] actions)
    {
        var changed = page.Settings.Where(_settings.IsChanged).ToList();
        var all = actions.ToList();
        if (changed.Count > 0)
        {
            var reset = GhostButton("Reset page");
            reset.ToolTip = changed.Count == 1 ? "Put the one changed setting on this page back to its default" : $"Put the {changed.Count} changed settings on this page back to their defaults";
            reset.Click += (_, _) =>
            {
                var words = changed.Count == 1 ? "the one changed setting" : $"the {changed.Count} changed settings";
                if (MessageBox.Show(this, $"Put {words} on “{page.Title}” back to the defaults?", "Nyatch", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                foreach (var name in changed) _settings.Reset(name);
                Changed(page.Settings.Contains("General.SettingsTheme"));
                ShowPage();
            };
            all.Insert(0, reset);
        }
        PageStrip(page.Title, "", [.. all]);
    }

    /// <summary>The settings changed: the notches follow (and this window, when its own theme did).</summary>
    private void Changed(bool theme = false)
    {
        _changed();
        if (theme) Paint();
    }

    /// <summary>A page's sections of rows, with an "on this page" row first when there are many.</summary>
    private void Sections(LayoutPage page)
    {
        var sections = page.Sections.Where(s => s.Key.Length == 0).ToList();
        var jump = sections.Count >= 4 ? new WrapPanel { Margin = new Thickness(0, 0, 0, 18) } : null;
        if (jump is not null) _content.Children.Add(jump);
        foreach (var section in sections) Section(section);
        if (jump is null) return;
        foreach (var (title, head) in _anchors)
        {
            var chip = LinkChip(title, () => BringIntoView(head));
            chip.Margin = new Thickness(0, 0, 6, 6);
            jump.Children.Add(chip);
        }
    }

    /// <summary>One section: its heading and count, a card of rows, and its note.</summary>
    private void Section(LayoutSection section)
    {
        var settings = section.Settings.Select(_settings.Schema.Find).OfType<Setting>().ToList();
        if (settings.Count == 0) return;
        _anchors.Add((section.Title, SectionStrip(section.Title, settings.Count > 1 ? settings.Count : null)));
        var rows = Rows();
        foreach (var setting in settings) SettingRow(rows, setting, section.Settings);
        if (section.Note.Length > 0) Note(section.Note);
    }

    /// <summary>Scroll so that a section's heading is at the top.</summary>
    private void BringIntoView(FrameworkElement head)
    {
        var y = head.TranslatePoint(new Point(0, 0), _content).Y;
        _scroll.ScrollToVerticalOffset(Math.Max(0, y + _content.Margin.Top - 14));
    }

    /// <summary>A setting added to the island without a place yet still shows: at the end of General.</summary>
    private void ShowUnplaced()
    {
        if (_page.Id != "general") return;
        var loose = SettingsLayout.Unplaced(_settings.Schema).ToList();
        if (loose.Count == 0) return;
        SectionStrip("More", loose.Count);
        var rows = Rows();
        foreach (var setting in loose) SettingRow(rows, setting, []);
    }

    // ---------- Search ----------

    private void ShowSearch(string query)
    {
        bool Has(string text) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
        var total = 0;
        var found = new List<(LayoutPage Page, string Heading, List<Setting> Rows, List<Setting> Links)>();
        foreach (var page in SettingsLayout.Pages)
        {
            var pageHit = Has(page.Title) || Has(page.Name);
            foreach (var section in page.Sections)
            {
                var rows = section.Settings.Select(_settings.Schema.Find).OfType<Setting>()
                    .Where(s => pageHit || Has(section.Title) || Has(s.Name) || Has(s.Description) || s.Options.Any(o => Has(o.Label))).ToList();
                if (rows.Count == 0) continue;
                found.Add((page, section.Title, rows, []));
                total += rows.Count;
            }
            // Settings a page shows with a control of its own: a row that opens the page.
            var links = (page.Special ?? []).Select(_settings.Schema.Find).OfType<Setting>().Where(s => pageHit || Has(s.Name) || Has(s.Description)).ToList();
            if (links.Count > 0 || (pageHit && page.Sections.Length == 0 && (page.Special ?? []).Length == 0))
            {
                found.Add((page, "", [], links));
                total += Math.Max(1, links.Count);
            }
        }
        PageStrip("Search", total == 0 ? $"Nothing matches “{query}”." : $"{total} {(total == 1 ? "result" : "results")} for “{query}”.");
        if (total == 0)
        {
            EmptyState(IconSearch, "Nothing found", "Try another word.");
            return;
        }
        foreach (var (page, heading, rowSettings, links) in found)
        {
            var open = LinkChip("Open page", () => OpenPage(page.Id));
            SectionStrip(heading.Length > 0 ? $"{page.Title} · {heading}" : page.Title, rowSettings.Count + links.Count is var n && n > 0 ? n : null, open);
            var rows = Rows();
            foreach (var setting in rowSettings) SettingRow(rows, setting, []);
            foreach (var setting in links)
            {
                Row(rows, setting.Name, setting.Description, GoButton($"Open {page.Title}", () => OpenPage(page.Id)), hover: false);
            }
            if (rowSettings.Count + links.Count == 0)
            {
                Row(rows, page.Title, page.About, GoButton($"Open {page.Title}", () => OpenPage(page.Id)), hover: false);
            }
        }
    }
}

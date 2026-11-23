using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// Shown when a .nyatch file is opened: the pack, on its own colour, with its cover, what it is and some
/// of its pictures, and a button to install it (or to update the one already here). Nothing is installed
/// until Install is clicked.
/// </summary>
internal sealed class PackInstallWindow : Window
{
    private static readonly string[] Showcase = ["idle", "happy", "cool", "sleeping", "thinking", "dance"];

    /// <summary>Show the pack in a file and ask. True when Install was clicked.</summary>
    public static bool Ask(string file)
    {
        CharacterPacks.Peeked peek;
        try
        {
            peek = CharacterPacks.Peek(file, Showcase);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            MessageBox.Show($"{Path.GetFileName(file)} is not a theme Nyatch can read.\n\n{error.Message}", "Nyatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var here = new CharacterPacks().InstalledPack(peek.Pack.Id);
        return new PackInstallWindow(peek, here).ShowDialog() == true;
    }

    /// <summary>For checking: the popup for a pack file, drawn off screen to a PNG.</summary>
    public static void Shot(string file, string png)
    {
        var peek = CharacterPacks.Peek(file, Showcase);
        var window = new PackInstallWindow(peek, new CharacterPacks().InstalledPack(peek.Pack.Id))
        {
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
        };
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var root = (FrameworkElement)window.Content;
        var picture = new RenderTargetBitmap((int)(root.ActualWidth + root.Margin.Left + root.Margin.Right), (int)(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        picture.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using (var stream = File.Create(png)) encoder.Save(stream);
        window.Close();
    }

    private PackInstallWindow(CharacterPacks.Peeked peek, CharacterPack? here)
    {
        var pack = peek.Pack;
        Title = $"Install {pack.Name}";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = Brushes.White;
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/nyatch.ico"));
        }
        catch (Exception error) when (error is IOException or NotSupportedException or UriFormatException)
        {
        }

        // The pack's colour behind it all, darkened so white words read on it; a quiet grey without one.
        var accent = pack.AccentColour is { } hex ? (Color)ColorConverter.ConvertFromString(hex) : Color.FromRgb(0x6B, 0x6B, 0x78);
        var back = new LinearGradientBrush(Shade(accent, 0.5), Shade(accent, 0.16), new Point(0, 0), new Point(1, 1));
        var card = new Border
        {
            Background = back,
            CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(18),
            Padding = new Thickness(26, 24, 26, 22),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.45, Direction = 270 },
        };
        Content = card;
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        var all = new StackPanel();
        card.Child = all;

        all.Children.Add(new TextBlock { Text = "THEME", FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 14) });

        // ----- The cover, the name and what it is -----
        var top = new DockPanel();
        if (Picture(peek.Cover) is { } cover)
        {
            var image = new Image { Source = cover, Width = 132, Height = 132, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var frame = new Border { Child = image, Width = 132, Height = 132, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 20, 0), ClipToBounds = true };
            frame.Clip = new RectangleGeometry(new Rect(0, 0, 132, 132), 12, 12);
            DockPanel.SetDock(frame, Dock.Left);
            top.Children.Add(frame);
        }
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = pack.Name, FontSize = 28, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var facts = new List<string>();
        if (pack.Author.Length > 0) facts.Add("By " + pack.Author);
        facts.Add("Version " + pack.Version);
        facts.Add($"{peek.Pictures} pictures");
        facts.Add($"{peek.Size / 1024.0 / 1024.0:0.0} MB");
        words.Children.Add(new TextBlock { Text = string.Join("  ·  ", facts), FontSize = 13, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (pack.Description.Length > 0) words.Children.Add(new TextBlock { Text = pack.Description, FontSize = 14, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
        top.Children.Add(words);
        all.Children.Add(top);

        // ----- Some of its pictures -----
        if (peek.Shown.Count > 0)
        {
            var strip = new System.Windows.Controls.Primitives.UniformGrid { Columns = Math.Max(peek.Shown.Count, 3), Margin = new Thickness(0, 22, 0, 0) };
            foreach (var (name, bytes) in peek.Shown)
            {
                if (Picture(bytes) is not { } source) continue;
                var tile = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                var image = new Image { Source = source, Height = 72, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                tile.Children.Add(new Border
                {
                    Child = image,
                    Height = 84,
                    Padding = new Thickness(6),
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                });
                tile.Children.Add(new TextBlock { Text = name, FontSize = 11.5, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
                strip.Children.Add(tile);
            }
            all.Children.Add(strip);
        }


        // ----- Install or not -----
        var question = here is null ? "Install it, and use it now?"
            : here.Version == pack.Version ? $"Version {here.Version} is already installed. Install it again, and use it now?"
            : $"Version {here.Version} is installed. Update it to {pack.Version}, and use it now?";
        var bottom = new DockPanel { Margin = new Thickness(0, 22, 0, 0), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = Button("Cancel", filled: false, accent);
        cancel.Click += (_, _) => DialogResult = false;
        var install = Button(here is null ? "Install" : here.Version == pack.Version ? "Reinstall" : "Update", filled: true, accent);
        install.Click += (_, _) => DialogResult = true;
        install.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(install);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(new TextBlock { Text = question, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
        all.Children.Add(bottom);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
            else if (e.Key == Key.Enter) DialogResult = true;
        };
        Loaded += (_, _) => install.Focus();
    }

    /// <summary>A plain rounded button: white on the pack's colour, or a quiet outline.</summary>
    private static Button Button(string text, bool filled, Color accent)
    {
        var face = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(22, 8, 22, 9),
            Background = filled ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(filled ? 0 : 1),
            Child = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = filled ? new SolidColorBrush(Shade(accent, 0.45)) : Brushes.White },
        };
        var button = new Button { Content = face, Cursor = Cursors.Hand, Focusable = true, Template = Template() };
        button.MouseEnter += (_, _) => face.Opacity = 0.88;
        button.MouseLeave += (_, _) => face.Opacity = 1;
        return button;
    }

    private static ControlTemplate Template()
    {
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return template;
    }

    /// <summary>The first frame of a PNG or GIF, or null.</summary>
    private static BitmapSource? Picture(byte[]? bytes)
    {
        if (bytes is null) return null;
        try
        {
            var frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception error) when (error is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static Color Shade(Color colour, double keep) =>
        Color.FromRgb((byte)(colour.R * keep), (byte)(colour.G * keep), (byte)(colour.B * keep));
}

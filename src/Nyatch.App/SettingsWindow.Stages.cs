using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nyatch.App;

// The strips of pictures at the top of the feature pages: what the page's settings are about, drawn on
// the same wallpaper as the notches' scene. The second notch's pages are shown as they open; the top
// notch's features as the closed notch shows them.
internal sealed partial class SettingsWindow
{
    /// <summary>A page's picture, a little smaller, to sit beside others.</summary>
    private static FrameworkElement Small(FrameworkElement picture, double width = 200) => new Viewbox { Width = width, Child = picture };

    /// <summary>Pages of the second notch side by side, each under its name.</summary>
    private FrameworkElement PageStage(params string[] ids) =>
        Stage([.. ids.Select(id => (DeckPageChoices.First(c => c.Id == id).Name, Small(DeckPagePicture(id))))]);

    /// <summary>The lyrics page: the song, and its lines with the one being sung lit.</summary>
    private FrameworkElement LyricsPicture()
    {
        var accent = new SolidColorBrush(_accent.Color);
        var c = Notch();
        const double L = 14, T = 14;
        Box(c, L, T, 30, 30, Frozen("#7B5EA7"), 6);
        Words(c, L + 38, T + 6, 80, 6, PicInk);
        Words(c, L + 38, T + 18, 52, 4);
        double[] widths = [120, 150, 176, 132, 96];
        for (var i = 0; i < widths.Length; i++)
        {
            var now = i == 2;
            Words(c, (PicW - widths[i]) / 2, T + 46 + i * 18, widths[i], now ? 7 : 5, now ? PicInk : PicStrong);
        }
        // The words sung so far.
        Words(c, (PicW - widths[2]) / 2, T + 46 + 2 * 18, 104, 7, accent);
        return c;
    }

    /// <summary>Quick Lookup: the search box and what it found.</summary>
    private FrameworkElement LookupPicture()
    {
        var tint = new SolidColorBrush(Color.FromArgb(0x55, _accent.Color.R, _accent.Color.G, _accent.Color.B));
        var c = Notch();
        const double L = 14, T = 14, R = PicW - 14;
        Box(c, L, T, R - L, 26, PicRaised, 8);
        Glyph(c, IconSearch, L + 9, T + 8, 10, PicSoft);
        Words(c, L + 28, T + 11, 70, 5, PicInk);
        string[] icons = ["#4C8DF6", "#E8A33D", "#3BC46B", "#9B7BF2"];
        for (var i = 0; i < icons.Length; i++)
        {
            var y = T + 36 + i * 23;
            if (i == 0) Box(c, L, y, R - L, 20, tint, 6);
            Box(c, L + 6, y + 4, 12, 12, Frozen(icons[i]), 3);
            Words(c, L + 26, y + 8, 100 - i * 14, 4, PicInk);
            Words(c, R - 40, y + 8, 30);
        }
        return c;
    }

    /// <summary>The Alerts page's pictures: the closed top notch showing the volume, Caps Lock, and the mic and camera dots.</summary>
    private FrameworkElement AlertsStage()
    {
        var (_, ink, muted, _, accent) = NotchColours();
        var font = NotchFont();
        Brush Ink() => new SolidColorBrush(ink);

        var level = new Grid { Width = 86, Height = 4 };
        level.Children.Add(new Border { Background = new SolidColorBrush(muted), Opacity = 0.45, CornerRadius = new CornerRadius(2) });
        level.Children.Add(new Border { Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(2), Width = 54, HorizontalAlignment = HorizontalAlignment.Left });
        var volume = ClosedNotch(new TextBlock { Text = "", FontFamily = Glyphs, FontSize = 13, Foreground = Ink() }, level);

        var key = new Border
        {
            Child = new TextBlock { Text = "A", FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = Ink(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            BorderBrush = Ink(),
            BorderThickness = new Thickness(1.2),
        };
        var caps = ClosedNotch(key, new TextBlock { Text = "Caps Lock on", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink(), FontFamily = font });

        var dots = new StackPanel { Orientation = Orientation.Horizontal };
        dots.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(DeckWindow.Hex(_settings.Get("Indicators.PrivacyDotsMicHex"), Colors.Orange)), Margin = new Thickness(0, 0, 5, 0) });
        dots.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(DeckWindow.Hex(_settings.Get("Indicators.PrivacyDotsCamHex"), Colors.LimeGreen)) });
        var (_, time) = DeckWindow.DateTimeWords(_settings, System.DateTime.Now);
        var privacy = ClosedNotch(dots, new TextBlock { Text = time, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink(), FontFamily = font });

        return Stage(("Volume", volume), ("Caps Lock", caps), ("Mic and camera", privacy));
    }

    /// <summary>The top notch's Tools page's pictures: Quick Lookup open, and the game overlay in the closed notch.</summary>
    private FrameworkElement ToolsStage()
    {
        var (_, ink, muted, _, _) = NotchColours();
        var font = NotchFont();
        var numbers = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, value) in new[] { ("FPS", "144"), ("CPU", "32%"), ("GPU", "54%") })
        {
            numbers.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = new SolidColorBrush(muted), Margin = new Thickness(numbers.Children.Count == 0 ? 0 : 12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = font });
            numbers.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), VerticalAlignment = VerticalAlignment.Center, FontFamily = font });
        }
        var overlay = ClosedNotch(null, numbers);
        overlay.VerticalAlignment = VerticalAlignment.Center;
        return Stage(("Quick Lookup", Small(LookupPicture())), ("Game overlay", overlay));
    }
}

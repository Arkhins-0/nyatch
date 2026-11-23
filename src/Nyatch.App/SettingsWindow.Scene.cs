using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Nyatch.App;

// The pictures at the top of pages: a small screen with both notches drawn as the settings have them
// (theme, shape, opacity, outline, place), and the closed notches with the time and date as they will
// be written. They are drawn again as settings change, so a change is seen before looking up.
internal sealed partial class SettingsWindow
{
    private const double SceneW = 520, SceneH = 190;

    /// <summary>The notches' colours as the settings have them: the theme, with any colour replaced by the user's.</summary>
    private (Color Back, Color Text, Color Muted, Color Line, Color Accent) NotchColours()
    {
        var preset = _settings.Get("Themes.ThemePreset");
        var theme = DeckWindow.Palettes.GetValueOrDefault(preset, DeckWindow.Palettes["obsidian"]);
        Color Pick(string setting, string fromTheme) =>
            DeckWindow.Hex(preset == "custom" || _settings.IsChanged(setting) ? _settings.Get(setting) : fromTheme, DeckWindow.Hex(fromTheme, Colors.Black));
        var accent = _settings.Get("Themes.AccentColorMode") switch
        {
            "custom" => DeckWindow.Hex(_settings.Get("Themes.CustomAccentHex"), Color.FromRgb(0x4C, 0xC9, 0xF0)),
            "system" => SystemParameters.WindowGlassColor is { A: > 0 } glass ? Color.FromRgb(glass.R, glass.G, glass.B) : Color.FromRgb(0x4C, 0xC9, 0xF0),
            // From the album cover: a cover's warm tone stands in for it.
            _ => Color.FromRgb(0x4C, 0xC9, 0xF0),
        };
        return (Pick("Themes.PillBgColor", theme.Background), Pick("Themes.TextPrimaryColor", theme.Text),
            Pick("Themes.TextSecondaryColor", theme.Secondary), Pick("Themes.ContourBorderHex", theme.Border), accent);
    }

    /// <summary>The desktop the pictures of the notches sit on: a soft wallpaper, the same in every theme of this window.</summary>
    private static Brush Wallpaper() => new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0x2B, 0x2F, 0x5B), 0), new(Color.FromRgb(0x4A, 0x3B, 0x73), 0.45), new(Color.FromRgb(0x23, 0x4E, 0x66), 1),
    }, new Point(0, 0), new Point(1, 1));

    private int Whole(string setting) => int.TryParse(_settings.Get(setting), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>A notch's outline for the shape setting: flush to an edge, a Windows 11 box, or a pill.</summary>
    private static Geometry NotchShape(string shape, double x, double y, double w, double h, bool flushTop, bool flushBottom)
    {
        if (shape == "notch" && (flushTop || flushBottom))
        {
            var r = Math.Min(h / 2, 9);
            var geometry = new StreamGeometry();
            using (var pen = geometry.Open())
            {
                if (flushTop)
                {
                    pen.BeginFigure(new Point(x, y), true, true);
                    pen.LineTo(new Point(x + w, y), true, false);
                    pen.LineTo(new Point(x + w, y + h - r), true, false);
                    pen.ArcTo(new Point(x + w - r, y + h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    pen.LineTo(new Point(x + r, y + h), true, false);
                    pen.ArcTo(new Point(x, y + h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
                else
                {
                    pen.BeginFigure(new Point(x, y + h), true, true);
                    pen.LineTo(new Point(x, y + r), true, false);
                    pen.ArcTo(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    pen.LineTo(new Point(x + w - r, y), true, false);
                    pen.ArcTo(new Point(x + w, y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    pen.LineTo(new Point(x + w, y + h), true, false);
                }
            }
            geometry.Freeze();
            return geometry;
        }
        var radius = shape == "w11" ? Math.Min(h / 2, 5) : h / 2;
        return new RectangleGeometry(new Rect(x, y, w, h), radius, radius);
    }

    /// <summary>
    /// A small screen with the notches where the settings put them, in their colours and shape.
    /// A notch the page is not about is drawn faintly, for its place only.
    /// </summary>
    private FrameworkElement Scene(bool top, bool second)
    {
        var (back, ink, muted, edge, accent) = NotchColours();
        var shape = _settings.Get("Appearance.ShapeStyle");
        var opacity = Math.Clamp(Whole("Themes.PillOpacity"), 35, 100) / 100.0;
        var outline = _settings.Get("Themes.ContourBorderMode");
        var shadow = _settings.Get("Themes.DropShadow") == "true";
        var canvas = Canvas(SceneW, SceneH);

        // The desktop: a soft wallpaper and a taskbar.
        Place(canvas, new Rectangle { Width = SceneW, Height = SceneH, RadiusX = 12, RadiusY = 12, Fill = Wallpaper() }, 0, 0);
        Place(canvas, new Ellipse { Width = 260, Height = 260, Fill = new RadialGradientBrush(Color.FromArgb(0x55, 0xF2, 0x8F, 0xAD), Color.FromArgb(0, 0xF2, 0x8F, 0xAD)) }, SceneW - 250, -120);
        const double bar = 11;
        Place(canvas, new Rectangle { Width = SceneW, Height = bar, Fill = new SolidColorBrush(Color.FromArgb(0x66, 0x0B, 0x0C, 0x12)) }, 0, SceneH - bar);
        for (var i = 0; i < 5; i++) Place(canvas, new Rectangle { Width = 7, Height = 5, RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)) }, SceneW / 2 - 28 + i * 12, SceneH - bar + 3);
        canvas.Clip = new RectangleGeometry(new Rect(0, 0, SceneW, SceneH), 12, 12);

        // One notch: where it sits, then its surface, outline and what it says.
        Rect Draw(string where, double w, double h, double dx, double dy, bool faint, string caption, Rect? other, Action<Canvas, double, double> inside)
        {
            var atBottom = where.StartsWith("bottom", StringComparison.Ordinal);
            var flush = shape == "notch";
            var gap = flush ? 0 : 6;
            var x = where.EndsWith("left", StringComparison.Ordinal) ? 22 : where.EndsWith("right", StringComparison.Ordinal) ? SceneW - 22 - w : (SceneW - w) / 2;
            var y = atBottom ? SceneH - bar - h - gap : gap;
            // Both in the same place: the second keeps clear of the first.
            if (other is { } taken && Math.Abs(taken.X + taken.Width / 2 - (x + w / 2)) < (taken.Width + w) / 2 && Math.Abs(taken.Y - y) < Math.Max(taken.Height, h))
            {
                y = atBottom ? taken.Y - h - 6 : taken.Bottom + 6;
                flush = false;
            }
            x = Math.Clamp(x + dx, 2, SceneW - w - 2);
            y = Math.Clamp(y + dy, 0, SceneH - h);
            var movedOff = Math.Abs(dy) > 0.5 || (other is not null && !flush && shape == "notch");
            var outlineShape = NotchShape(shape, x, y, w, h, flush && !atBottom && !movedOff, flush && atBottom && !movedOff);
            Brush? stroke = outline switch
            {
                "borderless" => null,
                "auto" => new LinearGradientBrush(Color.FromRgb(0xFF, 0x6B, 0x5B), Color.FromRgb(0xF7, 0xC8, 0x48), 0),
                _ => new SolidColorBrush(edge),
            };
            var surface = new Path { Data = outlineShape, Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), back.R, back.G, back.B)), Stroke = stroke, StrokeThickness = 1, Opacity = faint ? 0.4 : 1 };
            if (shadow && !faint) surface.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Direction = 270, Opacity = 0.4, Color = Colors.Black };
            canvas.Children.Add(surface);
            var content = Canvas(w, h);
            content.Opacity = faint ? 0.4 : 1;
            inside(content, w, h);
            Place(canvas, content, x, y);
            var label = new TextBlock { Text = caption, FontSize = 9.5, Foreground = new SolidColorBrush(Color.FromArgb(faint ? (byte)0x77 : (byte)0xCC, 0xFF, 0xFF, 0xFF)), Width = 120, TextAlignment = TextAlignment.Center };
            Place(canvas, label, x + w / 2 - 60, atBottom ? y - 16 : y + h + 4);
            return new Rect(x, y, w, h);
        }

        const double pxToScene = SceneW / 1920;
        var scale = double.TryParse(_settings.Get("Appearance.SizeScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, 0.8, 2.5) : 1.0;
        var topW = Math.Min(118 * (0.7 + scale * 0.3), 190);
        var topH = Math.Min(24 * (0.7 + scale * 0.3), 38);
        var topRect = Draw(_settings.Get("Appearance.Position"), topW, topH, Whole("Appearance.OffsetX") * pxToScene, Whole("Appearance.OffsetY") * pxToScene, !top, "Top notch", null,
            (c, w, h) =>
            {
                Place(c, new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(accent) }, 11, h / 2 - 3.5);
                Place(c, new Rectangle { Width = w * 0.3, Height = 4, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(ink) }, 24, h / 2 - 2);
                Place(c, new Rectangle { Width = w * 0.24, Height = 4, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(muted) }, w - w * 0.24 - 11, h / 2 - 2);
            });

        var deckOn = _settings.Get("Deck.Enabled") == "true";
        if (deckOn || second)
        {
            var mark = MarkPicture(11, accent);
            Draw(_settings.Get("Deck.Position"), mark is null ? 82 : 100, 22, Whole("Deck.OffsetX") * pxToScene, Whole("Deck.OffsetY") * pxToScene, !second || !deckOn, deckOn ? "Second notch" : "Second notch (off)", topRect,
                (c, w, h) =>
                {
                    var left = 12.0;
                    if (mark is not null)
                    {
                        Place(c, mark, left, h / 2 - 5.5);
                        left += 18;
                    }
                    Place(c, new Rectangle { Width = w - left - 12, Height = 4, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(ink) }, left, h / 2 - 2);
                });
        }

        return new Border { Child = canvas, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(8) };
    }

    // ---------- The closed notches, with their words ----------

    /// <summary>A closed notch as a picture: its mark and its words, in the notches' colours and shape.</summary>
    private Border ClosedNotch(FrameworkElement? mark, FrameworkElement words, bool atBottom = false)
    {
        var (back, _, _, edge, _) = NotchColours();
        var shape = _settings.Get("Appearance.ShapeStyle");
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (mark is not null)
        {
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Margin = new Thickness(0, 0, 8, 0);
            line.Children.Add(mark);
        }
        words.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(words);
        return new Border
        {
            Child = line,
            Background = new SolidColorBrush(back),
            BorderBrush = _settings.Get("Themes.ContourBorderMode") == "borderless" ? Brushes.Transparent : new SolidColorBrush(edge),
            BorderThickness = new Thickness(1),
            // A notch is flat where it meets the screen's edge.
            CornerRadius = shape == "w11" ? new CornerRadius(8) : shape != "notch" ? new CornerRadius(17) : atBottom ? new CornerRadius(14, 14, 0, 0) : new CornerRadius(0, 0, 14, 14),
            MinHeight = 34,
            Padding = new Thickness(18, 4, 18, 4),
            VerticalAlignment = VerticalAlignment.Top,
        };
    }

    /// <summary>The second notch closed: its emoji and what it says now.</summary>
    private Border ClosedSecondNotch()
    {
        var (_, ink, _, _, accent) = NotchColours();
        var (date, time) = DeckWindow.DateTimeWords(_settings, DateTime.Now);
        var says = _settings.Get("Deck.Shows") switch
        {
            "time" => time,
            "date-time" => date + "  ·  " + time,
            "time-date" => time + "  ·  " + date,
            _ => date,
        };
        return ClosedNotch(MarkPicture(15, accent), new TextBlock { Text = says, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), FontFamily = NotchFont() },
            atBottom: _settings.Get("Deck.Position").StartsWith("bottom", StringComparison.Ordinal));
    }

    private FontFamily NotchFont()
    {
        var family = _settings.Get("Themes.FontFamily").Trim();
        return family.Length > 0 ? new FontFamily(family + ", Segoe UI Variable Display, Segoe UI") : Numbers;
    }

    /// <summary>A dark strip that pictures sit on, each under a caption.</summary>
    private Border Stage(params (string Caption, FrameworkElement Picture)[] items)
    {
        // Side by side; they wrap when the window is narrow.
        var line = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (caption, picture) in items)
        {
            var column = new StackPanel { Margin = new Thickness(14, 4, 14, 4) };
            column.Children.Add(new TextBlock { Text = caption, FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
            picture.HorizontalAlignment = HorizontalAlignment.Center;
            column.Children.Add(picture);
            line.Children.Add(column);
        }
        return new Border { Child = line, Background = Wallpaper(), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 14, 16, 22) };
    }

    /// <summary>The Clock &amp; date page's picture: both notches closed, saying the time and date as set.</summary>
    private FrameworkElement ClockSample()
    {
        var (_, ink, muted, _, accent) = NotchColours();
        var (date, time) = DeckWindow.DateTimeWords(_settings, DateTime.Now);
        var font = NotchFont();
        var clock = new TextBlock { Text = time, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), FontFamily = font };
        var day = new TextBlock { Text = date, FontSize = 11.5, Foreground = new SolidColorBrush(muted), FontFamily = font };
        var words = new StackPanel { Orientation = Orientation.Horizontal };
        if (_settings.Get("Modules.DateFirst") == "true")
        {
            words.Orientation = Orientation.Vertical;
            day.HorizontalAlignment = clock.HorizontalAlignment = HorizontalAlignment.Center;
            words.Children.Add(day);
            words.Children.Add(clock);
        }
        else
        {
            day.Margin = new Thickness(10, 0, 0, 0);
            day.VerticalAlignment = clock.VerticalAlignment = VerticalAlignment.Center;
            words.Children.Add(clock);
            words.Children.Add(day);
        }
        var dot = new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(accent) };
        return Stage(("Top notch", ClosedNotch(dot, words)), ("Second notch", ClosedSecondNotch()));
    }

    // ---------- The mark beside the closed second notch's words ----------

    /// <summary>The second notch's mark as the setting has it: Nyatch's paws, an emoji, or nothing.</summary>
    private FrameworkElement? MarkPicture(double size, Color accent) => _settings.Get("Deck.Emoji") switch
    {
        "none" => null,
        "paws" or "" => Paws(size, new SolidColorBrush(accent)),
        "character" => HerPictureIfOn(size * 1.5, "closed", "face", "happy", "idle") is { } her ? her : Paws(size, new SolidColorBrush(accent)),
        var emoji => EmojiPicture(emoji, size),
    };

    /// <summary>Nyatch's mark: two paw prints, one stepping ahead of the other (as the island draws them).</summary>
    private static FrameworkElement Paws(double size, Brush fill)
    {
        var shapes = new GeometryGroup { FillRule = FillRule.Nonzero };
        var c = size / 2;
        void Paw(double dx, double dy, double s, double degrees)
        {
            var at = new Point(c + dx * size, c + dy * size);
            var paw = new GeometryGroup { FillRule = FillRule.Nonzero, Transform = new RotateTransform(degrees, at.X, at.Y) };
            paw.Children.Add(new EllipseGeometry(new Point(at.X, at.Y + 0.18 * s), 0.30 * s, 0.24 * s));
            foreach (var (tx, ty, rx, ry) in new[] { (-0.36, -0.04, 0.11, 0.14), (-0.13, -0.30, 0.12, 0.155), (0.13, -0.30, 0.12, 0.155), (0.36, -0.04, 0.11, 0.14) })
            {
                paw.Children.Add(new EllipseGeometry(new Point(at.X + tx * s, at.Y + ty * s), rx * s, ry * s));
            }
            shapes.Children.Add(paw);
        }
        Paw(-0.24, -0.22, 0.56 * size * 1.23, -18);
        Paw(0.24, 0.24, 0.56 * size * 1.23, 22);
        return new Path { Data = shapes, Fill = fill, Width = size, Height = size };
    }

    private readonly Dictionary<(string Emoji, int Pixels), ImageSource?> _emojiImages = [];

    /// <summary>An emoji in colour (drawn by the island's library: WPF text has no colour glyphs); as plain text if that fails.</summary>
    private FrameworkElement EmojiPicture(string emoji, double size)
    {
        var pixels = (int)Math.Ceiling(size * 1.25 * Math.Max(1.0, VisualTreeHelper.GetDpi(this).PixelsPerDip) * 1.5);
        if (!_emojiImages.TryGetValue((emoji, pixels), out var image))
        {
            image = null;
            if (Island.RenderEmoji(emoji, pixels) is { } bytes)
            {
                var bitmap = BitmapSource.Create(pixels, pixels, 96, 96, PixelFormats.Pbgra32, null, bytes, pixels * 4);
                bitmap.Freeze();
                image = bitmap;
            }
            _emojiImages[(emoji, pixels)] = image;
        }
        if (image is null) return new TextBlock { Text = emoji, FontSize = size * 0.85, FontFamily = new FontFamily("Segoe UI Emoji"), Foreground = _soft, Width = size * 1.25, TextAlignment = TextAlignment.Center };
        var picture = new Image { Source = image, Width = size * 1.25, Height = size * 1.25 };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        return picture;
    }
}

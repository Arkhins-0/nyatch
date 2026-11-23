using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nyatch.Core;

namespace Nyatch.App;

// Visual pickers: for settings about how things look, a small picture of each choice to click,
// instead of a list of names. The picture shows what the choice does.
internal sealed partial class SettingsWindow
{
    /// <summary>A picker's view, and how to mark a value as chosen (after a reset, say).</summary>
    private sealed record Picker(FrameworkElement View, Action<string> Select);

    // Colours used inside the pictures: a dark mini screen, the island's black and its cyan accent.
    private static readonly Brush ScreenFill = Frozen("#2B2D35");
    private static readonly Brush ScreenEdge = Frozen("#3A3C46");
    private static readonly Brush IslandFill = Frozen("#08080A");
    private static readonly Brush IslandEdge = Frozen("#4A4C56");
    private static readonly Brush IslandAccent = Frozen("#4CC9F0");
    private static readonly Brush Track = Frozen("#4A4C56");

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>A picker for this setting, or null for settings shown as a plain control.</summary>
    private Picker? PickerFor(Setting setting, Action<string> apply)
    {
        Func<string, (string Label, FrameworkElement Picture)>? draw = setting.FullName switch
        {
            "Appearance.ShapeStyle" => v => (v switch { "w11" => "Windows 11", "notch" => "Notch", _ => "Pill" }, ShapePicture(v)),
            "Appearance.Position" or "Deck.Position" => v => (PositionLabel(v), PositionPicture(v)),
            "Themes.ThemePreset" => v => (ThemeLabel(setting, v), ThemePicture(v)),
            "Appearance.ProgressStyle" => v => (v switch { "wavy" => "Wavy", "squiggle" => "Squiggle", "bar" => "Bar", _ => "Slim" }, ProgressPicture(v)),
            "Appearance.SpectrumStyle" => v => (v switch { "led" => "Peak matrix", "plasma" => "Plasma thread", "orb" => "Pulse orb", _ => "Classic bars" }, SpectrumPicture(v)),
            "Animations.AnimationStyle" => v => (v switch { "smooth" => "Smooth", "bouncy" => "Bouncy", "snappy" => "Snappy", _ => "Balanced" }, SpringPicture(v)),
            "Themes.ContourBorderMode" => v => (v switch { "auto" => "Album colour", "borderless" => "None", _ => "Theme line" }, ContourPicture(v)),
            "General.SettingsTheme" => v => (v switch { "light" => "Light", "dark" => "Dark", "black" => "Black", _ => "As Windows" }, WindowPicture(v)),
            _ => null,
        };
        if (draw is null || setting.Kind != SettingKind.Choice) return null;

        var tiles = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var byValue = new Dictionary<string, (Border Tile, TextBlock Label)>();
        void Select(string value)
        {
            foreach (var (key, (tile, label)) in byValue)
            {
                var chosen = key == value;
                tile.BorderBrush = chosen ? _accent : _line;
                tile.BorderThickness = new Thickness(chosen ? 2 : 1);
                tile.Padding = new Thickness(chosen ? 7 : 8);
                tile.Background = chosen ? _accentTint : _panel;
                label.Foreground = chosen ? _accent : _soft;
                label.FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal;
                tile.SetValue(System.Windows.Automation.AutomationProperties.ItemStatusProperty, chosen ? "chosen" : "");
            }
        }
        foreach (var option in setting.Options)
        {
            var value = option.Value;
            var (text, picture) = draw(value);
            var label = new TextBlock { Text = text, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 104 };
            var stack = new StackPanel();
            picture.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(picture);
            stack.Children.Add(label);
            var tile = new Border { Child = stack, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Focusable = true, ToolTip = option.Label, MinWidth = 96 };
            tile.SetValue(AutomationName, option.Label);
            tile.MouseEnter += (_, _) =>
            {
                if (tile.BorderBrush != _accent) tile.Background = _high;
            };
            tile.MouseLeave += (_, _) =>
            {
                if (tile.BorderBrush != _accent) tile.Background = _panel;
            };
            void Choose()
            {
                apply(value);
                Select(value);
            }
            tile.MouseLeftButtonUp += (_, _) => Choose();
            tile.KeyDown += (_, key) =>
            {
                if (key.Key is not (Key.Enter or Key.Space)) return;
                key.Handled = true;
                Choose();
            };
            byValue[value] = (tile, label);
            tiles.Children.Add(tile);
        }
        tiles.SetValue(AutomationName, setting.Name);
        Select(_settings.Get(setting.FullName));
        return new Picker(tiles, Select);
    }

    private static string PositionLabel(string value) => value switch
    {
        "top-left" => "Top left",
        "top-right" => "Top right",
        "bottom-left" => "Bottom left",
        "bottom-center" => "Bottom centre",
        "bottom-right" => "Bottom right",
        _ => "Top centre",
    };

    private static string ThemeLabel(Setting setting, string value)
    {
        var label = setting.Options.FirstOrDefault(o => o.Value == value)?.Label ?? value;
        var cut = label.IndexOfAny([' ', '(']);
        return value == "custom" ? "Custom" : cut > 0 ? label[..cut] : label;
    }

    // ---------- The pictures ----------

    private static Canvas Canvas(double w, double h) => new() { Width = w, Height = h, ClipToBounds = true, SnapsToDevicePixels = true };

    private static void Place(Canvas canvas, UIElement shape, double x, double y)
    {
        System.Windows.Controls.Canvas.SetLeft(shape, x);
        System.Windows.Controls.Canvas.SetTop(shape, y);
        canvas.Children.Add(shape);
    }

    /// <summary>A mini screen: a rounded dark rectangle.</summary>
    private static Canvas Screen(double w = 88, double h = 50)
    {
        var canvas = Canvas(w, h);
        Place(canvas, new Rectangle { Width = w, Height = h, RadiusX = 5, RadiusY = 5, Fill = ScreenFill, Stroke = ScreenEdge, StrokeThickness = 1 }, 0, 0);
        return canvas;
    }

    /// <summary>The island's three shapes, at the top of a screen.</summary>
    private static FrameworkElement ShapePicture(string value)
    {
        var screen = Screen();
        const double w = 44, h = 15, x = (88 - w) / 2;
        if (value == "notch")
        {
            // Flush to the top edge: square where it meets it, rounded underneath.
            var geometry = new StreamGeometry();
            using (var pen = geometry.Open())
            {
                pen.BeginFigure(new Point(x, 0), true, true);
                pen.LineTo(new Point(x + w, 0), true, false);
                pen.LineTo(new Point(x + w, h - 6), true, false);
                pen.ArcTo(new Point(x + w - 6, h), new Size(6, 6), 0, false, SweepDirection.Clockwise, true, false);
                pen.LineTo(new Point(x + 6, h), true, false);
                pen.ArcTo(new Point(x, h - 6), new Size(6, 6), 0, false, SweepDirection.Clockwise, true, false);
            }
            screen.Children.Add(new Path { Data = geometry, Fill = IslandFill, Stroke = IslandEdge, StrokeThickness = 1 });
        }
        else
        {
            var radius = value == "w11" ? 3.0 : h / 2;
            Place(screen, new Rectangle { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Fill = IslandFill, Stroke = IslandEdge, StrokeThickness = 1 }, x, 5);
        }
        return screen;
    }

    /// <summary>A screen with the notch at one of the six places.</summary>
    private static FrameworkElement PositionPicture(string value)
    {
        var screen = Screen();
        const double w = 26, h = 8;
        var x = value.EndsWith("left", StringComparison.Ordinal) ? 6 : value.EndsWith("right", StringComparison.Ordinal) ? 88 - w - 6 : (88 - w) / 2;
        var y = value.StartsWith("bottom", StringComparison.Ordinal) ? 50 - h - 5 : 5;
        Place(screen, new Rectangle { Width = w, Height = h, RadiusX = h / 2, RadiusY = h / 2, Fill = IslandAccent }, x, y);
        return screen;
    }

    /// <summary>A swatch in the theme's own colours: its background, a pill of text and a quieter line.</summary>
    private static FrameworkElement ThemePicture(string value)
    {
        var canvas = Canvas(88, 50);
        if (!DeckWindow.Palettes.TryGetValue(value, out var palette))
        {
            // Custom: the colours are the user's own.
            var rainbow = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(0xFF, 0x6B, 0x5B), 0), new(Color.FromRgb(0xF7, 0xC8, 0x48), 0.35),
                new(Color.FromRgb(0x4C, 0xC9, 0xF0), 0.7), new(Color.FromRgb(0x9B, 0x6B, 0xF2), 1),
            }, 0);
            Place(canvas, new Rectangle { Width = 88, Height = 50, RadiusX = 5, RadiusY = 5, Fill = rainbow }, 0, 0);
            return canvas;
        }
        static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
        Place(canvas, new Rectangle { Width = 88, Height = 50, RadiusX = 5, RadiusY = 5, Fill = B(palette.Background), Stroke = B(palette.Border), StrokeThickness = 1 }, 0, 0);
        Place(canvas, new Rectangle { Width = 36, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = B(palette.Text) }, 10, 14);
        Place(canvas, new Rectangle { Width = 52, Height = 4, RadiusX = 2, RadiusY = 2, Fill = B(palette.Secondary) }, 10, 25);
        Place(canvas, new Rectangle { Width = 30, Height = 4, RadiusX = 2, RadiusY = 2, Fill = B(palette.Secondary) }, 10, 33);
        Place(canvas, new Ellipse { Width = 10, Height = 10, Fill = IslandAccent }, 68, 12);
        return canvas;
    }

    /// <summary>The song's progress bar: played part in the accent, the rest as a quiet track.</summary>
    private static FrameworkElement ProgressPicture(string value)
    {
        var canvas = Canvas(96, 30);
        const double y = 15, played = 54, end = 92, start = 4;
        var thickness = value == "bar" ? 6.0 : 2.5;
        canvas.Children.Add(new Line { X1 = played, Y1 = y, X2 = end, Y2 = y, Stroke = Track, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        if (value is "wavy" or "squiggle")
        {
            // The played part as a wave: wavy is long and tall, squiggle short and shallow.
            var (length, height) = value == "wavy" ? (16.0, 4.0) : (9.0, 2.2);
            var wave = new PolyLineSegment();
            for (var x = start; x <= played; x += 1) wave.Points.Add(new Point(x, y + height * Math.Sin((x - start) / length * 2 * Math.PI)));
            var figure = new PathFigure { StartPoint = new Point(start, y) };
            figure.Segments.Add(wave);
            canvas.Children.Add(new Path { Data = new PathGeometry([figure]), Stroke = IslandAccent, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        else
        {
            canvas.Children.Add(new Line { X1 = start, Y1 = y, X2 = played, Y2 = y, Stroke = IslandAccent, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        if (value == "bar") Place(canvas, new Rectangle { Width = 2.5, Height = 14, RadiusX = 1, RadiusY = 1, Fill = Brushes.White }, played - 1, y - 7);
        else Place(canvas, new Ellipse { Width = 9, Height = 9, Fill = Brushes.White, Stroke = IslandAccent, StrokeThickness = 1.5 }, played - 4.5, y - 4.5);
        return canvas;
    }

    /// <summary>The audio visualizer's four looks.</summary>
    private static FrameworkElement SpectrumPicture(string value)
    {
        var canvas = Canvas(96, 36);
        double[] levels = [0.35, 0.65, 0.9, 0.55, 0.8, 0.45, 0.7, 0.3, 0.5];
        switch (value)
        {
            case "led":
                // Peak matrix: columns of dots, lit up to the level.
                for (var c = 0; c < levels.Length; c++)
                {
                    for (var r = 0; r < 5; r++)
                    {
                        var lit = (r + 1) / 5.0 <= levels[c] + 0.1;
                        Place(canvas, new Ellipse { Width = 5, Height = 5, Fill = IslandAccent, Opacity = lit ? 1 : 0.18 }, 9 + c * 9, 30 - r * 6.5);
                    }
                }
                break;
            case "plasma":
                // Plasma thread: two soft waves woven together.
                foreach (var (phase, opacity) in new[] { (0.0, 1.0), (1.4, 0.45) })
                {
                    var line = new PolyLineSegment();
                    for (var x = 4.0; x <= 92; x += 1) line.Points.Add(new Point(x, 18 + 10 * Math.Sin(x / 13 + phase) * Math.Sin(x / 29)));
                    var figure = new PathFigure { StartPoint = new Point(4, 18) };
                    figure.Segments.Add(line);
                    canvas.Children.Add(new Path { Data = new PathGeometry([figure]), Stroke = IslandAccent, StrokeThickness = 2.2, Opacity = opacity, StrokeLineJoin = PenLineJoin.Round });
                }
                break;
            case "orb":
                // Pulse orb: a glowing circle that swells with the music.
                var glow = new RadialGradientBrush(Color.FromArgb(0x99, 0x4C, 0xC9, 0xF0), Color.FromArgb(0, 0x4C, 0xC9, 0xF0));
                Place(canvas, new Ellipse { Width = 36, Height = 36, Fill = glow }, 30, 0);
                Place(canvas, new Ellipse { Width = 18, Height = 18, Fill = IslandAccent }, 39, 9);
                break;
            default:
                // Classic bars.
                for (var c = 0; c < levels.Length; c++)
                {
                    var h = 4 + levels[c] * 28;
                    Place(canvas, new Rectangle { Width = 6, Height = h, RadiusX = 3, RadiusY = 3, Fill = IslandAccent }, 7 + c * 9.5, 34 - h);
                }
                break;
        }
        return canvas;
    }

    /// <summary>How the island moves into place: the spring's path over time, with the resting line.</summary>
    private static FrameworkElement SpringPicture(string value)
    {
        var canvas = Canvas(96, 40);
        // The resting place.
        canvas.Children.Add(new Line { X1 = 4, Y1 = 12, X2 = 92, Y2 = 12, Stroke = Track, StrokeThickness = 1, StrokeDashArray = [3, 3] });
        var (stiffness, damping) = value switch
        {
            "smooth" => (120.0, 22.0),
            "bouncy" => (220.0, 7.0),
            "snappy" => (520.0, 40.0),
            _ => (220.0, 18.0),
        };
        // Moving from 0 to 1 over 1.4 seconds, drawn from the bottom (start) to the dashed line (rest).
        var path = new PolyLineSegment();
        double position = 0, speed = 0;
        const double step = 0.002;
        for (var t = 0.0; t <= 1.4; t += step)
        {
            speed += (stiffness * (1 - position) - damping * speed) * step;
            position += speed * step;
            if (Math.Abs(t / 0.02 - Math.Round(t / 0.02)) < step / 0.02) path.Points.Add(new Point(4 + t / 1.4 * 88, 36 - position * 24));
        }
        var figure = new PathFigure { StartPoint = new Point(4, 36) };
        figure.Segments.Add(path);
        canvas.Children.Add(new Path { Data = new PathGeometry([figure]), Stroke = IslandAccent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round });
        return canvas;
    }

    /// <summary>The island's outline: the theme's line, the album's colour, or none.</summary>
    private static FrameworkElement ContourPicture(string value)
    {
        var screen = Screen();
        Brush? stroke = value switch
        {
            "auto" => new LinearGradientBrush(Color.FromRgb(0xFF, 0x6B, 0x5B), Color.FromRgb(0xF7, 0xC8, 0x48), 0),
            "borderless" => null,
            _ => IslandEdge,
        };
        Place(screen, new Rectangle { Width = 50, Height = 17, RadiusX = 8.5, RadiusY = 8.5, Fill = IslandFill, Stroke = stroke, StrokeThickness = value == "auto" ? 1.6 : 1 }, 19, 7);
        if (value == "auto") Place(screen, new Ellipse { Width = 9, Height = 9, Fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0x6B, 0x5B), Color.FromRgb(0xF7, 0xC8, 0x48), 45) }, 23, 11);
        return screen;
    }

    /// <summary>This window in light, dark or black, or half and half for "as Windows has it".</summary>
    private static FrameworkElement WindowPicture(string value)
    {
        var canvas = Canvas(88, 50);
        void Window(double x, double w, string page, string rail, string line, string text)
        {
            var clip = new RectangleGeometry(new Rect(x, 0, w, 50));
            var group = new Canvas { Width = 88, Height = 50, Clip = clip };
            Place(group, new Rectangle { Width = 88, Height = 50, RadiusX = 5, RadiusY = 5, Fill = Frozen(page), Stroke = Frozen(line), StrokeThickness = 1 }, 0, 0);
            Place(group, new Rectangle { Width = 24, Height = 48, Fill = Frozen(rail) }, 1, 1);
            Place(group, new Rectangle { Width = 14, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Frozen("#7B68EE") }, 5, 8);
            Place(group, new Rectangle { Width = 12, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Frozen(text), Opacity = 0.4 }, 5, 15);
            Place(group, new Rectangle { Width = 30, Height = 4, RadiusX = 2, RadiusY = 2, Fill = Frozen(text) }, 32, 8);
            Place(group, new Rectangle { Width = 48, Height = 26, RadiusX = 3, RadiusY = 3, Fill = Frozen(page), Stroke = Frozen(line), StrokeThickness = 1 }, 32, 17);
            canvas.Children.Add(group);
        }
        var light = ("#FFFFFF", "#F3F4F6", "#E1E3E8", "#1E2028");
        var dark = ("#1A1B1F", "#16171A", "#34353C", "#E8E9EC");
        var black = ("#000000", "#08080A", "#26262B", "#ECECEF");
        switch (value)
        {
            case "light": Window(0, 88, light.Item1, light.Item2, light.Item3, light.Item4); break;
            case "dark": Window(0, 88, dark.Item1, dark.Item2, dark.Item3, dark.Item4); break;
            case "black": Window(0, 88, black.Item1, black.Item2, black.Item3, black.Item4); break;
            default:
                Window(0, 44, light.Item1, light.Item2, light.Item3, light.Item4);
                Window(44, 44, dark.Item1, dark.Item2, dark.Item3, dark.Item4);
                break;
        }
        return canvas;
    }
}

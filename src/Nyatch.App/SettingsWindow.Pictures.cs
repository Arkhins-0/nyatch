using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Nyatch.Core;

namespace Nyatch.App;

// Pictures of the notches' pages, drawn here: the open notch with a page in it, as quiet shapes (soft
// bars for text, tiles for icons) with the accent where the page has it.
internal sealed partial class SettingsWindow
{
    private const double PicW = 236, PicH = 150;

    private static readonly Brush PicPanel = Frozen("#0E0F12");
    private static readonly Brush PicInk = Frozen("#E8E9EC");
    private static readonly Brush PicSoft = Frozen("#6E717C");
    private static readonly Brush PicRaised = Frozen("#1E2026");
    private static readonly Brush PicStrong = Frozen("#2A2C34");

    private static Rectangle Box(Canvas canvas, double x, double y, double w, double h, Brush fill, double radius = 4)
    {
        var box = new Rectangle { Width = Math.Max(0, w), Height = Math.Max(0, h), Fill = fill, RadiusX = radius, RadiusY = radius };
        Place(canvas, box, x, y);
        return box;
    }

    /// <summary>A line of text, as a soft bar.</summary>
    private static void Words(Canvas canvas, double x, double y, double w, double h = 4, Brush? fill = null) => Box(canvas, x, y, w, h, fill ?? PicSoft, h / 2);

    private static void Disc(Canvas canvas, double x, double y, double d, Brush fill)
    {
        var disc = new Ellipse { Width = d, Height = d, Fill = fill };
        Place(canvas, disc, x, y);
    }

    private static void Glyph(Canvas canvas, string glyph, double x, double y, double size, Brush fill)
    {
        Place(canvas, new TextBlock { Text = glyph, FontFamily = Glyphs, FontSize = size, Foreground = fill }, x, y);
    }

    /// <summary>The open second notch, with its page dots; the page is drawn in it.</summary>
    private static Canvas Notch(int dot = -1)
    {
        var canvas = Canvas(PicW, PicH);
        Box(canvas, 0, 0, PicW, PicH, PicPanel, 14);
        if (dot >= 0)
        {
            for (var i = 0; i < 5; i++) Box(canvas, PicW - 8, 52 + i * 9 + (i > dot ? 8 : 0), 3, i == dot ? 12 : 3, i == dot ? PicInk : PicStrong, 1.5);
        }
        return canvas;
    }

    private FrameworkElement DeckPagePicture(string id)
    {
        var accent = new SolidColorBrush(_accent.Color);
        var tint = new SolidColorBrush(Color.FromArgb(0x55, _accent.Color.R, _accent.Color.G, _accent.Color.B));
        var c = Notch(dot: 1);
        const double L = 12, T = 12, R = PicW - 18;
        void Title(double w = 60) => Words(c, L, T + 2, w, 6, PicInk);
        switch (id)
        {
            case "apps":
            {
                Title(34);
                var colours = new[] { "#4C8DF6", "#E8A33D", "#3BC46B", "#E5484D", "#9B7BF2", "#3FC1C9", "#F28FAD" };
                for (var i = 0; i < 18; i++)
                {
                    var x = L + (i % 6) * 34;
                    var y = T + 20 + (i / 6) * 38;
                    Box(c, x, y, 26, 26, Frozen(colours[i % colours.Length]), 7);
                    if (i is 6 or 7 or 8) Disc(c, x + 11, y + 29, 4, PicInk);
                }
                break;
            }
            case "tray":
                Title(48);
                Glyph(c, "", R - 30, T, 9, PicSoft);
                Glyph(c, "", R - 14, T, 9, PicSoft);
                for (var i = 0; i < 4; i++)
                {
                    var y = T + 20 + i * 26;
                    Box(c, L, y, R - L, 21, i == 1 ? tint : PicRaised, 6);
                    Box(c, L + 6, y + 5, 11, 11, Frozen(i % 2 == 0 ? "#4C8DF6" : "#E8A33D"), 2);
                    Words(c, L + 24, y + 9, 90 - i * 10, 4, PicInk);
                    Words(c, R - 48, y + 9, 28);
                }
                break;
            case "clipboard":
                Title(56);
                Box(c, L, T + 20, R - L, 34, PicRaised, 6);
                Words(c, L + 8, T + 27, 150, 4, PicInk);
                Words(c, L + 8, T + 36, 120, 4, PicInk);
                Glyph(c, "", R - 16, T + 24, 9, accent);
                Box(c, L, T + 58, R - L, 46, PicRaised, 6);
                Box(c, L + 8, T + 64, 60, 34, Frozen("#3A4D7A"), 4);
                Box(c, L + 8, T + 82, 60, 16, Frozen("#55704A"), 0);
                Box(c, L, T + 108, R - L, 20, PicRaised, 6);
                Words(c, L + 8, T + 116, 110, 4, PicInk);
                break;
            case "notifications":
                Title(70);
                Glyph(c, "", R - 14, T, 10, PicSoft);
                for (var i = 0; i < 3; i++)
                {
                    var y = T + 20 + i * 36;
                    Box(c, L, y, R - L, 31, PicRaised, 7);
                    Box(c, L + 7, y + 7, 16, 16, Frozen(i == 0 ? "#5865F2" : i == 1 ? "#3BC46B" : "#E8A33D"), 4);
                    Words(c, L + 30, y + 8, 70, 4, PicInk);
                    Words(c, L + 30, y + 18, 120);
                    Words(c, R - 30, y + 8, 18);
                }
                break;
            case "claude" or "chatgpt" or "gemini":
            {
                Words(c, L, T + 2, id == "chatgpt" ? 52 : 40, 6, PicInk);
                Glyph(c, "", R - 30, T, 9, PicSoft);
                Glyph(c, "", R - 14, T, 9, PicSoft);
                Box(c, R - 110, T + 20, 110, 20, PicStrong, 8);
                Words(c, R - 100, T + 28, 86, 4, PicInk);
                Words(c, L, T + 50, 150, 4, PicInk);
                Words(c, L, T + 59, 170, 4, PicInk);
                Words(c, L, T + 68, 120, 4, PicInk);
                Box(c, L, T + 78, 130, 16, PicRaised, 3);
                Words(c, L + 6, T + 84, 90, 3, accent);
                Box(c, L, PicH - 32, R - L, 22, PicRaised, 8);
                Words(c, L + 10, PicH - 23, 80);
                Box(c, R - 20, PicH - 29, 16, 16, accent, 5);
                break;
            }
            case "focus":
                for (var i = 0; i < 3; i++)
                {
                    var x = L + i * 70;
                    Box(c, x, T + 6, 64, 114, PicRaised, 9);
                    var ring = new Ellipse { Width = 44, Height = 44, Stroke = PicStrong, StrokeThickness = 4 };
                    Place(c, ring, x + 10, T + 20);
                    var arc = new Ellipse { Width = 44, Height = 44, Stroke = accent, StrokeThickness = 4, StrokeDashArray = [i == 0 ? 18 : i == 1 ? 8 : 24, 100], StrokeDashCap = PenLineCap.Round };
                    Place(c, arc, x + 10, T + 20);
                    Words(c, x + 18, T + 39, 28, 5, PicInk);
                    Box(c, x + 8, T + 76, 22, 14, i == 0 ? accent : PicStrong, 4);
                    Box(c, x + 34, T + 76, 22, 14, PicStrong, 4);
                    if (i == 0) Box(c, x + 8, T + 96, 48, 14, PicStrong, 4);
                }
                break;
            case "notes":
                Words(c, L, T + 2, 34, 4);
                for (var i = 0; i < 3; i++) Box(c, L, T + 12 + i * 17, 64, 14, i == 0 ? PicStrong : Brushes.Transparent, 4);
                for (var i = 0; i < 3; i++) Words(c, L + 6, T + 17 + i * 17, 40 - i * 6, 4, i == 0 ? PicInk : PicSoft);
                Words(c, L, T + 66, 34, 4);
                Words(c, L + 6, T + 78, 44, 4);
                Words(c, L + 6, T + 92, 36, 4);
                Words(c, L + 76, T + 2, 70, 6, PicInk);
                Box(c, L + 76, T + 16, R - L - 76, 20, PicRaised, 6);
                Words(c, L + 84, T + 24, 70);
                for (var i = 0; i < 3; i++)
                {
                    var y = T + 42 + i * 26;
                    Box(c, L + 76, y, R - L - 76, 22, PicRaised, 6);
                    var tick = new Ellipse { Width = 10, Height = 10, Stroke = i == 2 ? accent : PicSoft, Fill = i == 2 ? accent : Brushes.Transparent, StrokeThickness = 1.4 };
                    Place(c, tick, L + 83, y + 6);
                    Words(c, L + 100, y + 9, 80 - i * 14, 4, i == 2 ? PicSoft : PicInk);
                }
                break;
            case "terminal":
                Box(c, L, T, 46, 16, PicStrong, 5);
                Words(c, L + 7, T + 6, 26, 4, PicInk);
                Box(c, L + 50, T, 42, 16, PicRaised, 5);
                Words(c, L + 57, T + 6, 22, 4);
                Box(c, R - 18, T, 18, 16, PicRaised, 5);
                Box(c, L, T + 22, R - L, 84, PicRaised, 7);
                for (var i = 0; i < 6; i++) Words(c, L + 8, T + 30 + i * 12, i % 3 == 0 ? 90 : 150 - i * 12, 4, i % 3 == 0 ? accent : PicInk);
                Box(c, L, PicH - 32, R - L, 22, PicStrong, 7);
                Words(c, L + 8, PicH - 23, 60, 4, accent);
                Words(c, L + 74, PicH - 23, 50, 4, PicInk);
                break;
            case "online":
                Title(44);
                Box(c, L, T + 18, R - L, 42, PicRaised, 8);
                Words(c, L + 8, T + 25, 22);
                Words(c, L + 8, T + 36, 38, 9, PicInk);
                Words(c, L + 70, T + 25, 36);
                Words(c, L + 70, T + 36, 56, 9, PicInk);
                Box(c, R - 52, T + 30, 44, 16, PicStrong, 5);
                for (var i = 0; i < 4; i++)
                {
                    var x = L + i * 52;
                    Box(c, x, T + 70, 46, 54, PicRaised, 8);
                    Box(c, x + 15, T + 76, 16, 16, PicStrong, 4);
                    Words(c, x + 10, T + 98, 26, 4, PicInk);
                    Disc(c, x + 12, T + 109, 5, Frozen(i == 2 ? "#E5484D" : "#3BC46B"));
                    Words(c, x + 20, T + 110, 16);
                }
                break;
        }
        return c;
    }

    /// <summary>The page picker the hotkey opens.</summary>
    private FrameworkElement PickerPicture()
    {
        var accent = new SolidColorBrush(_accent.Color);
        var tint = new SolidColorBrush(Color.FromArgb(0x55, _accent.Color.R, _accent.Color.G, _accent.Color.B));
        var c = Notch();
        Words(c, 12, 14, 70, 6, PicInk);
        Words(c, PicW - 92, 15, 76);
        var keys = DeckPages.Keys(_settings.Get("Deck.PageKeys"));
        // As the picker has them: the pages with a letter, hidden ones left out.
        var llmPanel = NotchUse.SideInUse(_settings);
        var shown = DeckPages.Order(_settings.Get("Deck.Pages")).Where(o => o.Show != PageShow.Hidden && keys.ContainsKey(o.Id) && !(llmPanel && o.Id is "claude" or "chatgpt" or "gemini")).Take(12).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var x = 12 + (i % 3) * 72;
            var y = 30 + (i / 3) * 29;
            Box(c, x, y, 68, 25, PicRaised, 6);
            Box(c, x + 4, y + 4, 17, 17, tint, 4);
            var letter = keys.TryGetValue(shown[i].Id, out var k) ? k.ToString() : "";
            Place(c, new TextBlock { Text = letter, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = accent, Width = 17, TextAlignment = TextAlignment.Center }, x + 4, y + 5);
            var name = DeckPageChoices.First(p => p.Id == shown[i].Id).Name;
            Place(c, new TextBlock { Text = name, FontSize = 8.5, Foreground = PicInk, Width = 42, TextTrimming = TextTrimming.CharacterEllipsis }, x + 24, y + 6);
        }
        return c;
    }

    /// <summary>Her picture on a preview (when characters are on and the pack has one of the names): true when placed.</summary>
    private bool Her(Canvas canvas, double x, double y, double size, params string[] names) => Her(canvas, x, y, size, size, names);

    private bool Her(Canvas canvas, double x, double y, double w, double h, params string[] names)
    {
        if (HerPictureIfOn(Math.Max(w, h), names) is not { } her) return false;
        her.Width = w;
        her.Height = h;
        Place(canvas, her, x, y);
        return true;
    }

    /// <summary>A page of the top notch, open.</summary>
    private FrameworkElement TopPagePicture(string id)
    {
        var accent = new SolidColorBrush(_accent.Color);
        var tint = new SolidColorBrush(Color.FromArgb(0x55, _accent.Color.R, _accent.Color.G, _accent.Color.B));
        var sun = Frozen("#F2B544");
        var cloud = Frozen("#AEB4C2");
        var c = Notch();
        const double L = 14, T = 14, R = PicW - 14;
        switch (id)
        {
            case PrimaryPage:
            {
                // The time page: your line, the clock and the date; with a character, on the left with her
                // picture at the bottom right.
                var her = Her(c, R - 66, PicH - 12 - 66, 66, "time-day", "idle");
                var x = her ? L + 4 : (PicW - 132) / 2;
                Words(c, her ? x : (PicW - 44) / 2, T + 16, 44, 4);
                Words(c, x, T + 32, 132, 26, PicInk);
                Words(c, her ? x : (PicW - 84) / 2, T + 70, 84, 6);
                break;
            }
            case "calendar":
            {
                // Today in a card on the left, the month beside it; with a character, her tall picture at the right.
                var her = Her(c, R - 48, T, 48, PicH - T * 2, "calendar", "idle");
                Box(c, L, T, 48, PicH - T * 2, PicRaised, 8);
                Words(c, L + 10, T + 10, 28, 4, accent);
                Words(c, L + 10, T + 44, 28, 22, PicInk);
                Words(c, L + 10, T + 104, 28, 4);
                var gridW = (her ? R - 56 : R) - (L + 58);
                var step = gridW / 7;
                for (var d = 0; d < 7; d++) Words(c, L + 58 + d * step + (step - 10) / 2, T + 4, 10, 3);
                for (var w = 0; w < 5; w++)
                {
                    for (var d = 0; d < 7; d++)
                    {
                        var x = L + 58 + d * step + (step - 14) / 2;
                        var y = T + 18 + w * 21;
                        var today = w == 3 && d == 0;
                        if (today) Disc(c, x - 1, y - 1, 16, accent);
                        var other = (w == 0 && d < 2) || (w == 4 && d > 4);
                        Words(c, x + 2, y + 5, 10, 4, today ? PicPanel : other ? PicStrong : PicInk);
                    }
                }
                break;
            }
            case "weather":
            {
                // The place at the top; the reading with today's range beside it and the words under it; the
                // next hours on a line; the details, under her picture when there is a character.
                Words(c, (PicW - 46) / 2, T - 4, 46, 5, accent);
                var her = Her(c, R - 44, T - 6, 44, "weather-sun", "cool", "idle");
                Disc(c, L, T + 8, 18, sun);
                Box(c, L + 7, T + 17, 20, 10, cloud, 5);
                Words(c, L + 34, T + 8, 34, 18, PicInk);
                Words(c, L + 76, T + 8, 18, 3, PicInk);
                Box(c, L + 76, T + 15, 30, 3, sun, 1.5);
                Disc(c, L + 88, T + 13, 7, PicInk);
                Words(c, L + 76, T + 22, 18, 3);
                Words(c, L + 34, T + 32, 40, 4);
                // The hours: a line, a picture and a time each.
                var line = new Polyline { Stroke = sun, StrokeThickness = 1.5, Points = [] };
                for (var i = 0; i < 5; i++)
                {
                    var x = L + 10 + i * 24;
                    var y = T + 60 + (i is 1 or 2 ? 4 : i == 4 ? -3 : 0);
                    line.Points.Add(new Point(x, y));
                    Words(c, x - 6, y - 9, 12, 4, PicInk);
                    Disc(c, x - 6, T + 72, 12, i % 2 == 0 ? cloud : sun);
                    Words(c, x - 7, T + 92, 14, 3);
                }
                Place(c, line, 0, 0);
                // The details: two to a row under her, or a list beside the reading.
                var left = L + 130;
                if (her)
                {
                    for (var i = 0; i < 5; i++)
                    {
                        var x = left + (i % 2) * 40;
                        var y = T + 48 + (i / 2) * 18;
                        Words(c, x, y, 14, 3);
                        Words(c, x + 18, y, 16, 3, PicInk);
                    }
                }
                else
                {
                    for (var i = 0; i < 5; i++)
                    {
                        Words(c, left, T + 14 + i * 20, 26, 3);
                        Words(c, R - 24, T + 14 + i * 20, 24, 3, PicInk);
                    }
                }
                break;
            }
            case "hardware":
            {
                // Six cards; with a character, three on each side of her, in a ring that fills with the load.
                var her = HerPictureIfOn(40, "hardware-idle", "cool", "idle");
                Words(c, L, T - 2, 62, 5, PicInk);
                double[] levels = [0.42, 0.18, 0.86, 0.3, 0.12, 0.4];
                var mid = her is null ? 0 : 60;
                var cardW = (R - L - mid - (her is null ? 6 : 12)) / 2;
                for (var i = 0; i < 6; i++)
                {
                    var x = L + (i % 2) * (cardW + 6 + (her is null ? 0 : mid + 6));
                    var y = T + 12 + (i / 2) * 37;
                    Box(c, x, y, cardW, 32, PicRaised, 6);
                    Words(c, x + 8, y + 7, 18, 3);
                    Words(c, x + 8, y + 15, 30, 6, PicInk);
                    Box(c, x + 8, y + 25, cardW - 16, 2.5, PicStrong, 1.2);
                    Box(c, x + 8, y + 25, (cardW - 16) * levels[i], 2.5, levels[i] > 0.8 ? Frozen("#E0564F") : accent, 1.2);
                }
                if (her is not null)
                {
                    var cx = L + cardW + 6 + mid / 2.0;
                    var cy = T + 12 + 53.5;
                    Place(c, new Ellipse { Width = 54, Height = 54, Stroke = PicStrong, StrokeThickness = 3 }, cx - 27, cy - 27);
                    Place(c, new System.Windows.Shapes.Path
                    {
                        Stroke = accent, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                        Data = Geometry.Parse(FormattableString.Invariant($"M {cx},{cy - 27} A 27,27 0 0 1 {cx + 25.7},{cy + 8.3}")),
                    }, 0, 0);
                    her.Clip = new EllipseGeometry(new Point(20, 20), 20, 20);
                    Place(c, her, cx - 20, cy - 20);
                }
                break;
            }
            case "tray":
            {
                // The files, and with a character her picture under the last one.
                Words(c, L, T + 2, 50, 6, PicInk);
                Glyph(c, "\uE77F", R - 30, T, 9, PicSoft);
                Glyph(c, "\uE74D", R - 14, T, 9, PicSoft);
                var her = HerPictureIfOn(44, "tray-end", "headpat", "happy", "idle");
                for (var i = 0; i < (her is null ? 4 : 2); i++)
                {
                    var y = T + 20 + i * 26;
                    Box(c, L, y, R - L, 21, i == 1 ? tint : PicRaised, 6);
                    Box(c, L + 6, y + 5, 11, 11, Frozen(i % 2 == 0 ? "#4C8DF6" : "#E8A33D"), 2);
                    Words(c, L + 24, y + 9, 90 - i * 10, 4, PicInk);
                    Words(c, R - 34, y + 9, 28);
                }
                if (her is not null)
                {
                    Place(c, her, PicW / 2 - 44, PicH - 12 - 44);
                    Words(c, PicW / 2 + 8, PicH - 12 - 24, 30, 4);
                }
                break;
            }
        }
        return c;
    }
}

#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property OutputType=Exe
#:property PublishAot=false
#:property PublishTrimmed=false

// Draws Nyatch's logo and the pictures made from it:
//   dotnet run tools\BrandArt.cs -- <output folder>
// writes nyatch.ico (every size Windows asks for), logo-1024.png, logo.svg,
// and the installer's banner.bmp (493x58) and dialog.bmp (493x312).
//
// The logo belongs with Arkhime and Nihonjo: a dark warm tile, an open ring in a red-to-coral
// gradient with its gap at the upper right, a small accent in the gap, and a cream mark inside.
// Nyatch's mark is its two notches: the island, and the second notch under it. The accent in
// the gap is the island's own colour, like the dot the island shows for the microphone.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("brand");
Directory.CreateDirectory(output);

var thread = new Thread(() => Art.WriteAll(output));
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Console.WriteLine($"Written to {output}");

static class Art
{
    // The family's colours.
    static readonly Color TileInner = Hex("#2C1519");
    static readonly Color TileOuter = Hex("#130A0D");
    static readonly Color RingStart = Hex("#D7263D");
    static readonly Color RingEnd = Hex("#FF6B5B");
    static readonly Color Cream = Hex("#FFF2EF");
    static readonly Color IslandCyan = Hex("#4CC9F0");

    static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static void WriteAll(string output)
    {
        // The icon: each size drawn on its own, small ones with heavier lines so they stay clear.
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
        var pngs = new List<(int Size, byte[] Png)>();
        foreach (var size in sizes) pngs.Add((size, Png(Render(size, size, dc => Logo(dc, 0, 0, size)))));
        WriteIco(Path.Combine(output, "nyatch.ico"), pngs);
        foreach (var (size, png) in pngs)
        {
            if (size is 16 or 32 or 48 or 256) File.WriteAllBytes(Path.Combine(output, $"logo-{size}.png"), png);
        }
        File.WriteAllBytes(Path.Combine(output, "logo-1024.png"), Png(Render(1024, 1024, dc => Logo(dc, 0, 0, 1024))));
        File.WriteAllText(Path.Combine(output, "logo.svg"), Svg(), new UTF8Encoding(false));

        // The installer's pictures. Windows Installer writes black text over them, so the
        // parts under its text stay white.
        WriteBmp(Path.Combine(output, "banner.bmp"), Render(493, 58, Banner));
        WriteBmp(Path.Combine(output, "dialog.bmp"), Render(493, 312, Dialog));

        // The picture links show when the project is shared (1200 x 630).
        File.WriteAllBytes(Path.Combine(output, "og.png"), Png(Render(1200, 630, Og)));
    }

    /// <summary>The logo in a square of side <paramref name="s"/> at (x, y).</summary>
    static void Logo(DrawingContext dc, double x, double y, double s)
    {
        var small = s <= 24;
        var tiny = s <= 32;
        double U(double v) => v / 1024 * s;  // drawn on a 1024 grid

        // The tile: a rounded square, warm near the top, darker at the edges.
        var tile = new Rect(x, y, s, s);
        var background = new RadialGradientBrush(TileInner, TileOuter)
        {
            Center = new Point(0.5, 0.43),
            GradientOrigin = new Point(0.5, 0.43),
            RadiusX = 0.56,
            RadiusY = 0.56,
        };
        dc.DrawRoundedRectangle(background, null, tile, U(224), U(224));

        // The ring: 320 degrees, open at the upper right.
        var centre = new Point(x + s / 2, y + s / 2);
        var radius = U(small ? 300 : 286);
        var stroke = U(small ? 118 : tiny ? 96 : 74);
        var ring = new LinearGradientBrush(RingStart, RingEnd, new Point(0.2, 0.2), new Point(0.8, 0.8));
        var pen = new Pen(ring, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var figure = new PathFigure { StartPoint = OnCircle(centre, radius, -18) };
        figure.Segments.Add(new ArcSegment(OnCircle(centre, radius, -62), new Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
        dc.DrawGeometry(null, pen, new PathGeometry([figure]));

        // The accent in the gap: the island's colour.
        dc.DrawEllipse(new SolidColorBrush(IslandCyan), null, OnCircle(centre, radius, -40), U(small ? 70 : 40), U(small ? 70 : 40));

        // The two notches: the island, and the second notch under it.
        var cream = new SolidColorBrush(Cream);
        if (small)
        {
            // Too small for two: the island alone, larger.
            var w = U(420);
            var h = U(170);
            dc.DrawRoundedRectangle(cream, null, new Rect(centre.X - w / 2, centre.Y - h / 2, w, h), h / 2, h / 2);
            return;
        }
        var islandW = U(312);
        var islandH = U(tiny ? 120 : 100);
        var islandTop = centre.Y - U(tiny ? 122 : 104);
        dc.DrawRoundedRectangle(cream, null, new Rect(centre.X - islandW / 2, islandTop, islandW, islandH), islandH / 2, islandH / 2);
        var secondW = U(184);
        var secondH = U(tiny ? 84 : 66);
        var secondTop = islandTop + islandH + U(tiny ? 54 : 48);
        var softer = new SolidColorBrush(Color.FromArgb(0xA6, Cream.R, Cream.G, Cream.B));
        dc.DrawRoundedRectangle(softer, null, new Rect(centre.X - secondW / 2, secondTop, secondW, secondH), secondH / 2, secondH / 2);
    }

    static Point OnCircle(Point centre, double radius, double degrees)
    {
        var a = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Cos(a), centre.Y + radius * Math.Sin(a));
    }

    /// <summary>The installer's top banner: white for its title text, the logo at the right.</summary>
    static void Banner(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 493, 58));
        Logo(dc, 493 - 50, 7, 44);
    }

    /// <summary>The installer's welcome and finish pages: the logo on the warm dark panel at the left, white where the text goes.</summary>
    static void Dialog(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 493, 312));
        var panel = new Rect(0, 0, 164, 312);
        var warm = new RadialGradientBrush(TileInner, TileOuter) { Center = new Point(0.5, 0.36), GradientOrigin = new Point(0.5, 0.36), RadiusX = 0.95, RadiusY = 0.6 };
        dc.DrawRectangle(warm, null, panel);
        Logo(dc, (164 - 96) / 2.0, 84, 96);
        var name = new FormattedText("Nyatch", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 22, new SolidColorBrush(Cream), 1.0);
        dc.DrawText(name, new Point((164 - name.Width) / 2, 196));
        var line = new FormattedText("the notch for Windows", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, new SolidColorBrush(Color.FromArgb(0xB0, Cream.R, Cream.G, Cream.B)), 1.0);
        dc.DrawText(line, new Point((164 - line.Width) / 2, 228));
    }

    /// <summary>The share picture: the name and what it is, between a top notch and the second notch.</summary>
    static void Og(DrawingContext dc)
    {
        const double W = 1200, H = 630;
        dc.DrawRectangle(new SolidColorBrush(Hex("#0C0709")), null, new Rect(0, 0, W, H));
        var warm = new RadialGradientBrush(Color.FromArgb(0xFF, TileInner.R, TileInner.G, TileInner.B), Color.FromArgb(0, TileOuter.R, TileOuter.G, TileOuter.B))
        { Center = new Point(0.5, 0.42), GradientOrigin = new Point(0.5, 0.42), RadiusX = 0.62, RadiusY = 0.85 };
        dc.DrawRectangle(warm, null, new Rect(0, 0, W, H));

        var display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        FormattedText Words(string text, double size, FontWeight weight, Color colour) => new(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface(display, FontStyles.Normal, weight, FontStretches.Normal), size, new SolidColorBrush(colour), 1.0);
        var soft = Color.FromArgb(0xB8, Cream.R, Cream.G, Cream.B);
        var pill = new SolidColorBrush(Hex("#08080A"));
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), 1.5);

        // The top notch: flat against the top edge, with the time and a sound wave.
        var top = new Rect((W - 360) / 2, -28, 360, 92);
        dc.DrawRoundedRectangle(pill, edge, top, 28, 28);
        var time = Words("9:41", 26, FontWeights.SemiBold, Cream);
        dc.DrawText(time, new Point(top.Left + 34, 16));
        double[] bars = [10, 22, 14, 28, 18, 24, 12];
        for (var i = 0; i < bars.Length; i++)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(IslandCyan), null, new Rect(top.Right - 96 + i * 9, 33 - bars[i] / 2, 5, bars[i]), 2.5, 2.5);
        }

        // The name.
        Logo(dc, (W - 124) / 2, 150, 124);
        var name = Words("Nyatch", 92, FontWeights.SemiBold, Cream);
        dc.DrawText(name, new Point((W - name.Width) / 2, 286));
        var line = Words("The notch for Windows", 34, FontWeights.Normal, soft);
        dc.DrawText(line, new Point((W - line.Width) / 2, 404));
        var more = Words("Now playing  ·  Notifications  ·  Clipboard  ·  AI chat  ·  Notes  ·  Terminal", 21, FontWeights.Normal, Color.FromArgb(0x8C, Cream.R, Cream.G, Cream.B));
        dc.DrawText(more, new Point((W - more.Width) / 2, 462));

        // The second notch: flat against the bottom edge, with its paw prints and the date.
        var date = Words("Sat 22 Nov", 24, FontWeights.SemiBold, Cream);
        var bottomWidth = date.Width + 120;
        var bottom = new Rect((W - bottomWidth) / 2, H - 62, bottomWidth, 92);
        dc.DrawRoundedRectangle(pill, edge, bottom, 26, 26);
        var cyan = new SolidColorBrush(IslandCyan);
        void Paw(double cx, double cy, double size, double degrees)
        {
            dc.PushTransform(new RotateTransform(degrees, cx, cy));
            dc.DrawEllipse(cyan, null, new Point(cx, cy + 0.18 * size), 0.30 * size, 0.24 * size);
            (double X, double Y, double RX, double RY)[] toes = [(-0.36, -0.04, 0.11, 0.14), (-0.13, -0.30, 0.12, 0.155), (0.13, -0.30, 0.12, 0.155), (0.36, -0.04, 0.11, 0.14)];
            foreach (var toe in toes) dc.DrawEllipse(cyan, null, new Point(cx + toe.X * size, cy + toe.Y * size), toe.RX * size, toe.RY * size);
            dc.Pop();
        }
        var pawX = bottom.Left + 52;
        var pawY = H - 31;
        Paw(pawX - 7, pawY - 7, 17, -18);
        Paw(pawX + 7, pawY + 7, 17, 22);
        dc.DrawText(date, new Point(bottom.Left + 82, pawY - date.Height / 2));

        var site = Words("arkhins.com", 20, FontWeights.Normal, Color.FromArgb(0x80, Cream.R, Cream.G, Cream.B));
        dc.DrawText(site, new Point(W - site.Width - 40, H - 48));
    }

    static BitmapSource Render(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
        using (var dc = visual.RenderOpen()) draw(dc);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    static void WriteBmp(string path, BitmapSource bitmap)
    {
        // Windows Installer wants plain 24-bit bitmaps.
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>An .ico holding each size as a PNG (which Windows reads since Vista).</summary>
    static void WriteIco(string path, List<(int Size, byte[] Png)> images)
    {
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) w.Write(png);
    }

    /// <summary>The same logo as an SVG, for the web and anything else that scales.</summary>
    static string Svg()
    {
        static string P(Point p) => $"{p.X:0.#},{p.Y:0.#}";
        var c = new Point(512, 512);
        var start = OnCircle(c, 286, -18);
        var end = OnCircle(c, 286, -62);
        var dot = OnCircle(c, 286, -40);
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">
              <defs>
                <radialGradient id="tile" cx="50%" cy="43%" r="56%"><stop offset="0" stop-color="#2C1519"/><stop offset="1" stop-color="#130A0D"/></radialGradient>
                <linearGradient id="ring" x1="20%" y1="20%" x2="80%" y2="80%"><stop offset="0" stop-color="#D7263D"/><stop offset="1" stop-color="#FF6B5B"/></linearGradient>
              </defs>
              <rect width="1024" height="1024" rx="224" fill="url(#tile)"/>
              <path d="M{P(start)} A286,286 0 1 1 {P(end)}" fill="none" stroke="url(#ring)" stroke-width="74" stroke-linecap="round"/>
              <circle cx="{dot.X:0.#}" cy="{dot.Y:0.#}" r="40" fill="#4CC9F0"/>
              <rect x="356" y="408" width="312" height="100" rx="50" fill="#FFF2EF"/>
              <rect x="420" y="556" width="184" height="66" rx="33" fill="#FFF2EF" fill-opacity="0.65"/>
            </svg>
            """;
    }
}

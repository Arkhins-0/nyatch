using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nyatch.Core;

namespace Nyatch.ThemeEditor;

/// <summary>
/// A cover made from the pack's own pictures: up to four slots ticked in the list (idle when none is),
/// laid out on a 512 × 512 square, on the pack's colour or a plain dark background.
/// </summary>
internal sealed partial class MainWindow
{
    private bool _coverTint = true;

    /// <summary>For trying it from the command line: a cover from these slots.</summary>
    public void MakeCoverFrom(IEnumerable<string> names, bool tint)
    {
        _selected.Clear();
        foreach (var name in names) _selected.Add(name);
        _coverTint = tint;
        MakeCover();
    }

    private void MakeCover()
    {
        if (_pack is not { } pack)
        {
            Say("Open a theme first.");
            return;
        }
        // In the list's order, so the cover reads as the list does.
        var names = CharacterSlots.All.Select(s => s.Name).Where(n => _selected.Contains(n) && pack.Has(n)).Take(4).ToList();
        if (names.Count == 0 && pack.Has("idle")) names.Add("idle");
        var pictures = names.Select(n => Load(pack.PictureOf(n))).OfType<Picture>().ToList();
        if (pictures.Count == 0)
        {
            Say("Tick up to 4 slots with pictures in the list first.");
            return;
        }

        const int size = 512;
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            Brush back = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x22));
            if (_coverTint && pack.Manifest.AccentColour is { } hex)
            {
                var accent = (Color)ColorConverter.ConvertFromString(hex);
                back = new LinearGradientBrush(Shade(accent, 0.55), Shade(accent, 0.22), new Point(0, 0), new Point(1, 1));
            }
            draw.DrawRectangle(back, null, new Rect(0, 0, size, size));
            foreach (var (picture, cell) in pictures.Zip(Cells(pictures.Count, size)))
            {
                var frame = picture.Frames[0];
                var inset = new Rect(cell.X + 8, cell.Y + 8, cell.Width - 16, cell.Height - 16);
                var fit = Math.Min(inset.Width / frame.PixelWidth, inset.Height / frame.PixelHeight);
                double w = frame.PixelWidth * fit, h = frame.PixelHeight * fit;
                var at = new Rect(inset.X + (inset.Width - w) / 2, inset.Y + (inset.Height - h) / 2, w, h);
                // A sticker with its own background gets rounded corners, as Nyatch draws it.
                if (picture.Tile) draw.PushClip(new RectangleGeometry(at, Math.Min(w, h) * 0.12, Math.Min(w, h) * 0.12));
                draw.DrawImage(frame, at);
                if (picture.Tile) draw.Pop();
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var file = Path.Combine(Path.GetTempPath(), $"nyatch-cover-{Guid.NewGuid():N}.png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file)) encoder.Save(stream);
            pack.SetPicture("preview", file);
            Say($"Cover made from {string.Join(", ", names)}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
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
        Changed();
    }

    /// <summary>Where each picture goes: one fills the square; two side by side; three or four in a 2 × 2 grid (three: two above, one centred below).</summary>
    private static IEnumerable<Rect> Cells(int count, double size)
    {
        const double pad = 20, gap = 12;
        var inner = size - pad * 2;
        var half = (inner - gap) / 2;
        switch (count)
        {
            case 1:
                yield return new Rect(pad, pad, inner, inner);
                break;
            case 2:
                yield return new Rect(pad, pad + half / 2, half, half);
                yield return new Rect(pad + half + gap, pad + half / 2, half, half);
                break;
            default:
                yield return new Rect(pad, pad, half, half);
                yield return new Rect(pad + half + gap, pad, half, half);
                if (count == 3) yield return new Rect(pad + (half + gap) / 2, pad + half + gap, half, half);
                else
                {
                    yield return new Rect(pad, pad + half + gap, half, half);
                    yield return new Rect(pad + half + gap, pad + half + gap, half, half);
                }
                break;
        }
    }

    private static Color Shade(Color colour, double keep) =>
        Color.FromRgb((byte)(colour.R * keep), (byte)(colour.G * keep), (byte)(colour.B * keep));
}

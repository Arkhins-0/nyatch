using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nyatch.ThemeEditor;

/// <summary>
/// A pack picture as Nyatch draws it: every frame of a GIF put together (where each goes, and what is
/// cleared after it), the empty margin round all frames trimmed off, and whether it is a sticker with
/// its own background (Nyatch rounds those). A PNG is one frame.
/// </summary>
internal sealed class Picture
{
    public required string Path { get; init; }
    public required IReadOnlyList<BitmapSource> Frames { get; init; }
    public required IReadOnlyList<double> Delays { get; init; }  // seconds
    public int Width { get; init; }          // the file's own size
    public int Height { get; init; }
    public int TrimLeft { get; init; }       // where the trimmed part starts in the file
    public int TrimTop { get; init; }
    public int TrimmedWidth { get; init; }   // after the empty margin is cut
    public int TrimmedHeight { get; init; }
    public bool Tile { get; init; }          // solid in all four corners: its own background
    public long Bytes { get; init; }
    public int FramesInFile { get; init; }
    public bool IsGif { get; init; }

    public double Length => Delays.Sum();

    /// <summary>The same limit as Nyatch: it plays the first 120 frames.</summary>
    public const int MaxFrames = 120;

    /// <summary>Every frame of a file, whole and put together (Pbgra32), with its delays.</summary>
    public sealed record Composed(List<byte[]> Frames, List<double> Delays, int Width, int Height, int FramesInFile, bool IsGif);

    /// <param name="all">Every frame (for making it smaller), not only the 120 Nyatch plays.</param>
    public static Composed? Compose(string path, bool all = false)
    {
        try
        {
            BitmapDecoder decoder;
            using (var stream = File.OpenRead(path))
            {
                decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
            }
            var gif = decoder is GifBitmapDecoder;
            var first = decoder.Frames[0];
            int canvasW = first.PixelWidth, canvasH = first.PixelHeight;
            if (gif)
            {
                canvasW = Number(decoder.Metadata, "/logscrdesc/Width", canvasW);
                canvasH = Number(decoder.Metadata, "/logscrdesc/Height", canvasH);
            }
            if (canvasW <= 0 || canvasH <= 0 || canvasW > 4096 || canvasH > 4096) return null;

            var whole = new List<byte[]>();
            var delays = new List<double>();
            var canvas = new byte[canvasW * canvasH * 4];
            byte[]? saved = null;
            var count = gif ? (all ? decoder.Frames.Count : Math.Min(decoder.Frames.Count, MaxFrames)) : 1;
            for (var i = 0; i < count; i++)
            {
                var frame = decoder.Frames[i];
                int left = 0, top = 0, delay = 10, disposal = 0;
                if (gif && frame.Metadata is BitmapMetadata meta)
                {
                    left = Number(meta, "/imgdesc/Left", 0);
                    top = Number(meta, "/imgdesc/Top", 0);
                    delay = Number(meta, "/grctlext/Delay", 10);
                    disposal = Number(meta, "/grctlext/Disposal", 0);
                }
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                int fw = converted.PixelWidth, fh = converted.PixelHeight;
                var pixels = new byte[fw * fh * 4];
                converted.CopyPixels(pixels, fw * 4, 0);
                if (disposal == 3) saved = (byte[])canvas.Clone();
                for (var y = 0; y < fh && top + y < canvasH; y++)
                {
                    for (var x = 0; x < fw && left + x < canvasW; x++)
                    {
                        var from = (y * fw + x) * 4;
                        var to = ((top + y) * canvasW + left + x) * 4;
                        int alpha = pixels[from + 3];
                        if (alpha == 255) Buffer.BlockCopy(pixels, from, canvas, to, 4);
                        else if (alpha != 0)
                        {
                            for (var c = 0; c < 4; c++) canvas[to + c] = (byte)(pixels[from + c] + canvas[to + c] * (255 - alpha) / 255);
                        }
                    }
                }
                whole.Add((byte[])canvas.Clone());
                delays.Add((delay < 2 ? 10 : delay) / 100.0);
                if (disposal == 2)
                {
                    for (var y = 0; y < fh && top + y < canvasH; y++)
                    {
                        if (left < canvasW) Array.Clear(canvas, ((top + y) * canvasW + left) * 4, Math.Min(fw, canvasW - left) * 4);
                    }
                }
                else if (disposal == 3 && saved is not null) canvas = saved;
            }
            return new Composed(whole, delays, canvasW, canvasH, decoder.Frames.Count, gif);
        }
        catch (Exception error) when (error is IOException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public static Picture? Load(string path)
    {
        if (Compose(path) is not { } composed) return null;
        return Build(path, composed.Frames, composed.Width, composed.Height, composed.Delays, new FileInfo(path).Length, composed.FramesInFile, composed.IsGif, composed.Width, composed.Height, 0, 0);
    }

    /// <summary>The picture as it would be with this part kept (in trimmed pixels): for showing a crop before it is made.</summary>
    public Picture Cropped(Int32Rect part)
    {
        var frames = new List<byte[]>();
        foreach (var frame in Frames)
        {
            var bytes = new byte[part.Width * part.Height * 4];
            frame.CopyPixels(part, bytes, part.Width * 4, 0);
            frames.Add(bytes);
        }
        return Build(Path, frames, part.Width, part.Height, Delays.ToList(), Bytes, FramesInFile, IsGif, part.Width, part.Height, TrimLeft + part.X, TrimTop + part.Y);
    }

    /// <summary>Trim the empty margin round all frames (as Nyatch does) and make the picture.</summary>
    private static Picture Build(string path, List<byte[]> whole, int canvasW, int canvasH, List<double> delays, long bytes, int framesInFile, bool gif, int width, int height, int offsetX, int offsetY)
    {
        int minX = canvasW, minY = canvasH, maxX = -1, maxY = -1;
        foreach (var frame in whole)
        {
            for (var y = 0; y < canvasH; y++)
            {
                for (var x = 0; x < canvasW; x++)
                {
                    if (frame[(y * canvasW + x) * 4 + 3] <= 12) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        if (maxX < 0)
        {
            minX = minY = 0;
            maxX = canvasW - 1;
            maxY = canvasH - 1;
        }
        int cropW = maxX - minX + 1, cropH = maxY - minY + 1;
        var frames = new List<BitmapSource>();
        foreach (var frame in whole)
        {
            var crop = new byte[cropW * cropH * 4];
            for (var y = 0; y < cropH; y++) Buffer.BlockCopy(frame, ((minY + y) * canvasW + minX) * 4, crop, y * cropW * 4, cropW * 4);
            var bitmap = BitmapSource.Create(cropW, cropH, 96, 96, PixelFormats.Pbgra32, null, crop, cropW * 4);
            bitmap.Freeze();
            frames.Add(bitmap);
        }
        var firstCrop = new byte[cropW * cropH * 4];
        frames[0].CopyPixels(firstCrop, cropW * 4, 0);
        byte AlphaAt(int x, int y) => firstCrop[(y * cropW + x) * 4 + 3];
        var tile = cropW > 2 && cropH > 2 && AlphaAt(0, 0) > 200 && AlphaAt(cropW - 1, 0) > 200 && AlphaAt(0, cropH - 1) > 200 && AlphaAt(cropW - 1, cropH - 1) > 200;
        return new Picture
        {
            Path = path, Frames = frames, Delays = delays, Width = width, Height = height,
            TrimLeft = offsetX + minX, TrimTop = offsetY + minY, TrimmedWidth = cropW, TrimmedHeight = cropH,
            Tile = tile, Bytes = bytes, FramesInFile = framesInFile, IsGif = gif,
        };
    }

    private static int Number(BitmapMetadata? meta, string query, int fallback)
    {
        try
        {
            return meta?.GetQuery(query) switch
            {
                ushort u => u,
                byte b => b,
                uint i => (int)i,
                int i => i,
                _ => fallback,
            };
        }
        catch (Exception error) when (error is NotSupportedException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return fallback;
        }
    }
}

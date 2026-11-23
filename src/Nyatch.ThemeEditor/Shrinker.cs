using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nyatch.ThemeEditor;

/// <summary>What making a picture smaller did.</summary>
internal sealed record Shrunk(long Before, long After, int WidthBefore, int HeightBefore, int Width, int Height, int FramesBefore, int Frames);

/// <summary>
/// Makes a picture smaller for Nyatch: the empty margin cut off (Nyatch cuts it anyway), then sized down
/// to the largest it is drawn at on a 200% screen, every frame kept with its timing. A GIF still over the
/// limit then keeps every other frame (each shown twice as long, so it runs at the same speed), and then
/// gets smaller again, until it fits. Windows decodes the picture; the sizing, the colours and the GIF
/// are worked out here, so nothing else is needed (and runs on any thread).
/// </summary>
internal static class Shrinker
{
    /// <summary>A frame being worked on: premultiplied BGRA, and how long it shows (hundredths of a second).</summary>
    private sealed class Frame(byte[] pixels, int delay)
    {
        public byte[] Pixels { get; set; } = pixels;
        public int Delay { get; set; } = delay;
    }

    public static Shrunk Shrink(string source, string target, int side, long limit)
    {
        var before = new FileInfo(source).Length;
        var composed = Picture.Compose(source, all: true) ?? throw new InvalidDataException("The picture could not be read.");
        var gif = composed.IsGif;
        int width = composed.Width, height = composed.Height;
        var frames = composed.Frames.Zip(composed.Delays, (p, d) => new Frame(p, (int)Math.Round(d * 100))).ToList();
        int widthBefore = width, heightBefore = height, framesBefore = composed.FramesInFile;

        // The part any frame draws on.
        var (x0, y0, x1, y1) = UsedPart(frames, width, height);
        if (x1 - x0 < width || y1 - y0 < height)
        {
            foreach (var frame in frames) frame.Pixels = Cut(frame.Pixels, width, x0, y0, x1 - x0, y1 - y0);
            width = x1 - x0;
            height = y1 - y0;
        }

        var scale = Math.Min(1.0, (double)side / Math.Max(width, height));
        (width, height) = Resize(frames, width, height, scale);
        // Nyatch plays 120 frames: more are put together so the whole movement plays.
        while (frames.Count > Picture.MaxFrames) HalveFrames(frames);
        var size = Save(frames, width, height, target, gif);
        // Still too big: fewer frames and smaller in turn, so neither goes too far.
        for (var tries = 0; gif && size > limit && tries < 8; tries++)
        {
            if (tries % 2 == 1 && frames.Count > 12) HalveFrames(frames);
            else (width, height) = Resize(frames, width, height, 0.85);
            size = Save(frames, width, height, target, gif);
        }
        return new Shrunk(before, size, widthBefore, heightBefore, width, height, framesBefore, frames.Count);
    }

    private static (int X0, int Y0, int X1, int Y1) UsedPart(List<Frame> frames, int width, int height)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        foreach (var frame in frames)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (frame.Pixels[(y * width + x) * 4 + 3] <= 12) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        return maxX < 0 ? (0, 0, width, height) : (minX, minY, maxX + 1, maxY + 1);
    }

    private static byte[] Cut(byte[] pixels, int width, int x, int y, int w, int h)
    {
        var cut = new byte[w * h * 4];
        for (var row = 0; row < h; row++) Buffer.BlockCopy(pixels, ((y + row) * width + x) * 4, cut, row * w * 4, w * 4);
        return cut;
    }

    /// <summary>Every frame sized down by area averaging (each new pixel the mean of the ones it covers).</summary>
    private static (int Width, int Height) Resize(List<Frame> frames, int width, int height, double scale)
    {
        if (scale >= 0.999) return (width, height);
        var w = Math.Max(1, (int)Math.Round(width * scale));
        var h = Math.Max(1, (int)Math.Round(height * scale));
        foreach (var frame in frames) frame.Pixels = AreaAverage(frame.Pixels, width, height, w, h);
        return (w, h);
    }

    private static byte[] AreaAverage(byte[] source, int width, int height, int w, int h)
    {
        var result = new byte[w * h * 4];
        double sx = (double)width / w, sy = (double)height / h;
        for (var y = 0; y < h; y++)
        {
            double top = y * sy, bottom = (y + 1) * sy;
            for (var x = 0; x < w; x++)
            {
                double left = x * sx, right = (x + 1) * sx;
                double b = 0, g = 0, r = 0, a = 0, total = 0;
                for (var py = (int)top; py < Math.Min(height, (int)Math.Ceiling(bottom)); py++)
                {
                    var wy = Math.Min(bottom, py + 1) - Math.Max(top, py);
                    for (var px = (int)left; px < Math.Min(width, (int)Math.Ceiling(right)); px++)
                    {
                        var weight = wy * (Math.Min(right, px + 1) - Math.Max(left, px));
                        var at = (py * width + px) * 4;
                        b += source[at] * weight;
                        g += source[at + 1] * weight;
                        r += source[at + 2] * weight;
                        a += source[at + 3] * weight;
                        total += weight;
                    }
                }
                var to = (y * w + x) * 4;
                if (total <= 0) continue;
                result[to] = (byte)Math.Round(b / total);
                result[to + 1] = (byte)Math.Round(g / total);
                result[to + 2] = (byte)Math.Round(r / total);
                result[to + 3] = (byte)Math.Round(a / total);
            }
        }
        return result;
    }

    /// <summary>Keep every other frame; each kept frame also shows for the one dropped after it.</summary>
    private static void HalveFrames(List<Frame> frames)
    {
        for (var i = frames.Count - 1; i >= 1; i--)
        {
            if (i % 2 == 0) continue;
            frames[i - 1].Delay = Math.Max(2, frames[i - 1].Delay) + Math.Max(2, frames[i].Delay);
            frames.RemoveAt(i);
        }
    }

    private static long Save(List<Frame> frames, int width, int height, string target, bool gif)
    {
        if (gif) File.WriteAllBytes(target, Gif(frames, width, height));
        else
        {
            var encoder = new PngBitmapEncoder();
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, frames[0].Pixels, width * 4);
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(target);
            encoder.Save(file);
        }
        return new FileInfo(target).Length;
    }

    // ---------- GIF ----------

    /// <summary>
    /// An animated GIF of whole frames: each with its own 255 colours (median cut) and one see-through
    /// index, cleared after it shows (so a frame never shows through the next), looping for ever.
    /// </summary>
    private static byte[] Gif(List<Frame> frames, int width, int height)
    {
        var output = new MemoryStream();
        output.Write("GIF89a"u8);
        Short(output, width);
        Short(output, height);
        output.WriteByte(0);  // no global colour table
        output.WriteByte(0);
        output.WriteByte(0);
        // Looping for ever.
        output.Write([0x21, 0xFF, 11]);
        output.Write("NETSCAPE2.0"u8);
        output.Write([3, 1, 0, 0, 0]);
        const byte Clear = 255;  // the see-through index
        foreach (var frame in frames)
        {
            var (palette, indexes) = Quantize(frame.Pixels, width * height, Clear);
            // Graphic control: cleared afterwards (2), see-through index 255, its delay.
            output.Write([0x21, 0xF9, 4, (2 << 2) | 1]);
            Short(output, Math.Max(2, frame.Delay));
            output.WriteByte(Clear);
            output.WriteByte(0);
            output.WriteByte(0x2C);
            Short(output, 0);
            Short(output, 0);
            Short(output, width);
            Short(output, height);
            output.WriteByte(0x80 | 7);  // a local table of 256 colours
            output.Write(palette);
            output.WriteByte(8);
            var data = Cropper.Encode(indexes, 8);
            for (var i = 0; i < data.Length; i += 255)
            {
                var size = Math.Min(255, data.Length - i);
                output.WriteByte((byte)size);
                output.Write(data, i, size);
            }
            output.WriteByte(0);
        }
        output.WriteByte(0x3B);
        return output.ToArray();
    }

    private static void Short(Stream output, int value)
    {
        output.WriteByte((byte)(value & 0xFF));
        output.WriteByte((byte)(value >> 8));
    }

    /// <summary>
    /// A frame's colours brought down to 255 by median cut (over 15-bit colours, weighted by how often each
    /// appears), and each pixel's index. Half see-through pixels become solid or clear: a GIF has nothing between.
    /// </summary>
    private static (byte[] Palette, byte[] Indexes) Quantize(byte[] pixels, int count, byte clear)
    {
        var counts = new int[32768];
        var keys = new int[count];
        for (var i = 0; i < count; i++)
        {
            var a = pixels[i * 4 + 3];
            if (a < 128)
            {
                keys[i] = -1;
                continue;
            }
            // Back from premultiplied, to the colour itself.
            int b = Math.Min(255, pixels[i * 4] * 255 / a), g = Math.Min(255, pixels[i * 4 + 1] * 255 / a), r = Math.Min(255, pixels[i * 4 + 2] * 255 / a);
            var key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
            keys[i] = key;
            counts[key]++;
        }

        // Boxes of colours, split at the weighted middle of their widest side until there are 255. Each box
        // knows its widest side from when it was made, so choosing the next to split is quick.
        static (List<int> Keys, int Axis, int Span) Measured(List<int> keys)
        {
            var (axis, span) = (0, -1);
            for (var c = 0; c < 3; c++)
            {
                var shift = 10 - c * 5;
                int lo = 31, hi = 0;
                foreach (var k in keys)
                {
                    var v = (k >> shift) & 31;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
                if (hi - lo > span) (axis, span) = (c, hi - lo);
            }
            return (keys, axis, keys.Count < 2 ? 0 : span);
        }
        var boxes = new List<(List<int> Keys, int Axis, int Span)> { Measured(Enumerable.Range(0, 32768).Where(k => counts[k] > 0).ToList()) };
        while (boxes.Count < 255)
        {
            var widest = -1;
            for (var i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Span > 0 && (widest < 0 || boxes[i].Span > boxes[widest].Span)) widest = i;
            }
            if (widest < 0) break;
            var (box, axis, _) = boxes[widest];
            var by = 10 - axis * 5;
            box.Sort((p, q) => ((p >> by) & 31).CompareTo((q >> by) & 31));
            long half = box.Sum(k => (long)counts[k]) / 2, run = 0;
            var cut = 1;
            for (; cut < box.Count - 1; cut++)
            {
                run += counts[box[cut - 1]];
                if (run >= half) break;
            }
            boxes[widest] = Measured(box.GetRange(0, cut));
            boxes.Add(Measured(box.GetRange(cut, box.Count - cut)));
        }

        var palette = new byte[256 * 3];
        var lookup = new byte[32768];
        for (var i = 0; i < boxes.Count; i++)
        {
            long r = 0, g = 0, b = 0, n = 0;
            foreach (var k in boxes[i].Keys)
            {
                long weight = counts[k];
                r += ((k >> 10) & 31) * weight;
                g += ((k >> 5) & 31) * weight;
                b += (k & 31) * weight;
                n += weight;
                lookup[k] = (byte)i;
            }
            if (n == 0) continue;
            palette[i * 3] = (byte)(r * 255 / (31 * n));
            palette[i * 3 + 1] = (byte)(g * 255 / (31 * n));
            palette[i * 3 + 2] = (byte)(b * 255 / (31 * n));
        }
        var indexes = new byte[count];
        for (var i = 0; i < count; i++) indexes[i] = keys[i] < 0 ? clear : lookup[keys[i]];
        return (palette, indexes);
    }
}

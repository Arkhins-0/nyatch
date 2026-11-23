using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Nyatch.ThemeEditor;

/// <summary>
/// Cuts a part out of a picture file. A GIF is cut frame by frame on its colour indexes, so its colours,
/// transparency, delays and looping stay exactly as they were (nothing is re-coloured). A PNG is cut and
/// saved again, also without loss.
/// </summary>
internal static class Cropper
{
    public static void Crop(string source, string target, Int32Rect part)
    {
        var ext = PackProject.PictureExtension(source) ?? throw new InvalidDataException("Not a PNG or GIF.");
        if (ext == ".gif") File.WriteAllBytes(target, CropGif(File.ReadAllBytes(source), part));
        else CropPng(source, target, part);
    }

    private static void CropPng(string source, string target, Int32Rect part)
    {
        BitmapFrame frame;
        using (var stream = File.OpenRead(source))
        {
            frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }
        part = Clamp(part, frame.PixelWidth, frame.PixelHeight);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(frame, part)));
        using var file = File.Create(target);
        encoder.Save(file);
    }

    private static Int32Rect Clamp(Int32Rect part, int width, int height)
    {
        var x = Math.Clamp(part.X, 0, width - 1);
        var y = Math.Clamp(part.Y, 0, height - 1);
        return new Int32Rect(x, y, Math.Clamp(part.Width, 1, width - x), Math.Clamp(part.Height, 1, height - y));
    }

    // ---------- GIF ----------

    public static byte[] CropGif(byte[] gif, Int32Rect part)
    {
        var read = new Reader(gif);
        var output = new MemoryStream();
        var head = read.Bytes(6);
        if (head[0] != 'G' || head[1] != 'I' || head[2] != 'F') throw new InvalidDataException("Not a GIF.");
        output.Write("GIF89a"u8);
        int screenW = read.Short(), screenH = read.Short();
        var packed = read.Byte();
        var background = read.Byte();
        var aspect = read.Byte();
        part = Clamp(part, screenW, screenH);
        Short(output, part.Width);
        Short(output, part.Height);
        output.WriteByte(packed);
        output.WriteByte(background);
        output.WriteByte(aspect);
        if ((packed & 0x80) != 0) output.Write(read.Bytes(3 << ((packed & 7) + 1)));

        byte[]? control = null;  // the graphic control block waiting for its image
        while (true)
        {
            var kind = read.Byte();
            if (kind == 0x3B) break;
            if (kind == 0x21)
            {
                var label = read.Byte();
                var blocks = read.Blocks();
                if (label == 0xF9) control = blocks.Count > 0 && blocks[0].Length >= 4 ? blocks[0] : null;
                else if (label == 0xFF) Extension(output, label, blocks);  // looping and the like
                // Comments and plain text are left out.
                continue;
            }
            if (kind != 0x2C) throw new InvalidDataException("A GIF part that cannot be read.");

            int left = read.Short(), top = read.Short(), width = read.Short(), height = read.Short();
            var flags = read.Byte();
            var table = (flags & 0x80) != 0 ? read.Bytes(3 << ((flags & 7) + 1)) : null;
            var codeSize = read.Byte();
            var data = Join(read.Blocks());
            var pixels = Decode(data, codeSize, width * height);
            if ((flags & 0x40) != 0) pixels = Deinterlace(pixels, width, height);

            // The part of this frame inside the crop.
            int x0 = Math.Max(left, part.X), y0 = Math.Max(top, part.Y);
            int x1 = Math.Min(left + width, part.X + part.Width), y1 = Math.Min(top + height, part.Y + part.Height);
            byte[] kept;
            int keptW, keptH, keptX, keptY;
            if (x1 > x0 && y1 > y0)
            {
                keptW = x1 - x0;
                keptH = y1 - y0;
                keptX = x0 - part.X;
                keptY = y0 - part.Y;
                kept = new byte[keptW * keptH];
                for (var y = 0; y < keptH; y++) Buffer.BlockCopy(pixels, (y0 - top + y) * width + (x0 - left), kept, y * keptW, keptW);
            }
            else
            {
                // Wholly outside: one see-through pixel keeps the frame's time.
                control = control is null ? [0, 10, 0, 0] : (byte[])control.Clone();
                if ((control[0] & 1) == 0) control[3] = 0;
                control[0] = (byte)((control[0] & 0xE2) | (1 << 2) | 1);
                keptW = keptH = 1;
                keptX = keptY = 0;
                kept = [control[3]];
            }
            if (control is not null) Extension(output, 0xF9, [control]);
            control = null;
            output.WriteByte(0x2C);
            Short(output, keptX);
            Short(output, keptY);
            Short(output, keptW);
            Short(output, keptH);
            output.WriteByte((byte)(flags & ~0x40));
            if (table is not null) output.Write(table);
            output.WriteByte(codeSize);
            Blocks(output, Encode(kept, codeSize));
        }
        output.WriteByte(0x3B);
        return output.ToArray();
    }

    private static void Short(Stream output, int value)
    {
        output.WriteByte((byte)(value & 0xFF));
        output.WriteByte((byte)(value >> 8));
    }

    private static void Extension(Stream output, byte label, List<byte[]> blocks)
    {
        output.WriteByte(0x21);
        output.WriteByte(label);
        foreach (var block in blocks)
        {
            output.WriteByte((byte)block.Length);
            output.Write(block);
        }
        output.WriteByte(0);
    }

    private static void Blocks(Stream output, byte[] data)
    {
        for (var i = 0; i < data.Length; i += 255)
        {
            var size = Math.Min(255, data.Length - i);
            output.WriteByte((byte)size);
            output.Write(data, i, size);
        }
        output.WriteByte(0);
    }

    private static byte[] Join(List<byte[]> blocks)
    {
        var all = new MemoryStream();
        foreach (var block in blocks) all.Write(block);
        return all.ToArray();
    }

    private static byte[] Deinterlace(byte[] pixels, int width, int height)
    {
        var rows = new byte[pixels.Length];
        var row = 0;
        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (var y = start; y < height; y += step, row++) Buffer.BlockCopy(pixels, row * width, rows, y * width, width);
        }
        return rows;
    }

    /// <summary>GIF's LZW, unpacked to colour indexes.</summary>
    public static byte[] Decode(byte[] data, int minimum, int count)
    {
        var output = new byte[count];
        int clear = 1 << minimum, end = clear + 1, size = minimum + 1, next = clear + 2, old = -1, at = 0;
        var prefix = new short[4096];
        var suffix = new byte[4096];
        var stack = new byte[4097];
        for (var i = 0; i < clear; i++) suffix[i] = (byte)i;
        byte first = 0;
        int bits = 0, held = 0, pos = 0;
        while (at < count)
        {
            while (held < size)
            {
                if (pos >= data.Length) return output;
                bits |= data[pos++] << held;
                held += 8;
            }
            var code = bits & ((1 << size) - 1);
            bits >>= size;
            held -= size;
            if (code == clear)
            {
                size = minimum + 1;
                next = clear + 2;
                old = -1;
                continue;
            }
            if (code == end) break;
            if (old == -1)
            {
                if (code >= clear) break;
                output[at++] = suffix[code];
                old = code;
                first = (byte)code;
                continue;
            }
            if (code > next) break;
            var read = code;
            var sp = 0;
            if (code == next)
            {
                stack[sp++] = first;
                code = old;
            }
            while (code >= clear)
            {
                stack[sp++] = suffix[code];
                code = prefix[code];
            }
            first = suffix[code];
            stack[sp++] = first;
            while (sp > 0 && at < count) output[at++] = stack[--sp];
            if (next < 4096)
            {
                prefix[next] = (short)old;
                suffix[next] = first;
                next++;
                if (next == 1 << size && size < 12) size++;
            }
            old = read;
        }
        return output;
    }

    /// <summary>Colour indexes packed with GIF's LZW.</summary>
    public static byte[] Encode(byte[] pixels, int minimum)
    {
        var output = new List<byte>();
        int bits = 0, held = 0;
        void Put(int code, int size)
        {
            bits |= code << held;
            held += size;
            while (held >= 8)
            {
                output.Add((byte)(bits & 0xFF));
                bits >>= 8;
                held -= 8;
            }
        }
        int clear = 1 << minimum, end = clear + 1, size = minimum + 1, next = clear + 2;
        var table = new Dictionary<int, int>();
        Put(clear, size);
        if (pixels.Length > 0)
        {
            int current = pixels[0];
            for (var i = 1; i < pixels.Length; i++)
            {
                int k = pixels[i];
                var key = (current << 8) | k;
                if (table.TryGetValue(key, out var code))
                {
                    current = code;
                    continue;
                }
                Put(current, size);
                if (next >= 1 << size && size < 12) size++;
                if (next < 4096) table[key] = next++;
                else
                {
                    Put(clear, size);
                    table.Clear();
                    size = minimum + 1;
                    next = clear + 2;
                }
                current = k;
            }
            Put(current, size);
            if (next >= 1 << size && size < 12) size++;
        }
        Put(end, size);
        if (held > 0) output.Add((byte)(bits & 0xFF));
        return output.ToArray();
    }

    private sealed class Reader(byte[] data)
    {
        private int _at;

        public byte Byte() => _at < data.Length ? data[_at++] : throw new InvalidDataException("The GIF ends too soon.");
        public int Short() => Byte() | (Byte() << 8);

        public byte[] Bytes(int count)
        {
            if (_at + count > data.Length) throw new InvalidDataException("The GIF ends too soon.");
            var bytes = data.AsSpan(_at, count).ToArray();
            _at += count;
            return bytes;
        }

        public List<byte[]> Blocks()
        {
            var blocks = new List<byte[]>();
            while (true)
            {
                var size = Byte();
                if (size == 0) return blocks;
                blocks.Add(Bytes(size));
            }
        }
    }
}

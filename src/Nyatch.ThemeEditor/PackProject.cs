using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nyatch.Core;

namespace Nyatch.ThemeEditor;

/// <summary>What a check found: a problem stops the pack being made; a note does not.</summary>
internal sealed record Finding(bool Problem, string Text, string? Slot = null);

/// <summary>
/// A pack being made: a folder with pack.json, the cover (preview.png or .gif), a font if it has one (font.ttf or font.otf), and a
/// picture for each slot it fills, named for the slot (time-morning.gif). The same folder MakePack
/// and Nyatch's tools read.
/// </summary>
internal sealed class PackProject
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public string Folder { get; }
    public CharacterPack Manifest { get; set; }

    private PackProject(string folder, CharacterPack manifest)
    {
        Folder = folder;
        Manifest = manifest;
    }

    /// <summary>Open a pack's folder; one without pack.json is started as a new pack named after the folder.</summary>
    public static PackProject Open(string folder)
    {
        Directory.CreateDirectory(folder);
        var file = System.IO.Path.Combine(folder, "pack.json");
        CharacterPack? manifest = null;
        if (File.Exists(file))
        {
            var bytes = File.ReadAllBytes(file);
            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            try
            {
                manifest = JsonSerializer.Deserialize<CharacterPack>(bytes.AsSpan(start), Json);
            }
            catch (JsonException)
            {
                // Started again below; the old file is kept beside it.
                File.Copy(file, file + ".broken", overwrite: true);
            }
        }
        manifest ??= new CharacterPack(PlainId(System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'))), System.IO.Path.GetFileName(folder.TrimEnd('\\', '/')));
        var project = new PackProject(folder, manifest);
        return project;
    }

    /// <summary>A plain pack id from any name: a-z, 0-9 and -.</summary>
    public static string PlainId(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') text.Append(c);
            else if (text.Length > 0 && text[^1] != '-') text.Append('-');
        }
        var id = text.ToString().Trim('-');
        if (id.Length > 40) id = id[..40].Trim('-');
        return id.Length == 0 ? "my-theme" : id;
    }

    public void Save()
    {
        // (Themes have no licence: one from an older theme is left out.)
        var manifest = Manifest with { Pictures = 0, Covers = null, Size = 0, Sha256 = "", File = "", Preview = "", Licence = "" };
        // Only what the maker wrote: the fields the making fills in (and any left empty) are left out.
        var json = JsonSerializer.SerializeToNode(manifest, Json)!.AsObject();
        foreach (var empty in json.Where(p => p.Value?.GetValueKind() == JsonValueKind.String && p.Value.GetValue<string>().Length == 0).Select(p => p.Key).ToList()) json.Remove(empty);
        File.WriteAllText(System.IO.Path.Combine(Folder, "pack.json"), json.ToJsonString(Json) + Environment.NewLine, new UTF8Encoding(false));
    }

    // ---------- The font ----------

    /// <summary>The theme's font file (font.ttf or font.otf), or null.</summary>
    public string? FontPath => CharacterPacks.FontFiles.Select(f => System.IO.Path.Combine(Folder, f)).FirstOrDefault(File.Exists);

    /// <summary>A font file (TrueType or OpenType) as the theme's font, with its family name; replaces one there.</summary>
    public string SetFont(string source)
    {
        var bytes = File.ReadAllBytes(source);
        if (!CharacterPacks.LooksLikeFont(bytes)) throw new InvalidDataException($"{System.IO.Path.GetFileName(source)} is not a TrueType or OpenType font.");
        if (bytes.Length > CharacterPacks.MaxFont) throw new InvalidDataException($"The font is over {CharacterPacks.MaxFont / 1024 / 1024} MB.");
        var family = FontFamilyName(source) ?? throw new InvalidDataException("The font's name could not be read.");
        RemoveFont();
        var ext = bytes[0] == 'O' ? ".otf" : ".ttf";
        File.WriteAllBytes(System.IO.Path.Combine(Folder, "font" + ext), bytes);
        Manifest = Manifest with { Font = family };
        Save();
        return family;
    }

    public void RemoveFont()
    {
        foreach (var f in CharacterPacks.FontFiles)
        {
            var path = System.IO.Path.Combine(Folder, f);
            if (File.Exists(path)) File.Delete(path);
        }
        Manifest = Manifest with { Font = "" };
    }

    /// <summary>A font file's family name, as Windows lists it.</summary>
    public static string? FontFamilyName(string path)
    {
        try
        {
            var face = new System.Windows.Media.GlyphTypeface(new Uri(System.IO.Path.GetFullPath(path)));
            var names = face.Win32FamilyNames;
            return names.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out var english) ? english : names.Values.FirstOrDefault();
        }
        catch (Exception error) when (error is IOException or FileFormatException or ArgumentException or UriFormatException)
        {
            return null;
        }
    }

    // ---------- Pictures ----------

    /// <summary>A name's own picture in the folder (GIF first, as Nyatch looks), or null.</summary>
    public string? PictureOf(string name)
    {
        foreach (var ext in new[] { ".gif", ".png" })
        {
            var path = System.IO.Path.Combine(Folder, name + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public bool Has(string name) => PictureOf(name) is not null;

    /// <summary>What a slot would show: its own picture, else the first of its fallbacks the pack has.</summary>
    public (string Name, string Path)? Shown(CharacterSlot slot) =>
        CharacterSlots.Resolve(slot, Has) is { } name ? (name, PictureOf(name)!) : null;

    /// <summary>Put a picture (PNG or GIF) in a slot (or the cover, "preview"), replacing what was there.</summary>
    public void SetPicture(string name, string source)
    {
        var ext = PictureExtension(source) ?? throw new InvalidDataException($"{System.IO.Path.GetFileName(source)} is not a PNG or GIF picture.");
        var target = System.IO.Path.Combine(Folder, name + ext);
        if (System.IO.Path.GetFullPath(source).Equals(System.IO.Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
        // Copied first, so a failed copy leaves the old picture.
        var draft = target + ".new";
        File.Copy(source, draft, overwrite: true);
        RemovePicture(name);
        File.Move(draft, target);
        if (Directory.Exists(Originals) && Directory.GetFileSystemEntries(Originals).Length == 0) Directory.Delete(Originals);
    }

    public void RemovePicture(string name)
    {
        RemoveFiles(Folder, name);
        // A crop's original goes with the picture it was cut from.
        RemoveFiles(Originals, name);
    }

    private static void RemoveFiles(string folder, string name)
    {
        foreach (var ext in new[] { ".gif", ".png" })
        {
            var path = System.IO.Path.Combine(folder, name + ext);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---------- Crops ----------

    /// <summary>Where the uncut pictures of cropped slots are kept. Not part of the pack: only files beside pack.json are packed.</summary>
    public string Originals => System.IO.Path.Combine(Folder, "originals");

    /// <summary>The uncut picture of a cropped slot, or null if it is not cropped.</summary>
    public string? OriginalOf(string name)
    {
        foreach (var ext in new[] { ".gif", ".png" })
        {
            var path = System.IO.Path.Combine(Originals, name + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public bool IsCropped(string name) => OriginalOf(name) is not null;

    /// <summary>Cut a slot's picture to a part of it (in the uncut file's pixels). The uncut picture is kept, so it can be undone or cut again.</summary>
    public void Crop(string name, System.Windows.Int32Rect part)
    {
        var current = PictureOf(name) ?? throw new InvalidDataException($"{name} has no picture to crop.");
        var original = OriginalOf(name);
        if (original is null)
        {
            Directory.CreateDirectory(Originals);
            original = System.IO.Path.Combine(Originals, System.IO.Path.GetFileName(current));
            File.Copy(current, original);
        }
        var target = System.IO.Path.Combine(Folder, name + System.IO.Path.GetExtension(original));
        var draft = target + ".new";
        Cropper.Crop(original, draft, part);
        RemoveFiles(Folder, name);
        File.Move(draft, target);
    }

    // ---------- Smaller pictures ----------

    /// <summary>The longest side a picture needs: twice the largest place that shows it (a 200% screen). The cover needs 512.</summary>
    public int BestSide(string name)
    {
        if (name == "preview") return 512;
        var sides = CharacterSlots.All
            .Where(s => s.Name == name || (!Has(s.Name) && CharacterSlots.Resolve(s, Has) == name))
            .Select(s => Math.Max(s.Width, s.Height) * 2)
            .ToList();
        return sides.Count == 0 ? 264 : sides.Max();
    }

    /// <summary>Whether a picture is over the limit, or much larger than Nyatch ever draws it.</summary>
    public bool TooBig(string name)
    {
        if (PictureOf(name) is not { } path) return false;
        if (new FileInfo(path).Length > CharacterPacks.MaxPicture) return true;
        return Picture.Load(path) is { } picture && (picture.FramesInFile > Picture.MaxFrames || Math.Max(picture.TrimmedWidth, picture.TrimmedHeight) > BestSide(name) * 1.25);
    }

    /// <summary>Make a picture smaller (see Shrinker). Its first picture is kept, so Restore original undoes it.</summary>
    public Shrunk Shrink(string name)
    {
        var current = PictureOf(name) ?? throw new InvalidDataException($"{name} has no picture.");
        var draft = current + ".new";
        var shrunk = Shrinker.Shrink(current, draft, BestSide(name), CharacterPacks.MaxPicture);
        // Kept only if it is smaller, or the old one was too many frames for Nyatch to play.
        if (shrunk.After >= shrunk.Before && shrunk.FramesBefore <= Picture.MaxFrames)
        {
            File.Delete(draft);
            return shrunk with { After = shrunk.Before, Width = shrunk.WidthBefore, Height = shrunk.HeightBefore, Frames = shrunk.FramesBefore };
        }
        if (OriginalOf(name) is null)
        {
            Directory.CreateDirectory(Originals);
            File.Copy(current, System.IO.Path.Combine(Originals, System.IO.Path.GetFileName(current)));
        }
        File.Delete(current);
        File.Move(draft, current);
        return shrunk;
    }

    /// <summary>Every picture in the pack that is too big, made smaller. Identical files are done once.</summary>
    public List<(string Name, Shrunk Result)> ShrinkAll(Action<string>? progress = null)
    {
        var done = new List<(string, Shrunk)>();
        var names = CharacterSlots.All.Select(s => s.Name).Append("preview").Where(TooBig).ToList();
        var made = new Dictionary<string, (string Path, Shrunk Result, int Side)>();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            progress?.Invoke($"Making smaller {i + 1} of {names.Count}: {name}…");
            var path = PictureOf(name)!;
            var hash = Hash(path);
            var side = BestSide(name);
            if (made.TryGetValue(hash, out var twin) && twin.Result.After < twin.Result.Before && twin.Side == side && File.Exists(twin.Path) && System.IO.Path.GetExtension(twin.Path) == System.IO.Path.GetExtension(path))
            {
                // The same picture as one already done: its result is copied (the first picture is still kept).
                if (OriginalOf(name) is null)
                {
                    Directory.CreateDirectory(Originals);
                    File.Copy(path, System.IO.Path.Combine(Originals, System.IO.Path.GetFileName(path)));
                }
                File.Copy(twin.Path, path, overwrite: true);
                done.Add((name, twin.Result with { Before = new FileInfo(System.IO.Path.Combine(Originals, System.IO.Path.GetFileName(path))).Length }));
                continue;
            }
            var result = Shrink(name);
            made[hash] = (path, result, side);
            done.Add((name, result));
        }
        return done;
    }

    /// <summary>Put a cropped slot's uncut picture back.</summary>
    public void Uncrop(string name)
    {
        if (OriginalOf(name) is not { } original) return;
        RemoveFiles(Folder, name);
        File.Move(original, System.IO.Path.Combine(Folder, System.IO.Path.GetFileName(original)));
        if (Directory.Exists(Originals) && Directory.GetFileSystemEntries(Originals).Length == 0) Directory.Delete(Originals);
    }

    /// <summary>".png" or ".gif" by the file's first bytes (not its name), or null.</summary>
    public static string? PictureExtension(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var head = new byte[8];
            if (file.Read(head, 0, 8) < 8) return null;
            if (head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G') return ".png";
            if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8') return ".gif";
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Pictures in the folder that no slot uses (so Nyatch never shows them).</summary>
    public IEnumerable<string> Unused() =>
        Directory.GetFiles(Folder)
            .Where(f => System.IO.Path.GetExtension(f).ToLowerInvariant() is ".png" or ".gif")
            .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
            .Where(n => n != "preview" && CharacterSlots.Find(n) is null)
            .OrderBy(n => n, StringComparer.Ordinal);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Size, DateTime Written, string Hash)> _hashes = new(StringComparer.OrdinalIgnoreCase);

    private string Hash(string path)
    {
        var info = new FileInfo(path);
        if (_hashes.TryGetValue(path, out var known) && known.Size == info.Length && known.Written == info.LastWriteTimeUtc) return known.Hash;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        _hashes[path] = (info.Length, info.LastWriteTimeUtc, hash);
        return hash;
    }

    /// <summary>Slots whose file is the same picture as an earlier slot's (by content): slot, the slot it copies.</summary>
    public Dictionary<string, string> Copies()
    {
        var first = new Dictionary<string, string>();
        var copies = new Dictionary<string, string>();
        foreach (var slot in CharacterSlots.All)
        {
            if (PictureOf(slot.Name) is not { } path) continue;
            string hash;
            try
            {
                hash = Hash(path);
            }
            catch (IOException)
            {
                continue;
            }
            if (first.TryGetValue(hash, out var original)) copies[slot.Name] = original;
            else first[hash] = slot.Name;
        }
        return copies;
    }

    /// <summary>
    /// Give every slot that borrows a picture its own file: a copy of what it shows now, named for the slot,
    /// so each can be replaced alone. Slots with nothing to show are left empty. Returns how many were made.
    /// </summary>
    public int FillAll()
    {
        // Worked out first, so a copy made early does not change what a later slot borrows.
        var copies = CharacterSlots.All
            .Where(s => !Has(s.Name) && Shown(s) is not null)
            .Select(s => (s.Name, Shown(s)!.Value.Path))
            .ToList();
        foreach (var (name, path) in copies) SetPicture(name, path);
        return copies.Count;
    }

    // ---------- Checks ----------

    /// <summary>Everything worth knowing before the pack is made: problems first.</summary>
    public List<Finding> Check()
    {
        var found = new List<Finding>();
        if (!CharacterPacks.IsPlainName(Manifest.Id)) found.Add(new(true, "Id: use only a-z, 0-9 and -."));
        if (string.IsNullOrWhiteSpace(Manifest.Name)) found.Add(new(true, "Add a name."));
        if (Manifest.Accent.Length > 0 && !CharacterPacks.IsColour(Manifest.Accent)) found.Add(new(true, "Colour: write it like #39C5BB."));
        if (PictureOf("preview") is null) found.Add(new(true, "Add a cover.", "preview"));
        if (!Has("idle")) found.Add(new(true, "Add an idle picture.", "idle"));
        if (Manifest.Font.Length > 0 && FontPath is null) found.Add(new(true, $"The font {Manifest.Font} is named but its file is missing. Choose it again."));
        var copies = Copies().Count;
        if (copies > 0) found.Add(new(false, copies == 1 ? "1 slot is a copy of another picture." : $"{copies} slots are copies of another picture."));

        long total = 0;
        foreach (var name in CharacterSlots.All.Select(s => s.Name).Append("preview"))
        {
            if (PictureOf(name) is not { } path) continue;
            long bytes;
            try
            {
                bytes = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                continue;
            }
            total += bytes;
            if (bytes > CharacterPacks.MaxPicture) found.Add(new(true, $"{name}: {bytes / 1024.0 / 1024.0:0.0} MB, limit 2 MB. Use Shrink all.", name));
            if (PictureExtension(path) is null) found.Add(new(true, $"{name}: not a PNG or GIF.", name));
            if (CharacterSlots.Find(name) is not { } slot || Picture.Load(path) is not { } picture) continue;
            if (picture.FramesInFile > Picture.MaxFrames) found.Add(new(false, $"{name}: {picture.FramesInFile} frames, only {Picture.MaxFrames} play. Use Shrink all.", name));
            // Drawn at its size on screens at 200%: smaller than that looks soft.
            var needed = Math.Max(slot.Width, slot.Height) * 2;
            if (Math.Max(picture.TrimmedWidth, picture.TrimmedHeight) < needed * 0.6)
            {
                found.Add(new(false, $"{name}: may look blurry. Use about {needed} px.", name));
            }
            if (slot.Shape == SlotShape.Tall && picture.TrimmedWidth > picture.TrimmedHeight)
            {
                found.Add(new(false, $"{name}: wide picture in a tall spot. It shows small.", name));
            }
        }
        if (total > CharacterPacks.MaxPack) found.Add(new(true, $"Theme is {total / 1024.0 / 1024.0:0.0} MB, limit 25 MB. Use Shrink all."));
        foreach (var name in Unused()) found.Add(new(false, $"{name}: not a slot name, never shown.", name));
        return found.OrderByDescending(f => f.Problem).ToList();
    }

    // ---------- Making it ----------

    /// <summary>Make the pack into a pack list's home (packs\&lt;id&gt;.nyatch, previews\, index.json): the site's characters folder, or any.</summary>
    public CharacterPack MakeInto(string home)
    {
        Save();
        return CharacterPacks.Make(Folder, home);
    }

    /// <summary>Make the .nyatch file alone, to a path.</summary>
    public CharacterPack MakeFile(string target)
    {
        var home = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nyatch-packer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var made = MakeInto(home);
            File.Copy(System.IO.Path.Combine(home, made.File.Replace('/', System.IO.Path.DirectorySeparatorChar)), target, overwrite: true);
            return made;
        }
        finally
        {
            try
            {
                Directory.Delete(home, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

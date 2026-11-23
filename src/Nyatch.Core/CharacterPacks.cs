using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nyatch.Core;

/// <summary>
/// A character pack as its pack.json describes it. In the pack list (index.json) it also says how big
/// its .nyatch file is, that file's SHA-256, and where the file and the preview picture are.
/// </summary>
public sealed record CharacterPack(
    string Id,
    string Name,
    string Version = "1.0",
    string Author = "",
    string Licence = "",
    string Description = "",
    string MinApp = "",
    int Pictures = 0,
    string[]? Covers = null,
    long Size = 0,
    string Sha256 = "",
    string File = "",
    string Preview = "",
    string Accent = "",
    // The family name of the theme's own font (font.ttf or font.otf in the theme), or empty for none.
    string Font = "")
{
    /// <summary>The pack's accent colour ("#RRGGBB"), or null if it has none (or not a usable one).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AccentColour => CharacterPacks.IsColour(Accent) ? Accent.ToUpperInvariant() : null;
}

/// <summary>The pack list: every pack that can be downloaded.</summary>
public sealed record CharacterPackIndex(int Format, CharacterPack[] Packs);

/// <summary>
/// Character packs: .nyatch files (a ZIP with pack.json, a preview picture, a licence and the pictures)
/// and where they are installed. The list of packs to download comes from a source: a web address or a
/// folder, laid out as index.json, previews/&lt;id&gt;.png and packs/&lt;id&gt;.nyatch. A pack is only fetched when
/// asked for, checked against the list's size and SHA-256, and unpacked into its own folder; only
/// pictures (PNG, GIF), pack.json and the licence are taken from it.
/// </summary>
public sealed partial class CharacterPacks
{
    public const string Extension = ".nyatch";
    public const long MaxPicture = 2 * 1024 * 1024, MaxPack = 25 * 1024 * 1024, MaxFont = 10 * 1024 * 1024;

    /// <summary>The font file a theme may carry: font.ttf or font.otf, beside pack.json.</summary>
    public static readonly string[] FontFiles = ["font.ttf", "font.otf"];

    /// <summary>A TrueType or OpenType font, by its first bytes.</summary>
    public static bool LooksLikeFont(byte[] bytes) =>
        bytes.Length > 12 && ((bytes[0] == 0 && bytes[1] == 1 && bytes[2] == 0 && bytes[3] == 0) || (bytes[0] == 'O' && bytes[1] == 'T' && bytes[2] == 'T' && bytes[3] == 'O') ||
                              (bytes[0] == 't' && bytes[1] == 'r' && bytes[2] == 'u' && bytes[3] == 'e'));

    /// <summary>An installed theme's font file, or null when it has none.</summary>
    public string? FontOf(string id) =>
        IsPlainName(id) ? FontFiles.Select(f => Path.Combine(_folder, id, f)).FirstOrDefault(File.Exists) : null;
    public const int MaxPictures = 200;

    /// <summary>The places a pack can have its own pictures for, by the first word of a picture's name.</summary>
    public static readonly string[] Places =
    [
        "time", "calendar", "weather", "hardware", "tray", "player", "lyrics", "lookup", "notice", "picker",
        "apps", "clipboard", "notifications", "chat", "focus", "notes", "terminal", "online",
    ];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex PlainName();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColour();

    /// <summary>"#RRGGBB".</summary>
    public static bool IsColour(string? text) => text is not null && HexColour().IsMatch(text);

    private readonly string _folder;
    private readonly string _source;
    private readonly HttpClient _web;

    public CharacterPacks(string? folder = null, string? source = null, HttpClient? web = null)
    {
        _folder = folder ?? DefaultFolder;
        _source = source ?? DefaultSource;
        _web = web ?? SharedWeb.Value;
    }

    private static readonly Lazy<HttpClient> SharedWeb = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Nyatch");
        return client;
    });

    /// <summary>Where packs are installed: a folder for each, under the user's local app data.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyatch", "Characters");

    /// <summary>
    /// The pack list's home: /characters on Nyatch's site (where updates come from too). NYATCH_PACKS (a
    /// folder or an address) replaces it, for trying packs out.
    /// </summary>
    public static string DefaultSource =>
        Environment.GetEnvironmentVariable("NYATCH_PACKS") is { Length: > 0 } other ? other : $"{Updates.Site}/characters/";

    public string Folder => _folder;

    public static bool IsPlainName(string name) => PlainName().IsMatch(name);

    // ---------- On this PC ----------

    /// <summary>The packs installed, by name.</summary>
    public IReadOnlyList<CharacterPack> Installed()
    {
        var found = new List<CharacterPack>();
        if (!Directory.Exists(_folder)) return found;
        foreach (var folder in Directory.GetDirectories(_folder))
        {
            var id = Path.GetFileName(folder);
            if (id == ".cache") continue;
            if (id.StartsWith('.'))
            {
                // Left over from an install that was cut short.
                TryDelete(folder);
                continue;
            }
            if (IsPlainName(id) && ReadManifest(Path.Combine(folder, "pack.json")) is { } pack && pack.Id == id) found.Add(pack);
        }
        return found.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public bool IsInstalled(string id) => IsPlainName(id) && File.Exists(Path.Combine(_folder, id, "pack.json"));

    /// <summary>An installed pack's picture for the first of the names it has (GIF, else PNG).</summary>
    public string? PictureOf(string id, params string[] names)
    {
        if (!IsPlainName(id)) return null;
        foreach (var name in names)
        {
            foreach (var ext in new[] { ".gif", ".png" })
            {
                var path = Path.Combine(_folder, id, name + ext);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    /// <summary>An installed pack, by name (null if it is not on this PC).</summary>
    public CharacterPack? InstalledPack(string id) =>
        IsPlainName(id) && ReadManifest(Path.Combine(_folder, id, "pack.json")) is { } pack && pack.Id == id ? pack : null;

    /// <summary>An installed pack's cover picture.</summary>
    public string? PreviewOf(string id) => PictureOf(id, "preview");

    /// <summary>Changes whenever a pack's files do (installed, updated, deleted), so the notches load its pictures again.</summary>
    public string Stamp(string id)
    {
        var manifest = Path.Combine(_folder, id, "pack.json");
        return IsPlainName(id) && File.Exists(manifest) ? File.GetLastWriteTimeUtc(manifest).Ticks.ToString() : "";
    }

    /// <summary>Take a pack off this PC.</summary>
    public void Delete(string id)
    {
        if (!IsPlainName(id)) return;
        var folder = Path.Combine(_folder, id);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>
    /// Install a .nyatch file. Everything is checked and unpacked beside the pack's place first, then
    /// swapped in, so a broken file changes nothing and a pack in use is never half replaced.
    /// </summary>
    public CharacterPack InstallFile(string path)
    {
        using var file = File.OpenRead(path);
        return Install(file);
    }

    /// <summary>What a .nyatch file holds, read without installing it: for showing it before it is installed.</summary>
    public sealed record Peeked(CharacterPack Pack, int Pictures, long Size, byte[]? Cover, IReadOnlyList<(string Name, byte[] Bytes)> Shown);

    /// <summary>
    /// Read a .nyatch file's pack.json, its cover and up to <paramref name="count"/> of its pictures (the
    /// <paramref name="wanted"/> ones first, then any), without installing anything.
    /// </summary>
    public static Peeked Peek(string path, IReadOnlyList<string> wanted, int count = 6)
    {
        using var zip = ZipFile.OpenRead(path);
        CharacterPack? manifest = null;
        byte[]? cover = null;
        var pictures = new List<(string Name, ZipArchiveEntry Entry)>();
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name == "pack.json") manifest = JsonSerializer.Deserialize<CharacterPack>(WithoutMark(ReadLimited(entry, MaxPicture)), Json);
            else if (name is "preview.png" or "preview.gif") cover = ReadLimited(entry, MaxPicture);
            else if (name.StartsWith("pictures/", StringComparison.Ordinal))
            {
                var leaf = name["pictures/".Length..];
                var ext = Path.GetExtension(leaf);
                if (ext is ".png" or ".gif" && IsPlainName(leaf[..^ext.Length])) pictures.Add((leaf[..^ext.Length], entry));
            }
        }
        if (manifest is null || !IsPlainName(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("It has no usable pack.json.");
        if (pictures.Count == 0) throw new InvalidDataException("It has no pictures.");
        var chosen = wanted.Select(w => pictures.FirstOrDefault(p => p.Name == w)).Where(p => p.Entry is not null)
            .Concat(pictures.Where(p => !wanted.Contains(p.Name)))
            .Take(count)
            .Select(p => (p.Name, ReadLimited(p.Entry, MaxPicture)))
            .Where(p => LooksLikePicture(p.Item2))
            .ToList();
        return new Peeked(manifest, pictures.Count, new FileInfo(path).Length, cover is not null && LooksLikePicture(cover) ? cover : null, chosen);
    }

    private CharacterPack Install(Stream package)
    {
        Directory.CreateDirectory(_folder);
        var incoming = Path.Combine(_folder, ".incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        try
        {
            CharacterPack? manifest = null;
            long total = 0;
            var pictures = 0;
            using (var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    string? target = null;
                    if (name == "pack.json") target = name;
                    else if (FontFiles.Contains(name)) target = name;
                    else if (name is "preview.png" or "preview.gif") target = name;
                    else if (name.StartsWith("pictures/", StringComparison.Ordinal))
                    {
                        var leaf = name["pictures/".Length..];
                        var ext = Path.GetExtension(leaf);
                        if (ext is ".png" or ".gif" && IsPlainName(leaf[..^ext.Length]) && leaf[..^ext.Length] != "preview")
                        {
                            target = leaf;
                            if (++pictures > MaxPictures) throw new InvalidDataException("The pack has too many pictures.");
                        }
                    }
                    if (target is null) continue;  // anything else in the file is left where it is
                    var bytes = ReadLimited(entry, FontFiles.Contains(target) ? MaxFont : MaxPicture);
                    total += bytes.Length;
                    if (total > MaxPack) throw new InvalidDataException("The pack is too big.");
                    if (target == "pack.json") manifest = JsonSerializer.Deserialize<CharacterPack>(WithoutMark(bytes), Json);
                    else if (FontFiles.Contains(target))
                    {
                        if (!LooksLikeFont(bytes)) throw new InvalidDataException($"{name} is not a font.");
                    }
                    else if (!LooksLikePicture(bytes)) throw new InvalidDataException($"{name} is not a PNG or GIF picture.");
                    File.WriteAllBytes(Path.Combine(incoming, target), bytes);
                }
            }
            if (manifest is null || !IsPlainName(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("The pack has no usable pack.json.");
            if (pictures == 0) throw new InvalidDataException("The pack has no pictures.");
            manifest = manifest with { Pictures = pictures, Size = 0, Sha256 = "", File = "", Preview = "", Accent = manifest.AccentColour ?? "" };
            // A font it names but does not carry is no font.
            if (!FontFiles.Any(f => File.Exists(Path.Combine(incoming, f)))) manifest = manifest with { Font = "" };
            File.WriteAllText(Path.Combine(incoming, "pack.json"), JsonSerializer.Serialize(manifest, Json));

            var place = Path.Combine(_folder, manifest.Id);
            if (Directory.Exists(place))
            {
                var old = Path.Combine(_folder, ".old-" + Guid.NewGuid().ToString("N"));
                Directory.Move(place, old);
                Directory.Move(incoming, place);
                TryDelete(old);
            }
            else Directory.Move(incoming, place);
            return manifest;
        }
        finally
        {
            if (Directory.Exists(incoming)) TryDelete(incoming);
        }
    }

    // ---------- To download ----------

    /// <summary>The packs that can be downloaded. The list is kept, so the last one can be shown without a connection.</summary>
    public async Task<IReadOnlyList<CharacterPack>> AvailableAsync(CancellationToken cancel = default)
    {
        var bytes = await ReadSourceAsync("index.json", 1024 * 1024, null, cancel).ConfigureAwait(false);
        var packs = Usable(JsonSerializer.Deserialize<CharacterPackIndex>(WithoutMark(bytes), Json));
        try
        {
            Directory.CreateDirectory(CacheFolder);
            await File.WriteAllBytesAsync(Path.Combine(CacheFolder, "index.json"), bytes, cancel).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Not kept this time; it is asked for again next time.
        }
        return packs;
    }

    /// <summary>The list as it was last fetched (empty if never).</summary>
    public IReadOnlyList<CharacterPack> LastAvailable()
    {
        try
        {
            var kept = Path.Combine(CacheFolder, "index.json");
            return File.Exists(kept) ? Usable(JsonSerializer.Deserialize<CharacterPackIndex>(WithoutMark(File.ReadAllBytes(kept)), Json)) : [];
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return [];
        }
    }

    /// <summary>A listed pack's cover picture, fetched once and kept. Null if it cannot be had.</summary>
    public async Task<string?> PreviewAsync(CharacterPack pack, CancellationToken cancel = default)
    {
        if (pack.Preview.Length == 0) return null;
        var ext = Path.GetExtension(pack.Preview).ToLowerInvariant();
        if (ext is not (".png" or ".gif")) return null;
        var kept = Path.Combine(CacheFolder, $"{pack.Id}-{pack.Version}{ext}");
        if (File.Exists(kept)) return kept;
        try
        {
            var bytes = await ReadSourceAsync(pack.Preview, MaxPicture, null, cancel).ConfigureAwait(false);
            if (!LooksLikePicture(bytes)) return null;
            Directory.CreateDirectory(CacheFolder);
            await File.WriteAllBytesAsync(kept, bytes, cancel).ConfigureAwait(false);
            return kept;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Fetch a listed pack, check it against the list (size and SHA-256), and install it.</summary>
    public async Task<CharacterPack> DownloadAsync(CharacterPack pack, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (pack.File.Length == 0 || pack.Sha256.Length == 0) throw new InvalidDataException("The pack list does not say where this pack is.");
        var bytes = await ReadSourceAsync(pack.File, MaxPack, pack.Size > 0 ? fraction => progress?.Report(fraction) : null, cancel, pack.Size).ConfigureAwait(false);
        if (pack.Size > 0 && bytes.Length != pack.Size) throw new InvalidDataException("The download is not the size the pack list gives.");
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(pack.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The download does not match the pack list.");
        using var package = new MemoryStream(bytes);
        var installed = Install(package);
        if (installed.Id != pack.Id)
        {
            Delete(installed.Id);
            throw new InvalidDataException("The download is a different pack.");
        }
        return installed;
    }

    private string CacheFolder => Path.Combine(_folder, ".cache");

    private static IReadOnlyList<CharacterPack> Usable(CharacterPackIndex? index) =>
        index?.Packs?.Where(p => p is not null && IsPlainName(p.Id) && !string.IsNullOrWhiteSpace(p.Name)).ToList() ?? [];

    /// <summary>A file of the source, by its path within it (never outside it), up to a size.</summary>
    private async Task<byte[]> ReadSourceAsync(string relative, long limit, Action<double>? progress, CancellationToken cancel, long expected = 0)
    {
        relative = relative.Replace('\\', '/');
        if (relative.StartsWith('/') || relative.Contains("..", StringComparison.Ordinal) || relative.Contains(':')) throw new InvalidDataException("Not a path within the pack list's home.");
        Stream stream;
        if (_source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var response = await _web.GetAsync(_source.TrimEnd('/') + "/" + relative, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        }
        else stream = File.OpenRead(Path.Combine(_source, relative.Replace('/', Path.DirectorySeparatorChar)));
        await using (stream)
        {
            using var all = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                all.Write(buffer, 0, read);
                if (all.Length > limit) throw new InvalidDataException("The file is bigger than a pack may be.");
                if (expected > 0) progress?.Invoke(Math.Min(1.0, all.Length / (double)expected));
            }
            return all.ToArray();
        }
    }

    // ---------- Making packs ----------

    /// <summary>
    /// Make a pack's .nyatch file from a folder (pack.json, preview.png or .gif, its font if it has one, and the
    /// pictures), and list it: writes packs/&lt;id&gt;.nyatch and previews/&lt;id&gt;.png under
    /// <paramref name="home"/>, and adds or replaces the pack in its index.json.
    /// </summary>
    public static CharacterPack Make(string packFolder, string home)
    {
        var manifest = ReadManifest(Path.Combine(packFolder, "pack.json")) ?? throw new InvalidDataException("The folder has no usable pack.json.");
        if (!IsPlainName(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("pack.json needs a plain id (a-z, 0-9, -) and a name.");
        var preview = new[] { "preview.png", "preview.gif" }.Select(n => Path.Combine(packFolder, n)).FirstOrDefault(File.Exists)
            ?? throw new InvalidDataException("The folder needs a preview.png (or preview.gif): the cover shown before downloading.");
        var pictures = Directory.GetFiles(packFolder)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".gif")
            .Where(f => IsPlainName(Path.GetFileNameWithoutExtension(f)) && Path.GetFileNameWithoutExtension(f) != "preview")
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (pictures.Count == 0) throw new InvalidDataException("The folder has no pictures.");
        if (pictures.Count > MaxPictures) throw new InvalidDataException("Too many pictures.");
        foreach (var picture in pictures.Append(preview))
        {
            if (new FileInfo(picture).Length > MaxPicture) throw new InvalidDataException($"{Path.GetFileName(picture)} is over {MaxPicture / 1024 / 1024} MB.");
        }
        var names = pictures.Select(Path.GetFileNameWithoutExtension).ToList();
        manifest = manifest with
        {
            Pictures = pictures.Count,
            Covers = Places.Where(place => names.Any(n => n == place || n!.StartsWith(place + "-", StringComparison.Ordinal))).ToArray(),
            Size = 0, Sha256 = "", File = "", Preview = "",
            Accent = manifest.AccentColour ?? "",
        };

        Directory.CreateDirectory(Path.Combine(home, "packs"));
        Directory.CreateDirectory(Path.Combine(home, "previews"));
        var package = Path.Combine(home, "packs", manifest.Id + Extension);
        if (File.Exists(package)) File.Delete(package);
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("pack.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open())) writer.Write(JsonSerializer.Serialize(manifest, Json));
            zip.CreateEntryFromFile(preview, Path.GetFileName(preview).ToLowerInvariant(), CompressionLevel.NoCompression);
            // The theme's own font, if it has one (and names it).
            if (manifest.Font.Length > 0 && FontFiles.Select(f => Path.Combine(packFolder, f)).FirstOrDefault(File.Exists) is { } font)
            {
                if (new FileInfo(font).Length > MaxFont) throw new InvalidDataException($"{Path.GetFileName(font)} is over {MaxFont / 1024 / 1024} MB.");
                zip.CreateEntryFromFile(font, Path.GetFileName(font).ToLowerInvariant(), CompressionLevel.Optimal);
            }
            // Pictures are compressed already.
            foreach (var picture in pictures) zip.CreateEntryFromFile(picture, "pictures/" + Path.GetFileName(picture).ToLowerInvariant(), CompressionLevel.NoCompression);
        }
        if (new FileInfo(package).Length > MaxPack) throw new InvalidDataException($"The pack is over {MaxPack / 1024 / 1024} MB.");
        var previewName = $"previews/{manifest.Id}{Path.GetExtension(preview).ToLowerInvariant()}";
        File.Copy(preview, Path.Combine(home, previewName.Replace('/', Path.DirectorySeparatorChar)), overwrite: true);

        var listed = manifest with
        {
            Size = new FileInfo(package).Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))).ToLowerInvariant(),
            File = $"packs/{manifest.Id}{Extension}",
            Preview = previewName,
        };
        var indexFile = Path.Combine(home, "index.json");
        var packs = File.Exists(indexFile) ? (JsonSerializer.Deserialize<CharacterPackIndex>(WithoutMark(File.ReadAllBytes(indexFile)), Json)?.Packs ?? []).ToList() : [];
        packs.RemoveAll(p => p.Id == listed.Id);
        packs.Add(listed);
        File.WriteAllText(indexFile, JsonSerializer.Serialize(new CharacterPackIndex(1, packs.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray()), Json));
        return listed;
    }

    // ---------- Small pieces ----------

    private static CharacterPack? ReadManifest(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<CharacterPack>(WithoutMark(File.ReadAllBytes(path)), Json) : null;
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>An entry's bytes, refusing one that unpacks to more than the limit (whatever its header says).</summary>
    private static byte[] ReadLimited(ZipArchiveEntry entry, long limit)
    {
        using var from = entry.Open();
        using var all = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            all.Write(buffer, 0, read);
            if (all.Length > limit) throw new InvalidDataException($"{entry.FullName} is over {limit / 1024 / 1024} MB.");
        }
        return all.ToArray();
    }

    /// <summary>JSON without the byte-order mark some Windows editors (and PowerShell 5) put at the start.</summary>
    private static ReadOnlySpan<byte> WithoutMark(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsSpan(3) : bytes;

    /// <summary>PNG or GIF, by its first bytes.</summary>
    private static bool LooksLikePicture(byte[] bytes) =>
        bytes.Length > 8 && ((bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G') || (bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8'));

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Still in use; it is tried again the next time packs are listed.
        }
    }
}

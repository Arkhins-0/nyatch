using Xunit;
using System.IO.Compression;
using Nyatch.Core;

namespace Nyatch.Core.Tests;

public sealed class CharacterPacksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nyatch-packs-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Gif = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0, 0, 0];

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string PackFolder(string id = "neko")
    {
        var folder = Path.Combine(_root, "source", id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pack.json"), $$"""{ "id": "{{id}}", "name": "Neko", "version": "1.2", "author": "Someone" }""");
        File.WriteAllBytes(Path.Combine(folder, "preview.png"), Png);
        File.WriteAllBytes(Path.Combine(folder, "idle.gif"), Gif);
        File.WriteAllBytes(Path.Combine(folder, "weather-rain.png"), Png);
        File.WriteAllBytes(Path.Combine(folder, "time-morning.png"), Png);
        File.WriteAllText(Path.Combine(folder, "LICENSE.txt"), "CC0");
        File.WriteAllText(Path.Combine(folder, "README.md"), "not part of the pack");
        return folder;
    }

    private string Home => Path.Combine(_root, "home");
    private CharacterPacks Packs => new(Path.Combine(_root, "installed"), Home);

    [Fact]
    public void ListingThePacksLeavesAnInstallGoingOnAlone()
    {
        // A pack being unpacked (its folder still written to) keeps what it has; one left over long ago goes.
        var installed = Path.Combine(_root, "installed");
        var going = Directory.CreateDirectory(Path.Combine(installed, ".incoming-now")).FullName;
        File.WriteAllBytes(Path.Combine(going, "about.png"), Png);
        var left = Directory.CreateDirectory(Path.Combine(installed, ".incoming-old")).FullName;
        Directory.SetLastWriteTimeUtc(left, DateTime.UtcNow.AddHours(-1));

        Assert.Empty(Packs.Installed());
        Assert.True(File.Exists(Path.Combine(going, "about.png")));
        Assert.False(Directory.Exists(left));
    }

    [Fact]
    public async Task APackIsMadeListedDownloadedAndDeleted()
    {
        var listed = CharacterPacks.Make(PackFolder(), Home);
        Assert.Equal(3, listed.Pictures);
        Assert.Equal(["time", "weather"], listed.Covers);
        Assert.True(File.Exists(Path.Combine(Home, "neko", "neko.nyatch")));
        Assert.True(File.Exists(Path.Combine(Home, "neko", "preview.png")));

        var packs = Packs;
        var available = await packs.AvailableAsync();
        var pack = Assert.Single(available);
        Assert.Equal("1.2", pack.Version);
        Assert.NotNull(await packs.PreviewAsync(pack));
        Assert.Empty(packs.Installed());

        await packs.DownloadAsync(pack);
        var installed = Assert.Single(packs.Installed());
        Assert.Equal("Neko", installed.Name);
        Assert.EndsWith("idle.gif", packs.PictureOf("neko", "time-day", "idle"));
        Assert.NotNull(packs.PreviewOf("neko"));
        Assert.False(File.Exists(Path.Combine(packs.Folder, "neko", "README.md")));
        Assert.NotEqual("", packs.Stamp("neko"));
        // The list is kept for when there is no connection.
        Assert.Single(packs.LastAvailable());

        packs.Delete("neko");
        Assert.Empty(packs.Installed());
        Assert.Equal("", packs.Stamp("neko"));
    }

    [Fact]
    public void APacksColourComesWithItAndABadOneIsDropped()
    {
        var folder = PackFolder();
        // Saved with a byte-order mark, as PowerShell 5 and some editors do.
        File.WriteAllText(Path.Combine(folder, "pack.json"), """{ "id": "neko", "name": "Neko", "accent": "#39c5bb" }""", new System.Text.UTF8Encoding(true));
        var packs = Packs;
        packs.InstallFile(Path.Combine(Home, CharacterPacks.Make(folder, Home).File));
        Assert.Equal("#39C5BB", packs.InstalledPack("neko")!.AccentColour);

        File.WriteAllText(Path.Combine(folder, "pack.json"), """{ "id": "neko", "name": "Neko", "version": "2.0", "accent": "red; x" }""");
        packs.InstallFile(Path.Combine(Home, CharacterPacks.Make(folder, Home).File));
        Assert.Null(packs.InstalledPack("neko")!.AccentColour);
        Assert.Null(packs.InstalledPack("nobody"));
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchTheListIsRefused()
    {
        var listed = CharacterPacks.Make(PackFolder(), Home);
        var packs = Packs;
        await Assert.ThrowsAsync<InvalidDataException>(() => packs.DownloadAsync(listed with { Sha256 = new string('0', 64) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => packs.DownloadAsync(listed with { File = "../outside.nyatch" }));
        Assert.Empty(packs.Installed());
    }

    [Fact]
    public void OnlyPicturesAreTakenFromAFileAndNothingLeavesThePacksFolder()
    {
        var file = Path.Combine(_root, "odd.nyatch");
        Directory.CreateDirectory(_root);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            void Add(string name, byte[] bytes)
            {
                using var to = zip.CreateEntry(name).Open();
                to.Write(bytes);
            }
            Add("pack.json", """{ "id": "odd", "name": "Odd" }"""u8.ToArray());
            Add("pictures/idle.png", Png);
            Add("pictures/../../escape.png", Png);
            Add("pictures/run.exe", [1, 2, 3]);
            Add("../evil.png", Png);
            Add("notes.txt", [1]);
        }
        var packs = Packs;
        var installed = packs.InstallFile(file);
        Assert.Equal(1, installed.Pictures);
        Assert.Equal(["idle.png", "pack.json"], Directory.GetFiles(Path.Combine(packs.Folder, "odd")).Select(Path.GetFileName).Order());
        Assert.False(File.Exists(Path.Combine(_root, "escape.png")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.png")));
    }

    [Fact]
    public void ABrokenFileChangesNothing()
    {
        var packs = Packs;
        packs.InstallFile(Path.Combine(CharacterPacks.Make(PackFolder(), Home) is { } made ? Home : "", "neko", "neko.nyatch"));
        var before = packs.Stamp("neko");

        // A picture that is not a picture, under the same id.
        var bad = Path.Combine(_root, "bad.nyatch");
        using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create))
        {
            using (var to = zip.CreateEntry("pack.json").Open()) to.Write("""{ "id": "neko", "name": "Neko" }"""u8);
            using (var to = zip.CreateEntry("pictures/idle.png").Open()) to.Write("MZ not a picture"u8);
        }
        Assert.Throws<InvalidDataException>(() => packs.InstallFile(bad));
        Assert.Equal(before, packs.Stamp("neko"));
        Assert.Single(packs.Installed());
        // No part-made folders are left behind.
        Assert.DoesNotContain(Directory.GetDirectories(packs.Folder), d => Path.GetFileName(d).StartsWith(".incoming", StringComparison.Ordinal));
    }
}

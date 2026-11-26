using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

public class UpdatesTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.3", "1.3.0")]
    [InlineData("Nyatch 2.0.1-beta", "2.0.1")]
    [InlineData("latest", null)]
    [InlineData(null, null)]
    public void ReadsVersions(string? text, string? version)
    {
        Assert.Equal(version, Updates.ParseVersion(text)?.ToString());
    }

    [Fact]
    public void ReadsTheSitesLatestRelease()
    {
        // As site/build.mjs writes /releases/latest.json.
        var json = """
            {
              "tag_name": "v1.2.0", "name": "Nyatch 1.2.0", "body": "- Packs update themselves",
              "html_url": "https://nyatch.arkhins.com/releases", "draft": false, "prerelease": false,
              "assets": [
                { "name": "Nyatch-1.2.0.msi", "size": 3000, "browser_download_url": "https://nyatch.arkhins.com/releases/v1.2.0/Nyatch-1.2.0.msi" },
                { "name": "Nyatch-1.2.0.msi.sha256", "size": 22, "browser_download_url": "https://nyatch.arkhins.com/releases/v1.2.0/Nyatch-1.2.0.msi.sha256" },
                { "name": "Nyatch-1.2.0.msi.sig", "size": 9, "browser_download_url": "https://nyatch.arkhins.com/releases/v1.2.0/Nyatch-1.2.0.msi.sig" }
              ]
            }
            """;
        var release = Updates.Parse(json)!;
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("https://nyatch.arkhins.com/releases/v1.2.0/Nyatch-1.2.0.msi", release.Installer.ToString());
        Assert.NotNull(release.Checksum);
        Assert.NotNull(release.Signature);
        Assert.Equal("https://nyatch.arkhins.com/releases/latest.json", Updates.LatestUrl().ToString());
    }

    [Fact]
    public void ReadsARelease()
    {
        const string json = """
            {"tag_name":"v1.2.0","name":"Nyatch 1.2","body":"Faster notch.","html_url":"https://github.com/o/r/releases/tag/v1.2.0",
             "draft":false,"prerelease":false,
             "assets":[
               {"name":"Nyatch-1.2.0.msi","browser_download_url":"https://github.com/o/r/releases/download/v1.2.0/Nyatch-1.2.0.msi"},
               {"name":"Nyatch-1.2.0.msi.sha256","browser_download_url":"https://github.com/o/r/releases/download/v1.2.0/Nyatch-1.2.0.msi.sha256"},
               {"name":"Nyatch-1.2.0.msi.sig","browser_download_url":"https://github.com/o/r/releases/download/v1.2.0/Nyatch-1.2.0.msi.sig"},
               {"name":"notes.txt","browser_download_url":"http://insecure.example/notes.txt"}
             ]}
            """;
        var release = Updates.Parse(json)!;
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("Nyatch-1.2.0.msi", release.InstallerName);
        Assert.EndsWith(".msi.sha256", release.Checksum!.AbsolutePath);
        Assert.EndsWith(".msi.sig", release.Signature!.AbsolutePath);
        Assert.Equal("Faster notch.", release.Notes);
        Assert.Equal("https://github.com/o/r/releases/tag/v1.2.0", release.Page.ToString());
    }

    [Fact]
    public void PrefersTheMsi()
    {
        const string json = """
            {"tag_name":"v1.3.0","assets":[
               {"name":"Nyatch-1.3.0.msi","browser_download_url":"https://x/Nyatch-1.3.0.msi"},
               {"name":"Nyatch-Setup-1.3.0.exe","browser_download_url":"https://x/Nyatch-Setup-1.3.0.exe"},
               {"name":"Nyatch-Setup-1.3.0.exe.sha256","browser_download_url":"https://x/Nyatch-Setup-1.3.0.exe.sha256"},
               {"name":"Nyatch-Setup-1.3.0.exe.sig","browser_download_url":"https://x/Nyatch-Setup-1.3.0.exe.sig"}
             ]}
            """;
        var release = Updates.Parse(json)!;
        Assert.Equal("Nyatch-1.3.0.msi", release.InstallerName);
    }

    [Fact]
    public void SkipsReleasesItCannotUse()
    {
        Assert.Null(Updates.Parse("""{"tag_name":"v1.0.0","assets":[]}"""));
        Assert.Null(Updates.Parse("""{"tag_name":"nightly","assets":[{"name":"a.msi","browser_download_url":"https://x/a.msi"}]}"""));
        Assert.Null(Updates.Parse("""{"tag_name":"v2.0.0","draft":true,"assets":[{"name":"a.msi","browser_download_url":"https://x/a.msi"}]}"""));
        Assert.Null(Updates.Parse("""{"tag_name":"v2.0.0","assets":[{"name":"a.msi","browser_download_url":"http://x/a.msi"}]}"""));
        Assert.Null(Updates.Parse("not json"));
        // No checksum: still a release, but one that will not be installed automatically.
        Assert.Null(Updates.Parse("""{"tag_name":"v2.0.0","assets":[{"name":"a.msi","browser_download_url":"https://x/a.msi"}]}""")!.Checksum);
    }

    [Fact]
    public void ReadsChecksumFiles()
    {
        Assert.Equal(Hash, Updates.ChecksumFor(Hash.ToUpperInvariant() + "\n", "Nyatch-1.2.0.msi"));
        Assert.Equal(Hash, Updates.ChecksumFor($"{new string('f', 64)}  other.msi\n{Hash} *Nyatch-1.2.0.msi\n", "Nyatch-1.2.0.msi"));
        Assert.Null(Updates.ChecksumFor($"{Hash}  other.msi", "Nyatch-1.2.0.msi"));
        Assert.Null(Updates.ChecksumFor("not a hash", "x.msi"));
    }

    [Fact]
    public void KeepsDownloadNamesSafe()
    {
        Assert.Equal("Nyatch-1.2.0.msi", Updates.SafeFileName("Nyatch-1.2.0.msi"));
        Assert.Equal("Nyatch-update.msi", Updates.SafeFileName(@"..\evil.msi"));
        Assert.Equal("Nyatch-Setup-1.3.0.exe", Updates.SafeFileName("Nyatch-Setup-1.3.0.exe"));
        Assert.Equal("Nyatch-update.exe", Updates.SafeFileName(@"..\evil.exe"));
    }
}

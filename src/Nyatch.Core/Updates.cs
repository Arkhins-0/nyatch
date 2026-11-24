using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nyatch.Core;

/// <summary>A published version of Nyatch: its number, what changed, and where its installer, checksum and signature are.</summary>
public sealed record Release(Version Version, string Tag, string Name, string Notes, Uri Installer, string InstallerName, Uri? Checksum, Uri Page, Uri? Signature = null);

/// <summary>
/// Reads Nyatch's releases, as nyatch.arkhins.com lists them (in the shape GitHub's API gives; the site keeps
/// its releases in Neon and their files in Cloudflare R2). A release is tagged with its version ("v1.2.0") and carries the
/// installer ("Nyatch-1.2.0.msi"), its SHA-256 ("Nyatch-1.2.0.msi.sha256") and its signature
/// ("Nyatch-1.2.0.msi.sig", see <see cref="ReleaseSignature"/>). Nothing here touches the
/// network: the caller fetches, this reads.
/// </summary>
public static partial class Updates
{
    /// <summary>Nyatch's site: where its updates and character packs are downloaded from. Change it here to move them.</summary>
    public const string Site = "https://nyatch.arkhins.com";

    /// <summary>The newest release.</summary>
    public static Uri LatestUrl() => new($"{Site}/releases/latest.json");

    /// <summary>"v1.2.0", "1.2", "Nyatch 1.2.0-beta" → 1.2.0; null if there is no version in it.</summary>
    public static Version? ParseVersion(string? text)
    {
        var match = VersionText().Match(text ?? "");
        if (!match.Success) return null;
        var parts = match.Value.Split('.').Select(int.Parse).ToList();
        while (parts.Count < 3) parts.Add(0);
        return new Version(parts[0], parts[1], parts[2]);
    }

    /// <summary>The release GitHub describes, if it is a usable one: a version in its tag and an installer attached.</summary>
    public static Release? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
            var tag = Text(root, "tag_name");
            if (ParseVersion(tag) is not { } version) return null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            var files = assets.EnumerateArray()
                .Select(a => (Name: Text(a, "name"), Url: Text(a, "browser_download_url")))
                .Where(a => a.Name.Length > 0 && Uri.TryCreate(a.Url, UriKind.Absolute, out var u) && u.Scheme == "https")
                .ToList();
            // The setup program (an .exe); a release from before it came as an .msi.
            var installer = files.FirstOrDefault(f => f.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (installer.Name is null) installer = files.FirstOrDefault(f => f.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            if (installer.Name is null) return null;
            var checksum = files.FirstOrDefault(f => f.Name.Equals(installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
            var signature = files.FirstOrDefault(f => f.Name.Equals(installer.Name + ".sig", StringComparison.OrdinalIgnoreCase));
            var page = Uri.TryCreate(Text(root, "html_url"), UriKind.Absolute, out var p) ? p : new Uri(installer.Url);
            return new Release(version, tag, Text(root, "name"), Text(root, "body"), new Uri(installer.Url), installer.Name,
                checksum.Url is null ? null : new Uri(checksum.Url), page, signature.Url is null ? null : new Uri(signature.Url));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The SHA-256 a checksum file gives for a file: "hash", or "hash  name" lines as sha256sum writes them.</summary>
    public static string? ChecksumFor(string text, string fileName)
    {
        string? only = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var match = ChecksumLine().Match(line);
            if (!match.Success) continue;
            var hash = match.Groups[1].Value.ToLowerInvariant();
            var name = match.Groups[2].Value.Trim().TrimStart('*');
            if (name.Length == 0) only ??= hash;
            else if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase)) return hash;
        }
        return only;
    }

    /// <summary>A safe file name for a download: the release's own name if it is a plain one.</summary>
    public static string SafeFileName(string name) =>
        SafeName().IsMatch(name) ? name : name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? "Nyatch-update.msi" : "Nyatch-update.exe";

    private static bool Bool(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    [GeneratedRegex(@"\d+(\.\d+){1,2}")] private static partial Regex VersionText();
    [GeneratedRegex(@"^([0-9a-fA-F]{64})(?:\s+(.*))?$")] private static partial Regex ChecksumLine();
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}\.(exe|msi)$", RegexOptions.IgnoreCase)] private static partial Regex SafeName();
}

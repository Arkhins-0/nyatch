using System.Text;
using System.Text.Json;

namespace Nyatch.Core;

public enum ClipKind { Text, Image, Files }

/// <summary>
/// Something that was copied. Key tells copies apart: the text itself, the list of files, or a hash of the picture's pixels.
/// Image is the picture's file: a copy in the history's own folder, or, for a screenshot Windows
/// saved itself, that screenshot (which the history never deletes).
/// </summary>
public sealed record ClipItem(string Id, ClipKind Kind, string Key, string Text, string? Image, string[] Files, bool Pinned, DateTime At);

/// <summary>
/// What has been copied lately, as Windows' own clipboard history keeps it: the newest first,
/// the oldest dropped once there are too many, and pinned items kept until unpinned, also across restarts.
/// Items that are not pinned are forgotten when the app closes, as Windows does.
/// </summary>
public sealed class ClipHistory
{
    /// <summary>Items kept that are not pinned, unless set otherwise.</summary>
    public const int Kept = 40;

    private int _keep = Kept;

    /// <summary>How many items that are not pinned are kept; the oldest go when it is lowered.</summary>
    public int Keep
    {
        get => _keep;
        set
        {
            _keep = Math.Max(1, value);
            Trim();
        }
    }

    /// <summary>Longer text is cut to this many characters.</summary>
    public const int MaxText = 200_000;

    private readonly string _pinnedFile;
    private readonly List<ClipItem> _items = [];

    public ClipHistory(string folder)
    {
        Folder = Path.Combine(folder, "clipboard");
        _pinnedFile = Path.Combine(Folder, "pinned.json");
        try
        {
            if (File.Exists(_pinnedFile) && JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(_pinnedFile)) is { } pinned)
            {
                _items.AddRange(pinned.Where(i => i is { Id: not null, Key: not null, Text: not null, Files: not null } && (i.Kind != ClipKind.Image || File.Exists(i.Image))).Select(i => i with { Pinned = true }));
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        // Pictures of items that were not pinned are left from the last run; they are not kept.
        try
        {
            if (Directory.Exists(Folder))
            {
                var keep = new HashSet<string>(_items.Where(i => i.Image is not null).Select(i => i.Image!), StringComparer.OrdinalIgnoreCase);
                foreach (var picture in Directory.EnumerateFiles(Folder, "*.png"))
                {
                    if (!keep.Contains(picture)) File.Delete(picture);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Where pictures are saved.</summary>
    public string Folder { get; }

    /// <summary>Pinned items first, then the rest; newest first within each.</summary>
    public IReadOnlyList<ClipItem> Items => _items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.At).ToList();

    /// <summary>A new file name for a picture about to be added.</summary>
    public string NewImagePath() => Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".png");

    public ClipItem? AddText(string text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > MaxText) text = text[..MaxText];
        return Add(ClipKind.Text, "T:" + text, text, null, [], now);
    }

    public ClipItem? AddFiles(IReadOnlyList<string> files, DateTime now)
    {
        var paths = files.Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
        if (paths.Length == 0) return null;
        return Add(ClipKind.Files, "F:" + string.Join("\n", paths).ToLowerInvariant(), "", null, paths, now);
    }

    /// <summary>Add a picture already saved at <paramref name="image"/>. A copy of a picture already held only moves that one to the top (and the new file is deleted).</summary>
    public ClipItem? AddImage(string image, string pixelHash, DateTime now) =>
        Add(ClipKind.Image, "I:" + pixelHash, "", image, [], now);

    private ClipItem Add(ClipKind kind, string key, string text, string? image, string[] files, DateTime now)
    {
        var at = _items.FindIndex(i => i.Key == key);
        if (at >= 0)
        {
            if (image is not null && !string.Equals(image, _items[at].Image, StringComparison.OrdinalIgnoreCase)) DeleteOwn(image);
            _items[at] = _items[at] with { At = now };
            return _items[at];
        }
        var item = new ClipItem(Guid.NewGuid().ToString("N"), kind, key, text, image, files, false, now);
        _items.Add(item);
        Trim();
        return item;
    }

    /// <summary>Bring an item to the top, as when it is copied again from the history.</summary>
    public void Touch(string id, DateTime now)
    {
        var at = _items.FindIndex(i => i.Id == id);
        if (at >= 0) _items[at] = _items[at] with { At = now };
    }

    public void Pin(string id, bool pinned)
    {
        var at = _items.FindIndex(i => i.Id == id);
        if (at < 0 || _items[at].Pinned == pinned) return;
        _items[at] = _items[at] with { Pinned = pinned };
        if (!pinned) Trim();
        Save();
    }

    public void Remove(string id)
    {
        var at = _items.FindIndex(i => i.Id == id);
        if (at < 0) return;
        var item = _items[at];
        _items.RemoveAt(at);
        if (item.Image is not null) DeleteOwn(item.Image);
        if (item.Pinned) Save();
    }

    /// <summary>Forget everything that is not pinned.</summary>
    public void ClearUnpinned()
    {
        foreach (var item in _items.Where(i => !i.Pinned).ToList()) Remove(item.Id);
    }

    private void Trim()
    {
        var loose = _items.Where(i => !i.Pinned).OrderByDescending(i => i.At).Skip(_keep).ToList();
        foreach (var item in loose) Remove(item.Id);
    }

    /// <summary>True for a picture this history saved itself (in its own folder), not one it only points to.</summary>
    public bool Owns(string file)
    {
        var folder = Path.GetFullPath(Folder).TrimEnd('\\') + "\\";
        return Path.GetFullPath(file).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Delete a picture only if it is the history's own copy: a screenshot it points to is yours.</summary>
    private void DeleteOwn(string file)
    {
        if (Owns(file)) Delete(file);
    }

    private static void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Write the pinned items to disk; the rest live only while the app runs.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var pinned = _items.Where(i => i.Pinned).ToList();
            if (pinned.Count == 0) File.Delete(_pinnedFile);
            else File.WriteAllText(_pinnedFile, JsonSerializer.Serialize(pinned), new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

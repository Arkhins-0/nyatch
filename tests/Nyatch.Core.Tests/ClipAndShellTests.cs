using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

public class ClipAndShellTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 9, 0, 0);

    [Fact]
    public void KeepsWhatWasCopiedAndPinnedOnesAcrossRuns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-clip-" + Guid.NewGuid().ToString("N"));
        try
        {
            var history = new ClipHistory(folder);
            Assert.Null(history.AddText("   ", T0));
            var hello = history.AddText("hello", T0)!;
            history.AddFiles([@"C:\a.txt", @"C:\b.txt"], T0.AddSeconds(1));
            Directory.CreateDirectory(history.Folder);
            var picture = history.NewImagePath();
            File.WriteAllBytes(picture, [1, 2, 3]);
            var shot = history.AddImage(picture, "abc", T0.AddSeconds(2))!;
            Assert.Equal([ClipKind.Image, ClipKind.Files, ClipKind.Text], history.Items.Select(i => i.Kind));

            // Copying the same thing again moves it up instead of adding it twice; a second copy of a picture is not kept.
            Assert.Equal(hello.Id, history.AddText("hello", T0.AddSeconds(3))!.Id);
            var again = history.NewImagePath();
            File.WriteAllBytes(again, [1, 2, 3]);
            Assert.Equal(shot.Id, history.AddImage(again, "abc", T0.AddSeconds(4))!.Id);
            Assert.False(File.Exists(again));
            Assert.Equal(3, history.Items.Count);

            // Pinned items come first and survive "clear" and a restart; the rest do not.
            history.Pin(hello.Id, true);
            history.Pin(shot.Id, true);
            Assert.Equal([shot.Id, hello.Id, history.Items[2].Id], history.Items.Select(i => i.Id));
            Assert.Equal(ClipKind.Files, history.Items[2].Kind);
            var reopened = new ClipHistory(folder);
            Assert.Equal(["", "hello"], reopened.Items.Select(i => i.Text));
            Assert.True(File.Exists(picture));

            reopened.AddText("loose", T0.AddMinutes(1));
            reopened.ClearUnpinned();
            Assert.Equal(2, reopened.Items.Count);
            reopened.Remove(reopened.Items.Single(i => i.Kind == ClipKind.Image).Id);
            Assert.False(File.Exists(picture));

            // Only the newest unpinned items are kept.
            for (var i = 0; i < ClipHistory.Kept + 5; i++) reopened.AddText("item " + i, T0.AddHours(1).AddSeconds(i));
            Assert.Equal(ClipHistory.Kept + 1, reopened.Items.Count);
            Assert.DoesNotContain(reopened.Items, i => i.Text == "item 0");
            Assert.Contains(reopened.Items, i => i.Text == "hello");

            Assert.Equal(ClipHistory.MaxText, reopened.AddText(new string('x', ClipHistory.MaxText + 9), T0.AddDays(1))!.Text.Length);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NeverDeletesAScreenshotItOnlyPointsTo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-clip-" + Guid.NewGuid().ToString("N"));
        var screenshots = Path.Combine(Path.GetTempPath(), "nyatch-shots-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(screenshots);
            var yours = Path.Combine(screenshots, "Screenshot.png");
            File.WriteAllBytes(yours, [1, 2, 3]);
            var history = new ClipHistory(folder);
            Directory.CreateDirectory(history.Folder);
            var copy = history.NewImagePath();
            File.WriteAllBytes(copy, [4, 5, 6]);

            var pointed = history.AddImage(yours, "a", DateTime.Now)!;
            history.AddImage(copy, "b", DateTime.Now);
            Assert.False(history.Owns(yours));
            Assert.True(history.Owns(copy));

            history.Remove(pointed.Id);
            history.ClearUnpinned();
            Assert.True(File.Exists(yours));   // your screenshot stays
            Assert.False(File.Exists(copy));   // the history's own copy goes
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            if (Directory.Exists(screenshots)) Directory.Delete(screenshots, recursive: true);
        }
    }

    [Fact]
    public void ReadsShellOutputAroundTheMarkers()
    {
        var reader = new ShellReader();
        var (text, finished) = reader.Feed("Volume in drive C\r\n\x1B[32mgreen\x1B[0m\r\n__NYATCH_");
        Assert.Equal("Volume in drive C\ngreen\n", text);
        Assert.False(finished);
        (text, finished) = reader.Feed("CWD__ \r\nC:\\Users\\me\r\n");
        Assert.Equal("", text);
        Assert.True(finished);
        Assert.Equal(@"C:\Users\me", reader.Folder);

        // cmd's prompt in front of the marker, and the empty line cmd adds after the folder, are taken out.
        var cmd = new ShellReader();
        (text, finished) = cmd.Feed("\x1B[0m\r\n\x1B[0m__NYATCH_CWD__ \r\nC:\\x\r\n\r\n\x1B[0mhosts\r\n\r\nnetworks\r\n");
        Assert.Equal("\nhosts\n\nnetworks\n", text);
        Assert.True(finished);
        Assert.Equal(@"C:\x", cmd.Folder);

        // A question with no line end yet is shown straight away.
        (text, finished) = reader.Feed("Overwrite? (Y/N) ");
        Assert.Equal("Overwrite? (Y/N) ", text);
        Assert.False(finished);
    }

    [Fact]
    public void WritesCommandsForEachShell()
    {
        Assert.Equal("dir & echo __NYATCH_CWD__ & cd", Shell.Command(ShellKind.Cmd, "dir"));
        Assert.StartsWith("ls; Write-Output \"__NYATCH_CWD__\"", Shell.Command(ShellKind.PowerShell, "ls"));
        Assert.Equal(@"~\Projects\x", Shell.ShortFolder(@"C:\Users\Me\Projects\x", @"C:\Users\me"));
        Assert.Equal(@"C:\Users\Mel", Shell.ShortFolder(@"C:\Users\Mel", @"C:\Users\Me"));
        Assert.Equal("~", Shell.ShortFolder(@"C:\Users\me", @"C:\Users\me"));

        Assert.Equal(@"C:\Users\Arkhins", Shell.Squeeze(@"C:\Users\Arkhins", 34));
        var squeezed = Shell.Squeeze(@"C:\Users\Arkhins\Documents\Projects\nyatch\src\Nyatch.App", 34);
        Assert.Equal(34, squeezed.Length);
        Assert.StartsWith(@"C:\Users\Ark", squeezed);
        Assert.EndsWith(@"src\Nyatch.App", squeezed);
        Assert.Contains("\u2026", squeezed);

        Assert.Equal("python -i -u 2>&1", Shell.ForPipes(ShellKind.Cmd, " python "));
        Assert.Equal("py -i -u 2>&1", Shell.ForPipes(ShellKind.Cmd, "py"));
        Assert.Equal("cmd /c \"node -i 2>&1\"", Shell.ForPipes(ShellKind.PowerShell, "node"));
        Assert.Equal("python app.py", Shell.ForPipes(ShellKind.Cmd, "python app.py"));
    }
}

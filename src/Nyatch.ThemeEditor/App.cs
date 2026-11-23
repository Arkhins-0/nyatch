using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.ThemeEditor;

/// <summary>
/// Nyatch's theme editor, in a window of its own: opened from the Theme page, or by Nyatch.exe --theme-editor.
/// For checking it, Nyatch.exe passes these on:
///   --theme-editor-shot &lt;theme folder&gt; &lt;png&gt; [slot]   a picture of the window, drawn off screen
///   --theme-editor-test &lt;log&gt;                         check making and installing a theme; results in the log
///   --theme-editor-shrink | -cover | -fill &lt;theme folder&gt; ...
/// </summary>
public static class ThemeEditor
{
    private static MainWindow? _open;

    /// <summary>Open the editor (or bring it forward if it is open), on a theme's folder, or the last one.</summary>
    public static Window Open(string? folder = null)
    {
        if (_open is { IsLoaded: true } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            if (folder is not null) open.OpenFolder(folder);
            return open;
        }
        var window = new MainWindow();
        _open = window;
        window.Closed += (_, _) => _open = null;
        folder ??= MainWindow.LastFolder();
        if (folder is not null) window.OpenFolder(folder);
        window.Show();
        return window;
    }

    /// <summary>A checking command (the arguments after "--theme-editor-…"); the process's exit code.</summary>
    public static int Command(string verb, string[] args)
    {
        args = [$"--{verb}", .. args];
        if (args.Length >= 3 && args[0] == "--shot") return Shot(args[1], args[2], args.Length > 3 ? args[3] : null);
        if (args.Length >= 2 && args[0] == "--selftest") return SelfTest(args[1]);
        if (args.Length >= 2 && args[0] == "--cover")
        {
            // --cover <pack folder> [notint] [slot ...]: a cover made from those slots, as the Cover card makes it.
            var maker = new MainWindow();
            maker.OpenFolder(args[1]);
            var plain = args.Length > 2 && args[2] == "notint";
            maker.MakeCoverFrom(args.Skip(plain ? 3 : 2), tint: !plain);
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--shrink")
        {
            var shrinking = PackProject.Open(args[1]);
            foreach (var (name, r) in shrinking.ShrinkAll())
            {
                Console.WriteLine($"{name,-16} {r.Before / 1024,6} KB -> {r.After / 1024,5} KB  {r.WidthBefore}x{r.HeightBefore} -> {r.Width}x{r.Height}  frames {r.FramesBefore} -> {r.Frames}");
            }
            var problems = shrinking.Check().Where(f => f.Problem).Select(f => f.Text).ToList();
            Console.WriteLine(problems.Count == 0 ? "No problems left." : "Still: " + string.Join("; ", problems));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--fill")
        {
            Console.WriteLine($"{PackProject.Open(args[1]).FillAll()} slots given their own file.");
            return 0;
        }
        return 2;
    }

    /// <summary>The Miku theme, for the self-test: in "nyatch themes" beside the folder Nyatch runs from (or one above).</summary>
    private static string? TestTheme()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            var miku = Path.Combine(at.FullName, "nyatch themes", "miku");
            if (Directory.Exists(miku)) return miku;
        }
        return null;
    }

    private static int Shot(string folder, string png, string? slot)
    {
        var window = new MainWindow { ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
        window.OpenFolder(folder);
        if (slot is not null) window.PickSlot(slot.Replace(":crop", ""));
        if (slot?.EndsWith(":crop") == true) window.CropChosen();
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (window.Content is not FrameworkElement root) return 1;
        var picture = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var back = new DrawingVisual();
        using (var draw = back.RenderOpen()) draw.DrawRectangle(window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.Black, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        picture.Render(back);
        picture.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using (var file = File.Create(png)) encoder.Save(file);
        window.Close();
        return 0;
    }

    /// <summary>Make a pack from a copy of a folder, put a picture in a slot, make it, install it in a test place, and check each step.</summary>
    private static int SelfTest(string log)
    {
        var text = new StringBuilder();
        var failed = 0;
        void Check(bool ok, string what)
        {
            text.AppendLine((ok ? "ok      " : "FAILED  ") + what);
            if (!ok) failed++;
        }
        var root = Path.Combine(Path.GetTempPath(), "nyatch-packer-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = TestTheme() ?? "nyatch themes\\miku";
            Check(Directory.Exists(source), $"the Miku theme's folder is at {source}");
            var folder = Path.Combine(root, "theme");
            Directory.CreateDirectory(folder);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));

            var pack = PackProject.Open(folder);
            Check(pack.Manifest.Id == "miku", "pack.json is read");
            Check(pack.Manifest.AccentColour == "#39C5BB", "its colour is read");
            // The test starts from slots that borrow: copies made in the source pack are taken away.
            foreach (var (copy, _) in pack.Copies()) pack.RemovePicture(copy);
            Check(pack.Shown(CharacterSlots.Find("weather-snow")!)?.Name == "idle", "a slot without its own picture shows its fallback");

            pack.SetPicture("weather-snow", Path.Combine(folder, "sleeping.png"));
            Check(pack.Has("weather-snow") && pack.Shown(CharacterSlots.Find("weather-snow")!)?.Name == "weather-snow", "a picture put in a slot is its own");
            pack.SetPicture("weather-snow", Path.Combine(folder, "dance.gif"));
            Check(pack.PictureOf("weather-snow")!.EndsWith(".gif") && !File.Exists(Path.Combine(folder, "weather-snow.png")), "a GIF replaces a PNG in the same slot");
            var notPicture = Path.Combine(root, "not-a-picture.png");
            File.WriteAllText(notPicture, "hello");
            Check(Throws(() => pack.SetPicture("weather-rain", notPicture)), "a file that is not a picture is refused");
            pack.RemovePicture("weather-snow");
            Check(!pack.Has("weather-snow"), "a picture can be removed");

            var filled = pack.FillAll();
            var empty0 = CharacterSlots.All.Where(s => !pack.Has(s.Name)).ToList();
            Check(filled > 0 && empty0.All(s => pack.Shown(s) is null), $"every slot gets its own file ({filled} copied)");
            Check(File.ReadAllBytes(pack.PictureOf("weather-snow")!).SequenceEqual(File.ReadAllBytes(pack.PictureOf("idle")!)), "a copied slot holds the picture it used");
            Check(pack.FillAll() == 0, "filling again copies nothing");

            // Cropping: every frame is the same part of the uncut one, pixel for pixel, and Uncrop gives the file back.
            foreach (var (name, part) in new[] { ("idle", new Int32Rect(7, 5, 61, 73)), ("thinking", new Int32Rect(3, 2, 30, 31)), ("cool", new Int32Rect(20, 15, 90, 100)) })
            {
                var path = pack.PictureOf(name)!;
                var fileBefore = File.ReadAllBytes(path);
                var before = Picture.Compose(path)!;
                pack.Crop(name, part);
                var after = Picture.Compose(pack.PictureOf(name)!)!;
                var same = after.Width == part.Width && after.Height == part.Height && after.Frames.Count == before.Frames.Count && after.Delays.SequenceEqual(before.Delays);
                for (var f = 0; same && f < after.Frames.Count; f++)
                {
                    for (var y = 0; same && y < part.Height; y++)
                    {
                        for (var x = 0; same && x < part.Width; x++)
                        {
                            var a = ((y + part.Y) * before.Width + x + part.X) * 4;
                            var b = (y * part.Width + x) * 4;
                            for (var c = 0; c < 4; c++) same &= before.Frames[f][a + c] == after.Frames[f][b + c];
                        }
                    }
                }
                Check(same, $"{name} ({(before.IsGif ? $"GIF, {before.Frames.Count} frames" : "PNG")}) is cropped without changing a pixel or a delay");
                Check(pack.IsCropped(name) && File.ReadAllBytes(pack.OriginalOf(name)!).SequenceEqual(fileBefore), $"{name}'s uncut picture is kept");
                if (name == "thinking") continue;  // left cropped, to make the pack with
                pack.Uncrop(name);
                Check(!pack.IsCropped(name) && File.ReadAllBytes(pack.PictureOf(name)!).SequenceEqual(fileBefore), $"{name} uncropped is the file it was");
            }

            // Making smaller: the 80-frame GIF, at the size its slot needs, running just as long.
            pack.SetPicture("party", Path.Combine(folder, "neko-dance.gif"));
            var big = Picture.Compose(pack.PictureOf("party")!)!;
            var shrunk = pack.Shrink("party");
            var small = Picture.Compose(pack.PictureOf("party")!)!;
            Check(shrunk.After < shrunk.Before && Math.Max(small.Width, small.Height) <= pack.BestSide("party"), $"a GIF is made smaller ({shrunk.Before / 1024} KB to {shrunk.After / 1024} KB, {small.Width} × {small.Height} px)");
            Check(small.Frames.Count == Math.Min(big.FramesInFile, Picture.MaxFrames) || shrunk.Frames < shrunk.FramesBefore, $"its frames are kept unless it is still too big ({shrunk.Frames} of {shrunk.FramesBefore})");
            Check(Math.Abs(small.Delays.Sum() - big.Delays.Sum()) < 0.05, $"it runs just as long ({big.Delays.Sum():0.00} s, now {small.Delays.Sum():0.00} s)");
            Check(pack.IsCropped("party"), "its first picture is kept");
            pack.Uncrop("party");
            Check(File.ReadAllBytes(pack.PictureOf("party")!).SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "neko-dance.gif"))), "Restore original puts it back");
            pack.RemovePicture("party");

            var gif = Picture.Load(Path.Combine(folder, "idle.gif"));
            Check(gif is { IsGif: true, Frames.Count: 4 }, $"a GIF plays its frames ({gif?.Frames.Count} of 4)");
            var tile = Picture.Load(Path.Combine(folder, "cool.png"));
            Check(tile is { Tile: true }, "a sticker with its own background is seen as one");

            pack.Manifest = pack.Manifest with { Version = "2.0", Name = "Miku test" };
            pack.Save();
            var findings = pack.Check();
            Check(findings.All(f => !f.Problem), "the theme has no problems: " + string.Join("; ", findings.Where(f => f.Problem).Select(f => f.Text)));
            Check(findings.Any(f => f.Text.Contains("not a slot name")), "a picture no slot uses is pointed out");

            // A font of its own: named from the file, packed, installed.
            var consolas = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf");
            if (File.Exists(consolas))
            {
                Check(pack.SetFont(consolas) == "Consolas" && pack.FontPath is not null, "a font is taken, with its name (Consolas)");
            }

            var file2 = Path.Combine(root, "miku.nyatch");
            var made = pack.MakeFile(file2);
            Check(File.Exists(file2) && made.Version == "2.0", "the .nyatch file is made");
            using (var zip = System.IO.Compression.ZipFile.OpenRead(file2))
            {
                Check(zip.Entries.All(e => !e.FullName.Contains("originals")) && zip.Entries.Any(e => e.Name == "thinking.gif"), "the theme has the cropped picture, not the uncut one");
            }
            var installed = new CharacterPacks(Path.Combine(root, "installed"), root).InstallFile(file2);
            Check(installed.Name == "Miku test" && installed.AccentColour == "#39C5BB", "it installs as Nyatch installs it");
            if (File.Exists(consolas))
            {
                var place = new CharacterPacks(Path.Combine(root, "installed"), root);
                Check(installed.Font == "Consolas" && place.FontOf(installed.Id) is { } fontThere && File.ReadAllBytes(fontThere).SequenceEqual(File.ReadAllBytes(consolas)), "its font installs with it");
            }
            using (var zip = System.IO.Compression.ZipFile.OpenRead(file2))
            {
                Check(zip.Entries.All(e => e.Name != "LICENSE.txt"), "no licence goes into a theme");
            }

            var home = Path.Combine(root, "home");
            pack.MakeInto(home);
            Check(File.Exists(Path.Combine(home, "index.json")) && File.Exists(Path.Combine(home, "packs", "miku.nyatch")), "it is added to a theme list");

            var broken = Path.Combine(root, "broken");
            Directory.CreateDirectory(broken);
            var empty = PackProject.Open(broken);
            Check(empty.Check().Count(f => f.Problem) >= 2, "an empty theme is not ready (no cover, no idle picture)");
        }
        catch (Exception error)
        {
            Check(false, "unexpected: " + error);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
        text.AppendLine(failed == 0 ? "All passed." : $"{failed} failed.");
        File.WriteAllText(log, text.ToString());
        return failed == 0 ? 0 : 1;
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// Pictures of the settings window's pages, for checking how they look without opening the window:
/// <c>Nyatch.exe --shots &lt;folder&gt; [page ids] [Setting.Name=value]</c> draws each page (whole, however
/// long) to a PNG in the folder and stops. It works on a copy of the settings and saves nothing; the
/// window is never on screen. Settings given as Name=value are set on the copy first
/// ("General.SettingsTheme=dark"), page ids keep it to those pages, and width=760 sets the window's width.
/// </summary>
internal static class SettingsShots
{
    public static void Run(string folder, string[] options)
    {
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(Path.GetTempPath(), "nyatch-shots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        try
        {
            var saved = Path.Combine(SettingsStore.DefaultFolder, "settings.json");
            if (File.Exists(saved)) File.Copy(saved, Path.Combine(copy, "settings.json"));
            var settings = new SettingsStore(SettingsSchema.Load(), copy);
            var pages = new List<string>();
            var width = 1100.0;
            foreach (var option in options)
            {
                var at = option.IndexOf('=');
                if (at > 0 && option[..at].Equals("width", StringComparison.OrdinalIgnoreCase)) width = double.Parse(option[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture);
                else if (at > 0) settings.Set(option[..at], option[(at + 1)..]);
                else pages.Add(option);
            }
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Dark };
            var window = new SettingsWindow(settings, new ChatHistory(copy), new Updater(settings, (_, _) => { }, () => { }), () => SettingsWindow.ChangesPassedOn++)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000,
                Top = -30000,
                Width = width,
                Height = 1000,
            };
            window.Show();
            if (pages.Remove("switches")) window.CheckSwitches(folder);
            if (pages.Count > 0 || !options.Contains("switches")) window.SaveShots(folder, pages);
            window.Close();
        }
        finally
        {
            try
            {
                Directory.Delete(copy, recursive: true);
            }
            catch (IOException)
            {
                // A leftover folder in Temp does no harm.
            }
        }
    }
}

internal sealed partial class SettingsWindow
{
    /// <summary>Draw each page (the named ones, or all) to a PNG in the folder: the whole page, as tall as it is.</summary>
    internal void SaveShots(string folder, IReadOnlyList<string> only)
    {
        var number = 0;
        void Shot(string name)
        {
            // Let the page lay itself out and load; a page taller than the window is drawn a screenful at a time.
            Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (Content is not FrameworkElement root) return;
            number++;
            var part = 0;
            for (var offset = 0.0; ; offset += Math.Max(200, _scroll.ViewportHeight - 40))
            {
                _scroll.ScrollToVerticalOffset(offset);
                Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var width = root.ActualWidth;
                var height = root.ActualHeight;
                var picture = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
                var backdrop = new DrawingVisual();
                using (var draw = backdrop.RenderOpen()) draw.DrawRectangle(_rail, null, new Rect(0, 0, width, height));
                picture.Render(backdrop);
                picture.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(picture));
                using (var file = File.Create(Path.Combine(folder, $"{number:00}-{name}-{(char)('a' + part++)}.png"))) encoder.Save(file);
                if (offset + _scroll.ViewportHeight >= _scroll.ExtentHeight - 1 || part >= 8) break;
            }
        }

        foreach (var page in SettingsLayout.Pages)
        {
            if (only.Count > 0 && !only.Contains(page.Id)) continue;
            if (page.Id == "top-pages")
            {
                foreach (var choice in TopPageChoices)
                {
                    _topChosen = choice.Id;
                    OpenPage(page.Id);
                    Shot($"{page.Id}-{choice.Id}");
                }
            }
            else if (page.Id == "deck-pages")
            {
                foreach (var id in new[] { "apps", "tray", "gemini", "focus" })
                {
                    _deckChosen = id;
                    OpenPage(page.Id);
                    Shot($"{page.Id}-{id}");
                }
            }
            else
            {
                OpenPage(page.Id);
                Shot(page.Id);
            }
        }
        if (only.Count == 0 || only.Contains("search"))
        {
            _search.Text = "hotkey";
            Shot("search");
            _search.Text = "";
        }
    }
}

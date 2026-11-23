using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.App;

// The Clipboard page: what was copied lately (text, pictures and screenshots, files), with pins, as Windows' own clipboard history.
internal sealed partial class DeckWindow
{
    private const int ClipboardUpdate = 0x031D;
    private HwndSource? _clipSource;
    private bool _clipListening;
    private DateTime _ownCopyAt;
    private int _clipVersion;
    private IntPtr _lastForeground;
    private readonly DispatcherTimer _clipRead = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private void StartClipboard()
    {
        if (_clipSource is not null) return;
        _clipSource = HwndSource.FromHwnd(new WindowInteropHelper(this).EnsureHandle());
        _clipSource?.AddHook((IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (message == ClipboardUpdate)
            {
                // Apps put several formats on the clipboard one after another; read once they are done.
                _clipRead.Stop();
                _clipRead.Start();
            }
            return IntPtr.Zero;
        });
        _clipRead.Tick += (_, _) =>
        {
            _clipRead.Stop();
            ReadClipboard();
        };
    }

    /// <summary>Start or stop recording what is copied. It records only while the second pill is on.</summary>
    private void ListenToClipboard(bool on)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        StartClipboard();
        if (on == _clipListening) return;
        _clipListening = on && Native.ListenToClipboard(handle, true);
        if (!on) Native.ListenToClipboard(handle, false);
    }

    /// <summary>Remember the app in front (not this one), so a clipboard item can be pasted back into it.</summary>
    private void NoteForeground()
    {
        if (Native.ForeignForeground() is var window && window != IntPtr.Zero) _lastForeground = window;
    }

    private void ReadClipboard()
    {
        // What this pill put on the clipboard itself is not new.
        if (DateTime.UtcNow - _ownCopyAt < TimeSpan.FromMilliseconds(700)) return;
        // Remembering can be turned off (the Apps & files page of the settings).
        if (_settings.Get("Deck.ClipboardRemember") != "true") return;
        IDataObject? data = null;
        for (var attempt = 0; attempt < 4 && data is null; attempt++)
        {
            try
            {
                data = Clipboard.GetDataObject();
            }
            catch (Exception e) when (e is COMException or ExternalException)
            {
                // Another app still has the clipboard open.
                System.Threading.Thread.Sleep(40);
            }
        }
        if (data is null) return;
        try
        {
            var formats = data.GetFormats(false);
            // Password managers and the like ask clipboard histories to leave their copies alone.
            if (formats.Contains("ExcludeClipboardContentFromMonitorProcessing") || formats.Contains("Clipboard Viewer Ignore")) return;
            if (formats.Contains("CanIncludeInClipboardHistory") && data.GetData("CanIncludeInClipboardHistory") is MemoryStream allowed && allowed.Length >= 4 && BitConverter.ToInt32(allowed.ToArray(), 0) == 0) return;

            var now = DateTime.Now;
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                _clips.AddFiles(files, now);
                ClipsChanged();
            }
            else if (_settings.Get("Deck.ClipboardPictures") == "true" && ClipboardPicture(data, formats) is { } picture)
            {
                var file = _clips.NewImagePath();
                var folder = _clips.Folder;
                // The clipboard's picture belongs to this thread; a frozen copy may be read on any.
                var copy = new WriteableBitmap(picture);
                copy.Freeze();
                Task.Run(async () =>
                {
                    // Hashing and saving a large screenshot is kept off the screen's thread.
                    var hash = PixelHash(copy);
                    // A screenshot Windows saved itself is pointed to, not copied a second time.
                    if (await SavedScreenshot(copy) is { } saved) return (hash, saved);
                    Directory.CreateDirectory(folder);
                    using (var output = File.Create(file))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(copy));
                        encoder.Save(output);
                    }
                    return (hash, file);
                }).ContinueWith(done =>
                {
                    if (done.IsCompletedSuccessfully)
                    {
                        _clips.AddImage(done.Result.Item2, done.Result.Item1, now);
                        ClipsChanged();
                    }
                    else
                    {
                        _ = done.Exception;  // seen: a picture that could not be saved is skipped
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text)
            {
                if (_clips.AddText(text, now) is not null) ClipsChanged();
            }
        }
        catch (Exception e) when (e is COMException or ExternalException or OutOfMemoryException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            // The clipboard changed or refused while being read; the next copy will be read as usual.
        }
    }

    private void ClipsChanged() => _clipVersion++;

    /// <summary>A copied picture, as a frozen bitmap: the PNG copy if the app gave one, else the device-independent bitmap.</summary>
    private static BitmapSource? ClipboardPicture(IDataObject data, string[] formats)
    {
        try
        {
            if (formats.Contains("PNG") && data.GetData("PNG") is MemoryStream png)
            {
                var frame = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                frame.Freeze();
                return frame;
            }
            if (data.GetDataPresent(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dib)
            {
                var bytes = dib.ToArray();
                if (bytes.Length < 40) return null;
                // A DIB is a .bmp file without its 14-byte header; put one in front and let the decoder read it.
                var headerSize = BitConverter.ToInt32(bytes, 0);
                var bits = BitConverter.ToInt16(bytes, 14);
                var compression = BitConverter.ToInt32(bytes, 16);
                var used = BitConverter.ToInt32(bytes, 32);
                var colours = used != 0 ? used : bits <= 8 ? 1 << bits : 0;
                var masks = compression == 3 && headerSize == 40 ? 12 : 0;
                var file = new byte[14 + bytes.Length];
                file[0] = (byte)'B';
                file[1] = (byte)'M';
                BitConverter.GetBytes(file.Length).CopyTo(file, 2);
                BitConverter.GetBytes(14 + headerSize + masks + colours * 4).CopyTo(file, 10);
                bytes.CopyTo(file, 14);
                var frame = new BmpBitmapDecoder(new MemoryStream(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                // Screenshots carry an empty alpha channel; read them as opaque.
                BitmapSource picture = frame.Format == PixelFormats.Bgra32 ? new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0) : frame;
                picture = new WriteableBitmap(picture);
                picture.Freeze();
                return picture;
            }
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException or COMException or OverflowException)
        {
        }
        return null;
    }

    /// <summary>
    /// The file in Pictures\Screenshots that is this picture, if Windows (Snipping Tool, Print Screen)
    /// saved it there just now. It may land a moment after the clipboard, so this looks a few times.
    /// Compared by colour only: clipboard pictures often carry a blank transparency the file does not.
    /// </summary>
    private static async Task<string?> SavedScreenshot(BitmapSource picture)
    {
        var folder = Native.ScreenshotsFolder();
        if (folder is null || !Directory.Exists(folder)) return null;
        string? wanted = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var recent = new DirectoryInfo(folder).EnumerateFiles("*.png")
                .Where(f => DateTime.Now - f.LastWriteTime < TimeSpan.FromSeconds(20))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(3);
            foreach (var file in recent)
            {
                try
                {
                    var frame = BitmapFrame.Create(new Uri(file.FullName), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                    if (frame.PixelWidth != picture.PixelWidth || frame.PixelHeight != picture.PixelHeight) continue;
                    wanted ??= ColourHash(picture);
                    if (ColourHash(frame) == wanted) return file.FullName;
                }
                catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or FileFormatException or UnauthorizedAccessException)
                {
                    // Still being written: looked at again on the next round.
                }
            }
            await Task.Delay(400);
        }
        return null;
    }

    private static string ColourHash(BitmapSource picture)
    {
        var plain = new FormatConvertedBitmap(picture, PixelFormats.Bgr24, null, 0);
        var stride = (plain.PixelWidth * 3 + 3) & ~3;
        var pixels = new byte[stride * plain.PixelHeight];
        plain.CopyPixels(pixels, stride, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }

    private static string PixelHash(BitmapSource picture)
    {
        var plain = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        var stride = plain.PixelWidth * 4;
        var pixels = new byte[stride * plain.PixelHeight];
        plain.CopyPixels(pixels, stride, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }

    /// <summary>Put an item back on the clipboard, and paste it into the app that was in front unless <paramref name="pasteToo"/> is false.</summary>
    private void UseClip(ClipItem item, bool pasteToo)
    {
        try
        {
            _ownCopyAt = DateTime.UtcNow;
            switch (item.Kind)
            {
                case ClipKind.Text:
                    Clipboard.SetText(item.Text);
                    break;
                case ClipKind.Files:
                    var list = new StringCollection();
                    list.AddRange(item.Files);
                    Clipboard.SetFileDropList(list);
                    break;
                case ClipKind.Image when item.Image is not null:
                    var picture = BitmapDecoder.Create(new Uri(item.Image), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    Clipboard.SetImage(picture);
                    break;
            }
        }
        catch (Exception e) when (e is COMException or ExternalException or IOException or NotSupportedException or ArgumentException or UriFormatException)
        {
            return;
        }
        _clips.Touch(item.Id, DateTime.Now);
        ClipsChanged();
        if (!pasteToo) return;
        var target = _lastForeground;
        Collapse();
        Native.PasteInto(target);
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    // The clipboard version last sent to the island, and the items as sent (by the action numbers).
    private int _clipSent = -1;
    private List<ClipItem> _clipsSent = [];

    /// <summary>The Clipboard page's items for the island (see its DrawDeckClipboard).</summary>
    private string ClipboardData()
    {
        _clipSent = _clipVersion;
        _clipsSent = _clips.Items.ToList();
        static string Clean(string text) => text.Replace('\u001E', ' ').Replace('\u001F', ' ');
        return string.Join('\u001E', _clipsSent.Select(item =>
        {
            var kind = item.Kind switch { ClipKind.Image => "I", ClipKind.Files => "F", _ => "T" };
            var text = item.Kind == ClipKind.Text ? (item.Text.Length > 600 ? item.Text[..600] : item.Text) : "";
            var first = item.Files.FirstOrDefault() ?? "";
            var files = item.Files.Length switch
            {
                0 => "",
                1 => Path.GetFileName(first.TrimEnd(Path.DirectorySeparatorChar)),
                _ => $"{Path.GetFileName(first.TrimEnd(Path.DirectorySeparatorChar))} and {item.Files.Length - 1} more",
            };
            return string.Join('\u001F', kind, item.Pinned ? "1" : "0", Clean(text), Clean(item.Image ?? ""), Clean(files), Clean(first));
        }));
    }

    /// <summary>A click on the Clipboard page, from the island: index * 4 (+2 pin, +3 remove), or 1000000 to clear.</summary>
    private void ClipboardAction(int action)
    {
        if (action == 1000000)
        {
            _clips.ClearUnpinned();
            ClipsChanged();
        }
        else if (action is >= 0 and < 10000 && action / 4 < _clipsSent.Count)
        {
            var item = _clipsSent[action / 4];
            switch (action % 4)
            {
                case 0:
                    // Shift+click only copies it.
                    UseClip(item, pasteToo: (GetAsyncKeyState(0x10) & 0x8000) == 0);
                    break;
                case 2:
                    _clips.Pin(item.Id, !item.Pinned);
                    ClipsChanged();
                    break;
                case 3:
                    _clips.Remove(item.Id);
                    ClipsChanged();
                    break;
            }
        }
        SendNativeData();
    }

    private UIElement ClipboardTab()
    {
        var panel = new StackPanel();
        var count = Words("", 11.5, colour: Muted);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(0, 0, 8, 0);
        var clear = TrayButton("bin", 24, "Clear the history", "Clear everything that is not pinned");
        panel.Children.Add(PageTitle("Clipboard", count, clear));
        var list = new StackPanel();
        panel.Children.Add(list);
        var shown = -1;

        void Draw()
        {
            if (shown == _clipVersion) return;
            shown = _clipVersion;
            var items = _clips.Items;
            count.Text = items.Count == 0 ? "" : items.Count == 1 ? "1 item" : $"{items.Count} items";
            clear.IsEnabled = items.Any(i => !i.Pinned);
            list.Children.Clear();
            if (items.Count == 0)
            {
                list.Children.Add(Empty("", _clipListening ? "Copy something, or take a screenshot, and it shows up here." : "The clipboard history records while the second pill is on.", 170));
                return;
            }
            foreach (var item in items) list.Children.Add(ClipRow(item));
        }
        OnPress(clear, () =>
        {
            _clips.ClearUnpinned();
            ClipsChanged();
            Draw();
        });
        _everySecond.Add(Draw);
        Draw();
        return panel;
    }

    /// <summary>One clipboard item: a raised card with the text, the picture or the files, and pin and remove buttons.</summary>
    private FrameworkElement ClipRow(ClipItem item)
    {
        FrameworkElement body;
        switch (item.Kind)
        {
            case ClipKind.Image:
                var picture = new BitmapImage();
                try
                {
                    picture.BeginInit();
                    picture.CacheOption = BitmapCacheOption.OnLoad;
                    picture.DecodePixelWidth = 360;
                    picture.UriSource = new Uri(item.Image!);
                    picture.EndInit();
                    picture.Freeze();
                    var image = new Image { Source = picture, MaxHeight = 110, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    body = new Border { Child = image, CornerRadius = new CornerRadius(6), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
                }
                catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or UriFormatException or FileFormatException)
                {
                    body = Words("Picture (no longer readable)", 12, colour: Muted);
                }
                break;
            case ClipKind.Files:
                var files = new DockPanel();
                var icon = IconOrLetter(item.Files[0], Path.GetFileName(item.Files[0]), 18);
                icon.Margin = new Thickness(0, 0, 8, 0);
                files.Children.Add(icon);
                var names = Path.GetFileName(item.Files[0].TrimEnd('\\'));
                if (item.Files.Length > 1) names += $"  +{item.Files.Length - 1} more";
                files.Children.Add(new TextBlock { Text = names, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                body = files;
                break;
            default:
                body = new TextBlock { Text = item.Text.Trim().ReplaceLineEndings("\n"), FontSize = 12, Foreground = Ink, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 50 };
                break;
        }

        var pin = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, ToolTip = item.Pinned ? "Unpin" : "Pin: keep it, also after a restart", VerticalAlignment = VerticalAlignment.Top };
        pin.Child = new TextBlock { Text = item.Pinned ? "" : "", FontFamily = Glyphs, FontSize = 11, Foreground = item.Pinned ? Accent : Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(pin, item.Pinned ? "Unpin" : "Pin");
        pin.MouseEnter += (_, _) => pin.Background = RaisedStrong;
        pin.MouseLeave += (_, _) => pin.Background = Brushes.Transparent;
        OnPress(pin, () =>
        {
            _clips.Pin(item.Id, !item.Pinned);
            ClipsChanged();
        });
        var remove = TrayButton("remove", 22, "Remove from the history", "Remove");
        remove.VerticalAlignment = VerticalAlignment.Top;
        OnPress(remove, () =>
        {
            _clips.Remove(item.Id);
            ClipsChanged();
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        buttons.Children.Add(pin);
        buttons.Children.Add(remove);
        // The buttons show on hover, and the pin stays while pinned.
        remove.Opacity = 0;
        if (!item.Pinned) pin.Opacity = 0;

        var line = new DockPanel { Margin = new Thickness(10, 8, 6, 8) };
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.Add(buttons);
        line.Children.Add(body);
        var row = new Border
        {
            Child = line,
            CornerRadius = new CornerRadius(9),
            Background = Raised,
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = "Click to paste it into the app you were in. Shift+click only copies it.",
        };
        System.Windows.Automation.AutomationProperties.SetName(row, item.Kind switch { ClipKind.Image => "Picture", ClipKind.Files => "Files", _ => item.Text.Length > 80 ? item.Text[..80] : item.Text });
        row.MouseEnter += (_, _) =>
        {
            row.Background = RaisedStrong;
            remove.Opacity = pin.Opacity = 1;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Raised;
            remove.Opacity = 0;
            pin.Opacity = item.Pinned ? 1 : 0;
        };
        OnPress(row, () => UseClip(item, pasteToo: (Keyboard.Modifiers & ModifierKeys.Shift) == 0));
        return row;
    }
}

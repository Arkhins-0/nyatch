using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// A theme's own font: installed for this Windows account only (no administrator needed), as Windows'
/// Settings does when you install a font "for me", so the notches can write in it while the theme is in use.
/// </summary>
internal static class ThemeFonts
{
    private const string FontsKey = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceW(string file);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>The family name of the font to write in, if the theme in use has one and it is wanted; it is installed first.</summary>
    public static string? Ready(SettingsStore settings, CharacterPacks packs)
    {
        if (settings.Get("Characters.Enabled") != "true" || settings.Get("Characters.UseFont") != "true") return null;
        var id = settings.Get("Characters.Pack");
        if (packs.InstalledPack(id) is not { Font.Length: > 0 } pack || packs.FontOf(id) is not { } file) return null;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, $"Nyatch theme {id}{Path.GetExtension(file)}");
            var fresh = !File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(file));
            if (fresh)
            {
                try
                {
                    File.Copy(file, target, overwrite: true);
                }
                catch (IOException) when (File.Exists(target))
                {
                    // In use by Windows: the one already there is kept until the next start.
                    fresh = false;
                }
            }
            using (var key = Registry.CurrentUser.CreateSubKey(FontsKey))
            {
                var name = $"{pack.Font} (Nyatch theme {id})";
                if (key.GetValue(name) as string != target) key.SetValue(name, target);
            }
            if (fresh)
            {
                // Usable at once, and other programs are told a font came.
                AddFontResourceW(target);
                SendMessageTimeoutW(new IntPtr(0xFFFF), 0x001D /* WM_FONTCHANGE */, IntPtr.Zero, IntPtr.Zero, 0x2 /* SMTO_ABORTIFHUNG */, 1000, out _);
            }
            return pack.Font;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}

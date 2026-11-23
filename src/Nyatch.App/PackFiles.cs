using System;
using Microsoft.Win32;

namespace Nyatch.App;

/// <summary>
/// .nyatch files (character packs) open with Nyatch: kept in this user's part of the registry, pointing
/// at this copy of Nyatch.exe, so opening one installs it.
/// </summary>
internal static class PackFiles
{
    private const string Kind = "Nyatch.CharacterPack";

    public static void Register()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            var command = $"\"{exe}\" \"%1\"";
            using var open = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Kind}\shell\open\command");
            if (open.GetValue(null) as string == command) return;
            open.SetValue(null, command);
            using var kind = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Kind}");
            kind.SetValue(null, "Nyatch theme");
            using var icon = kind.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{exe}\",0");
            using var extension = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.nyatch");
            extension.SetValue(null, Kind);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            // Without it, packs are still installed from the Characters page.
        }
    }

    /// <summary>Take the file type away again (on uninstalling).</summary>
    public static void Forget()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Kind}", throwOnMissingSubKey: false);
            using var extension = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.nyatch");
            if (extension?.GetValue(null) as string == Kind) Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.nyatch", throwOnMissingSubKey: false);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
        }
    }
}

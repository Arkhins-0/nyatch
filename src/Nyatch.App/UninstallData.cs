using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// The uninstaller's question: also remove your Nyatch settings and data from this PC? Run by the
/// uninstaller as you (Nyatch.exe --ask-remove-data), so it is your files and keys that are removed.
/// Anything that cannot be removed (in use) is left, quietly.
/// </summary>
internal static class UninstallData
{
    private const uint YesNo = 0x4, Question = 0x20, SecondDefault = 0x100, Foreground = 0x10000, Topmost = 0x40000;
    private const int Yes = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    public static void Ask()
    {
        var answer = MessageBoxW(IntPtr.Zero,
            "Also remove your Nyatch settings and data from this PC?\n\n" +
            "This removes your settings, notes and to-do lists, saved chats, clipboard history, " +
            "and the API keys saved for the chat pages.\n\n" +
            "Choose No to keep them for the next time you install Nyatch.",
            "Uninstall Nyatch", YesNo | Question | SecondDefault | Foreground | Topmost);
        if (answer == Yes) Remove();
    }

    private static void Remove()
    {
        DeleteFolder(SettingsStore.DefaultFolder);
        DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyatch"));
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Nyatch", throwOnMissingSubKey: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
        }
        foreach (var secret in Secrets.Known)
        {
            try
            {
                Secrets.Delete(secret.Id);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
    }

    private static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // In use (an Explorer still holding the folder sizes DLL): left behind.
            }
        }
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

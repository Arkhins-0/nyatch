using System;
using Nyatch.Core;
using Microsoft.Win32;

namespace Nyatch.App;

/// <summary>
/// "Start Nyatch when Windows starts": an entry in the current user's list of programs run at sign-in
/// (the one Task Manager shows under Startup apps). It points at whichever copy of Nyatch turned it on.
/// </summary>
internal static class SignInLaunch
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Nyatch";

    public static void Apply(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;
            if (on)
            {
                var command = $"\"{Environment.ProcessPath}\"";
                if (!string.Equals(key.GetValue(Name) as string, command, StringComparison.OrdinalIgnoreCase)) key.SetValue(Name, command);
            }
            else if (key.GetValue(Name) is not null)
            {
                key.DeleteValue(Name, throwOnMissingValue: false);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            // Windows refused; the setting stays as chosen and is tried again on the next change.
        }
    }

    /// <summary>
    /// The choices made in the installer's pages ("Start Nyatch when Windows starts", "Keep Nyatch up to date"),
    /// left in the registry for Nyatch to take into its settings the first time it starts. True if there were any.
    /// </summary>
    public static bool TakeInstallerChoices(SettingsStore settings)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Nyatch", writable: true);
            if (key is null) return false;
            var taken = false;
            foreach (var (name, setting) in new[] { ("SetupLaunchAtStartup", "General.LaunchAtStartup"), ("SetupAutoUpdate", "Updates.AutoInstall") })
            {
                if (key.GetValue(name) is not string choice) continue;
                var on = choice == "on";
                settings.Set(setting, on ? "true" : "false");
                // Keeping up to date needs checking too.
                if (setting == "Updates.AutoInstall" && on) settings.Set("Updates.AutoCheck", "true");
                key.DeleteValue(name, throwOnMissingValue: false);
                taken = true;
            }
            return taken;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return false;
        }
    }
}

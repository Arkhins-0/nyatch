using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Nyatch.App;

/// <summary>
/// The other apps' sounds, made quieter while a timer or an alarm rings and put back after: each app's own
/// volume in the Windows mixer (its audio session on the default speakers), never Windows' main volume.
/// Only the volumes this lowered are put back, and only to what they were.
/// </summary>
internal static class OtherSounds
{
    private static readonly List<(ISimpleAudioVolume Volume, float Was)> Lowered = [];

    /// <summary>Lower every other app's sound to this part of what it is (0.2: a fifth).</summary>
    public static void Lower(float part)
    {
        PutBack();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var device) != 0 || device is null) return;
            var managerId = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref managerId, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var activated) != 0 || activated is not IAudioSessionManager2 manager) return;
            if (manager.GetSessionEnumerator(out var sessions) != 0 || sessions is null) return;
            sessions.GetCount(out var count);
            var own = (uint)Environment.ProcessId;
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) != 0 || session is not IAudioSessionControl2 control) continue;
                if (control.GetProcessId(out var process) == 0 && process == own) continue;
                // The system sounds' session is left alone (the alarm may be one of them).
                if (control.IsSystemSoundsSession() == 0) continue;
                if (session is not ISimpleAudioVolume volume) continue;
                if (volume.GetMasterVolume(out var was) != 0) continue;
                var context = Guid.Empty;
                if (volume.SetMasterVolume(was * part, ref context) == 0) Lowered.Add((volume, was));
            }
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        {
        }
    }

    /// <summary>Put back the volumes this lowered.</summary>
    public static void PutBack()
    {
        foreach (var (volume, was) in Lowered)
        {
            try
            {
                var context = Guid.Empty;
                volume.SetMasterVolume(was, ref context);
            }
            catch (COMException)
            {
                // The app has gone.
            }
        }
        Lowered.Clear();
    }

    // ---------- Windows' audio sessions ----------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint process);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute(bool mute, ref Guid context);
        [PreserveSig] int GetMute(out bool mute);
    }
}

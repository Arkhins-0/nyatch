using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nyatch.App;

/// <summary>
/// A timer or an alarm ringing: its sound plays over and over, and the top notch says what rings, until it
/// is stopped (Enter), started again (Space: the timer again, the alarm snoozed) or two minutes
/// pass. Enter and Space are taken only while it rings. Lives on the app's thread.
/// </summary>
internal sealed class Ringer
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);

    private const int HotkeyMessage = 0x0312;
    private const int AgainId = 0x5201, StopId = 0x5202;
    private const uint NoRepeat = 0x4000, Enter = 0x0D, Space = 0x20;
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(2);

    private readonly IntPtr _window;
    private readonly DispatcherTimer _limit = new() { Interval = Longest };
    private MediaPlayer? _player;
    private Action? _again;

    public Ringer(IntPtr window)
    {
        _window = window;
        _limit.Tick += (_, _) => Stop();
        HwndSource.FromHwnd(window)?.AddHook((IntPtr _, int message, IntPtr w, IntPtr _, ref bool handled) =>
        {
            if (message != HotkeyMessage || w.ToInt32() is not (AgainId or StopId)) return IntPtr.Zero;
            handled = true;
            var again = w.ToInt32() == AgainId ? _again : null;
            Stop();
            again?.Invoke();
            return IntPtr.Zero;
        });
    }

    public bool Ringing { get; private set; }

    /// <summary>The Windows alarm, when no sound file is chosen (or the chosen one is gone).</summary>
    public static string SoundOrDefault(string chosen)
    {
        if (chosen.Length > 0 && File.Exists(chosen)) return chosen;
        var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        foreach (var name in new[] { "Alarm01.wav", "Alarm02.wav", "Ring01.wav", "notify.wav" })
        {
            var path = Path.Combine(media, name);
            if (File.Exists(path)) return path;
        }
        return "";
    }

    /// <summary>
    /// Ring: "Timer finished", the keys' words under it ("Space again · Enter stop"), a sound (a file; empty: the
    /// Windows alarm; null: silent) at a volume (0 to 1), whether the other apps go quieter meanwhile, and what Space does.
    /// </summary>
    public void Start(string title, string keys, string againWord, string? sound, double volume, bool lowerOthers, Action? again)
    {
        Stop();
        Ringing = true;
        _again = again;
        // The top notch shows the title, the keys under it, and two buttons: againWord ("Again", "Snooze") and Stop.
        Island.SetRinging(title + "\n" + keys + "\n" + againWord);
        RegisterHotKey(_window, AgainId, NoRepeat, Space);
        RegisterHotKey(_window, StopId, NoRepeat, Enter);
        if (sound is not null && SoundOrDefault(sound) is { Length: > 0 } file)
        {
            if (lowerOthers) OtherSounds.Lower(0.2f);
            _player = new MediaPlayer { Volume = Math.Clamp(volume, 0.05, 1.0) };
            _player.MediaEnded += (_, _) =>
            {
                if (_player is null) return;
                _player.Position = TimeSpan.Zero;
                _player.Play();
            };
            _player.Open(new Uri(file));
            _player.Play();
        }
        _limit.Stop();
        _limit.Start();
    }

    /// <summary>The top notch's buttons, as Space (again: true) and Enter do.</summary>
    public void Press(bool again)
    {
        if (!Ringing) return;
        var then = again ? _again : null;
        Stop();
        then?.Invoke();
    }

    public void Stop()
    {
        _limit.Stop();
        if (_player is { } player)
        {
            _player = null;
            player.Stop();
            player.Close();
        }
        OtherSounds.PutBack();
        if (!Ringing) return;
        Ringing = false;
        _again = null;
        UnregisterHotKey(_window, AgainId);
        UnregisterHotKey(_window, StopId);
        Island.SetRinging(null);
    }

    // ---------- Trying a sound (the settings) ----------

    private static MediaPlayer? _trying;

    /// <summary>Stop a sound being tried, if one is playing (the window it was tried from closed).</summary>
    public static void StopTrying()
    {
        if (_trying is not { } playing) return;
        _trying = null;
        playing.Stop();
        playing.Close();
    }

    /// <summary>Play a sound once, to hear it (a second call stops it).</summary>
    public static void Try(string sound)
    {
        if (_trying is { } playing)
        {
            _trying = null;
            playing.Stop();
            playing.Close();
            return;
        }
        if (SoundOrDefault(sound) is not { Length: > 0 } file) return;
        var player = new MediaPlayer();
        player.MediaEnded += (_, _) =>
        {
            if (ReferenceEquals(_trying, player)) _trying = null;
            player.Close();
        };
        player.Open(new Uri(file));
        player.Play();
        _trying = player;
    }
}

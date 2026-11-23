using System;
using System.Globalization;

namespace Nyatch.App;

// The Focus page: a timer, a stopwatch and a pomodoro. The island draws it (native); this keeps the
// clocks, sends what they show twice a second while the page is on show, and does what its buttons ask.
internal sealed partial class DeckWindow
{
    private static readonly int[] TimerLengths = [5, 10, 25, 45];

    /// <summary>What the Focus page shows: three clocks, then the timer's length (see the island's DrawDeckFocus).</summary>
    private string FocusData()
    {
        var now = DateTime.Now;
        static string Part(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        string Clockface(string title, TimeSpan shown, string caption, double sweep, bool running) =>
            string.Join('\u001F', title, Clock(shown), caption, Part(Math.Clamp(sweep, 0, 1)), running ? "1" : "0");

        var left = _timer.Left(now);
        var timer = Clockface("Timer", left,
            _timer.Running ? "left" : left < _timer.Length && left > TimeSpan.Zero ? "paused" : $"{(int)_timer.Length.TotalMinutes} min",
            _timer.Length > TimeSpan.Zero ? left / _timer.Length : 0, _timer.Running);

        var elapsed = _watch.Elapsed(now);
        // Once round the ring each minute.
        var watch = Clockface("Stopwatch", elapsed, _watch.Running ? "running" : elapsed > TimeSpan.Zero ? "paused" : "ready",
            elapsed.TotalSeconds % 60 / 60, _watch.Running);

        var clock = _pomodoro.Clock;
        var due = clock.Left(now);
        var tomato = Clockface(_pomodoro.OnBreak ? "Pomodoro · break" : "Pomodoro", due,
            (_pomodoro.OnBreak ? "break" : "focus") + (_pomodoro.Done > 0 ? $" · {_pomodoro.Done} done" : ""),
            clock.Length > TimeSpan.Zero ? 1 - due / clock.Length : 0, clock.Running);

        return string.Join('\u001E', timer, watch, tomato, ((int)_timer.Length.TotalMinutes).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The custom minutes box, from the island: Enter (1) starts the timer for that many minutes.</summary>
    private void FocusEdit(int what)
    {
        if (what != 1) return;
        if (int.TryParse(Island.DeckEditText(submitted: true), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes is > 0 and < 1000)
        {
            FocusAction(1000 + minutes);
        }
    }

    /// <summary>A button on the Focus page, from the island.</summary>
    private void FocusAction(int action)
    {
        var now = DateTime.Now;
        switch (action)
        {
            case 1:
                if (_timer.Running) _timer.Pause(now);
                else _timer.Start(now);
                break;
            case 2:
                _timer.Reset();
                break;
            case 3:
                if (_watch.Running) _watch.Pause(now);
                else _watch.Start(now);
                break;
            case 4:
                _watch.Reset();
                break;
            case 5:
                if (_pomodoro.Clock.Running) _pomodoro.Clock.Pause(now);
                else _pomodoro.Clock.Start(now);
                break;
            case 6:
                _pomodoro.Reset();
                break;
            case > 1000 and <= 1999:
                // Typed in the custom minutes box: that long, started at once.
                _timer.Reset(TimeSpan.FromMinutes(action - 1000));
                _timer.Start(now);
                break;
            case > 100 when Array.IndexOf(TimerLengths, action - 100) >= 0:
                _timer.Reset(TimeSpan.FromMinutes(action - 100));
                break;
            default:
                return;
        }
        UpdateStatus();
        SendNativeData();
    }
}

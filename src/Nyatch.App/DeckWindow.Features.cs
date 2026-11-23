using System;
using System.Globalization;

namespace Nyatch.App;

// The pages' own settings (the settings window's Apps & files and Tools pages of the second notch):
// how long the clocks run, how much the clipboard keeps. Read again whenever the settings change.
internal sealed partial class DeckWindow
{
    /// <summary>The timer's length the settings last gave, so a length picked on the page is not undone by another setting changing.</summary>
    private int _timerDefault = 10;

    /// <summary>A whole-number setting, kept within what the page can use.</summary>
    private int Whole(string setting, int low, int high) =>
        Math.Clamp(int.TryParse(_settings.Get(setting), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : low, low, high);

    private void ApplyFeatures()
    {
        var minutes = Whole("Deck.TimerMinutes", 1, 999);
        if (minutes != _timerDefault)
        {
            _timerDefault = minutes;
            // The timer takes its new length unless it is under way (or paused part-way).
            if (!_timer.Running && _timer.Left(DateTime.Now) == _timer.Length) _timer.Reset(TimeSpan.FromMinutes(minutes));
        }
        _pomodoro.Configure(
            TimeSpan.FromMinutes(Whole("Deck.PomodoroFocus", 1, 180)),
            TimeSpan.FromMinutes(Whole("Deck.PomodoroBreak", 1, 60)),
            TimeSpan.FromMinutes(Whole("Deck.PomodoroLongBreak", 1, 120)),
            Whole("Deck.PomodoroRounds", 2, 12));
        var keep = Whole("Deck.ClipboardKept", 5, 500);
        if (_clips.Keep != keep)
        {
            _clips.Keep = keep;
            ClipsChanged();
        }
        // What is on show follows at once (the Apps page's rows, the clocks' lengths).
        if (_expanded) SendNativeData();
    }
}

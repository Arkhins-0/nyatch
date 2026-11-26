using System;
using System.Globalization;
using System.Linq;
using Nyatch.Core;

namespace Nyatch.App;

// The Focus page: a timer, a stopwatch and the alarms. The island draws it (native); this keeps the clocks
// and the alarms, sends what they show twice a second while the page is on show, does what its buttons ask,
// and rings: a timer or an alarm rings until it is stopped (see Ringer).
internal sealed partial class DeckWindow
{
    private static readonly int[] TimerLengths = [5, 10, 25, 45];

    // Button numbers on the page (the island sends them back): 1-4 the clocks, 101-145 a timer length,
    // 1001-1999 a typed length, 200 a new alarm, 201 the settings, 300+ an alarm's row, 400+ its switch.
    private const int AddAlarm = 200, FocusSettings = 201, OpenAlarm = 300, SwitchAlarm = 400;

    /// <summary>What the Focus page shows: the timer, the stopwatch, the alarms, then the timer's length (see the island's DrawDeckFocus).</summary>
    private string FocusData()
    {
        var now = DateTime.Now;
        static string Part(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        static string Clean(string text) => text.Replace('\u001D', ' ').Replace('\u001E', ' ').Replace('\u001F', ' ');
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

        // The alarms: a head (when the next one rings, and the buttons), then a row each.
        // A snoozed alarm says so, with a countdown to when it rings again, in the head and on its row.
        var snoozed = _snoozed is { } sz && sz.At > now ? sz : ((Alarm Alarm, DateTime At)?)null;
        string SnoozeLeft(DateTime at)
        {
            var left = at - now;
            return left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}" : $"{left.Minutes}:{left.Seconds:00}";
        }
        var next = snoozed is { } snz ? "Snoozed " + SnoozeLeft(snz.At)
            : _alarms.NextAfter(now) is { } soonest ? "Next alarm " + AlarmStore.Until(now, soonest.At) : _alarms.All.Count == 0 ? "No alarms yet" : "All alarms off";
        var rows = _alarms.All.Select((a, i) => string.Join('\u001F',
            AlarmTime(a),
            Clean(a.Label),
            snoozed is { } so && so.Alarm.Id == a.Id ? "Snoozed " + SnoozeLeft(so.At) : Clean(a.DaysWords),
            // A snoozed alarm's switch is on (a one-off is already off once it rang): switching it off ends the snooze.
            a.On || snoozed is { } sn && sn.Alarm.Id == a.Id ? "1" : "0", (OpenAlarm + i).ToString(CultureInfo.InvariantCulture), (SwitchAlarm + i).ToString(CultureInfo.InvariantCulture)));
        // The head: its words, the + and gear buttons, and 1 while an alarm is snoozed (her picture shows it).
        var alarms = string.Join('\u001D', new[] { string.Join('\u001F', Clean(next), AddAlarm.ToString(CultureInfo.InvariantCulture), FocusSettings.ToString(CultureInfo.InvariantCulture), snoozed is null ? "0" : "1") }.Concat(rows));

        return string.Join('\u001E', timer, watch, alarms, ((int)_timer.Length.TotalMinutes).ToString(CultureInfo.InvariantCulture));
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
            case AddAlarm:
                EditAlarm(null);
                return;
            case FocusSettings:
                OpenSettingsAt?.Invoke("focus");
                return;
            case >= OpenAlarm and < SwitchAlarm when action - OpenAlarm < _alarms.All.Count:
                EditAlarm(_alarms.All[action - OpenAlarm]);
                return;
            case >= SwitchAlarm and < 500 when action - SwitchAlarm < _alarms.All.Count:
                var alarm = _alarms.All[action - SwitchAlarm];
                // A snoozed alarm's switch shows on: switching it off ends the snooze, and leaves the alarm itself as it
                // was (a one-off is off already; a repeating one still rings on its next day).
                if (_snoozed is { } snoozedNow && snoozedNow.Alarm.Id == alarm.Id && snoozedNow.At > now) _snoozed = null;
                else _alarms.Put(alarm with { On = !alarm.On });
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

    /// <summary>An alarm's own window: a new one (null), or this one to change or delete.</summary>
    private void EditAlarm(Alarm? alarm)
    {
        var editor = new AlarmWindow(alarm, _settings.Get("Deck.AlarmSound"), _settings.Get("General.SettingsTheme"), TwelveHour(), FirstDay());
        editor.Saved += saved =>
        {
            _alarms.Put(saved);
            SendNativeData();
        };
        editor.Deleted += id =>
        {
            _alarms.Remove(id);
            if (_snoozed is { } snoozed && snoozed.Alarm.Id == id) _snoozed = null;
            SendNativeData();
        };
        editor.Show();
        editor.Activate();
    }

    /// <summary>
    /// The top notch's Focus Timer menu, on the timer: 30 a 25-minute one, 31 a 50-minute one, 32 a 5-minute
    /// break, 33 pause or resume, 34 stop.
    /// </summary>
    private void FocusMenu(int item)
    {
        var now = DateTime.Now;
        switch (item)
        {
            case 30 or 31 or 32:
                _timer.Reset(TimeSpan.FromMinutes(item == 30 ? 25 : item == 31 ? 50 : 5));
                _timer.Start(now);
                break;
            case 33 when _timer.Running:
                _timer.Pause(now);
                break;
            case 33:
                _timer.Start(now);
                break;
            case 34:
                _timer.Reset();
                break;
            default:
                return;
        }
        UpdateStatus();
        SendNativeData();
    }

    /// <summary>How the timer is, for the top notch's menu: 0 not started, 1 running, 2 paused.</summary>
    private int FocusState() => _timer.Running ? 1 : _timer.Left(DateTime.Now) < _timer.Length ? 2 : 0;

    /// <summary>Whether times have AM and PM, as the clock is set (Settings, Clock & date).</summary>
    private bool TwelveHour() => _settings.Get("Modules.ClockFormat") switch
    {
        "12h" => true,
        "24h" => false,
        _ => CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h', StringComparison.Ordinal),
    };

    /// <summary>The first day of the week, as set (Settings, Top notch pages, Calendar).</summary>
    private DayOfWeek FirstDay() => _settings.Get("Modules.FirstDayOfWeek") switch
    {
        "sunday" => DayOfWeek.Sunday,
        "monday" => DayOfWeek.Monday,
        _ => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek == DayOfWeek.Sunday ? DayOfWeek.Sunday : DayOfWeek.Monday,
    };

    /// <summary>An alarm's time as the clock is set: "7:30 AM", or "07:30".</summary>
    private string AlarmTime(Alarm alarm) => TwelveHour()
        ? $"{(alarm.Hour % 12 == 0 ? 12 : alarm.Hour % 12)}:{alarm.Minute:00} {(alarm.Hour < 12 ? "AM" : "PM")}"
        : $"{alarm.Hour:00}:{alarm.Minute:00}";

    // ---------- Ringing ----------

    /// <summary>Once a second: the timer that ran out, the alarms whose time came, and a snoozed alarm due again.</summary>
    private void CheckRinging(DateTime now)
    {
        if (_timer.TakeFinished(now))
        {
            var length = _timer.Length;
            Ring("Timer finished", "Space again · Enter stop", "Again", "Your timer has run out.", _settings.Get("Deck.TimerSound"), () =>
            {
                _timer.Reset(length);
                _timer.Start(DateTime.Now);
                UpdateStatus();
                SendNativeData();
            });
        }

        var due = _alarms.Due(_alarmsChecked, now);
        _alarmsChecked = now;
        if (_snoozed is { } snoozed && snoozed.At <= now)
        {
            _snoozed = null;
            due.Add(snoozed.Alarm);
        }
        foreach (var alarm in due)
        {
            // An alarm for once is off after it rings.
            if (alarm.Days == AlarmDays.None && _alarms.Find(alarm.Id) is { On: true } kept) _alarms.Put(kept with { On = false });
            var time = AlarmTime(alarm);
            var title = alarm.Label.Length > 0 ? $"{alarm.Label} · {time}" : $"Alarm · {time}";
            // Space snoozes it (for as long as the settings say), Enter stops it.
            var snooze = int.TryParse(_settings.Get("Deck.SnoozeMinutes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? Math.Clamp(m, 1, 60) : 10;
            Ring(title, $"Space snooze {snooze} min · Enter stop", "Snooze", "Your alarm is ringing.", alarm.Sound.Length > 0 ? alarm.Sound : _settings.Get("Deck.AlarmSound"),
                () =>
                {
                    _snoozed = (alarm, DateTime.Now.AddMinutes(snooze));
                    // The top notch says so for a moment, with when it rings again.
                    var again = DateTime.Now.AddMinutes(snooze);
                    var at = TwelveHour() ? $"{(again.Hour % 12 == 0 ? 12 : again.Hour % 12)}:{again.Minute:00} {(again.Hour < 12 ? "AM" : "PM")}" : $"{again.Hour:00}:{again.Minute:00}";
                    Island.ShowNotice(alarm.Label.Length > 0 ? alarm.Label : "Alarm", $"Snoozed · rings again at {at}");
                    SendNativeData();
                });
        }
        if (due.Count > 0) SendNativeData();
    }

    private void Ring(string title, string keys, string againWord, string text, string sound, Action again)
    {
        if (_settings.Get("Deck.FocusNotify") == "true") _notify(title, text);
        var volume = int.TryParse(_settings.Get("Deck.RingVolume"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v / 100.0 : 1.0;
        _ringer?.Start(title, keys, againWord, _settings.Get("Deck.FocusSound") == "true" ? sound : null, volume, _settings.Get("Deck.RingLowersOthers") == "true", again);
    }
}

using System.Globalization;
using System.Text.Json;

namespace Nyatch.Core;

/// <summary>
/// An alarm: a time of day, a label, the days it rings on (none: once, the next time that time comes), whether
/// it is on, and its own sound (empty: the alarm sound in the settings).
/// </summary>
public sealed record Alarm(string Id, int Hour, int Minute, string Label = "", AlarmDays Days = AlarmDays.None, bool On = true, string Sound = "")
{
    /// <summary>The next time it rings strictly after a moment, or null while it is off.</summary>
    public DateTime? Next(DateTime after)
    {
        if (!On) return null;
        var day = after.Date;
        for (var i = 0; i <= 7; i++, day = day.AddDays(1))
        {
            var at = day.AddHours(Hour).AddMinutes(Minute);
            if (at <= after) continue;
            if (Days == AlarmDays.None || Days.HasFlag(AlarmDaysOf(at.DayOfWeek))) return at;
        }
        return null;
    }

    /// <summary>The days in words: "Once", "Every day", "Weekdays", "Weekends", or "Mon, Wed, Fri" (not saved).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DaysWords => Days switch
    {
        AlarmDays.None => "Once",
        AlarmDays.Every => "Every day",
        AlarmDays.Weekdays => "Weekdays",
        AlarmDays.Weekend => "Weekends",
        _ => string.Join(", ", Week.Where(d => Days.HasFlag(d.Flag)).Select(d => d.Short)),
    };

    public static AlarmDays AlarmDaysOf(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => AlarmDays.Monday,
        DayOfWeek.Tuesday => AlarmDays.Tuesday,
        DayOfWeek.Wednesday => AlarmDays.Wednesday,
        DayOfWeek.Thursday => AlarmDays.Thursday,
        DayOfWeek.Friday => AlarmDays.Friday,
        DayOfWeek.Saturday => AlarmDays.Saturday,
        _ => AlarmDays.Sunday,
    };

    /// <summary>The week from Monday, with each day's flag and short name.</summary>
    public static readonly IReadOnlyList<(AlarmDays Flag, string Short)> Week =
    [
        (AlarmDays.Monday, "Mon"), (AlarmDays.Tuesday, "Tue"), (AlarmDays.Wednesday, "Wed"), (AlarmDays.Thursday, "Thu"),
        (AlarmDays.Friday, "Fri"), (AlarmDays.Saturday, "Sat"), (AlarmDays.Sunday, "Sun"),
    ];
}

[Flags]
public enum AlarmDays
{
    None = 0,
    Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8, Friday = 16, Saturday = 32, Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    Every = Weekdays | Weekend,
}

/// <summary>The alarms, kept in alarms.json, in the order of their time of day.</summary>
public sealed class AlarmStore
{
    private readonly string _path;
    private List<Alarm> _alarms = [];

    public AlarmStore(string folder)
    {
        _path = Path.Combine(folder, "alarms.json");
        try
        {
            if (File.Exists(_path)) _alarms = JsonSerializer.Deserialize<List<Alarm>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            _alarms = [];
        }
        _alarms = [.. _alarms.Where(a => a.Hour is >= 0 and < 24 && a.Minute is >= 0 and < 60).OrderBy(a => a.Hour).ThenBy(a => a.Minute)];
    }

    public IReadOnlyList<Alarm> All => _alarms;

    public Alarm? Find(string id) => _alarms.Find(a => a.Id == id);

    /// <summary>Add an alarm, or replace the one with its id; the list stays in time order.</summary>
    public void Put(Alarm alarm)
    {
        _alarms.RemoveAll(a => a.Id == alarm.Id);
        _alarms.Add(alarm);
        _alarms = [.. _alarms.OrderBy(a => a.Hour).ThenBy(a => a.Minute)];
        Save();
    }

    public void Remove(string id)
    {
        if (_alarms.RemoveAll(a => a.Id == id) > 0) Save();
    }

    /// <summary>The next alarm to ring after a moment, and when.</summary>
    public (Alarm Alarm, DateTime At)? NextAfter(DateTime after) =>
        _alarms.Select(a => (Alarm: a, At: a.Next(after))).Where(x => x.At is not null).OrderBy(x => x.At).Select(x => ((Alarm, DateTime)?)(x.Alarm, x.At!.Value)).FirstOrDefault();

    /// <summary>The alarms due between two moments (after the first, up to and including the second).</summary>
    public List<Alarm> Due(DateTime from, DateTime to) => _alarms.Where(a => a.Next(from) is { } at && at <= to).ToList();

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_alarms, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>"in 3 h 20 min", "in 45 min", "in 1 min"; a day or more away: "Mon 07:30".</summary>
    public static string Until(DateTime now, DateTime at, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var left = at - now;
        if (left >= TimeSpan.FromHours(24)) return at.ToString("ddd ", culture) + at.ToString("t", culture);
        var minutes = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
        var hours = minutes / 60;
        minutes %= 60;
        return hours == 0 ? $"in {minutes} min" : minutes == 0 ? $"in {hours} h" : $"in {hours} h {minutes} min";
    }
}

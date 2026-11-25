using System.Globalization;
using Nyatch.Core;
using Xunit;

namespace Nyatch.Core.Tests;

public class AlarmTests
{
    // A Wednesday.
    private static readonly DateTime Wednesday9 = new(2026, 11, 25, 9, 0, 0);

    [Fact]
    public void AnAlarmOnceRingsTheNextTimeItsTimeComes()
    {
        var later = new Alarm("a", 10, 30);
        Assert.Equal(new DateTime(2026, 11, 25, 10, 30, 0), later.Next(Wednesday9));
        var earlier = new Alarm("b", 7, 0);
        Assert.Equal(new DateTime(2026, 11, 26, 7, 0, 0), earlier.Next(Wednesday9));
        Assert.Null((earlier with { On = false }).Next(Wednesday9));
    }

    [Fact]
    public void AnAlarmOnDaysRingsOnlyOnThem()
    {
        var weekend = new Alarm("w", 8, 0, Days: AlarmDays.Weekend);
        Assert.Equal(new DateTime(2026, 11, 28, 8, 0, 0), weekend.Next(Wednesday9));
        var weekdays = new Alarm("d", 9, 0, Days: AlarmDays.Weekdays);
        // Exactly at its time does not count: the next one is Thursday's.
        Assert.Equal(new DateTime(2026, 11, 26, 9, 0, 0), weekdays.Next(Wednesday9));
        Assert.Equal("Weekends", weekend.DaysWords);
        Assert.Equal("Mon, Wed, Fri", (weekend with { Days = AlarmDays.Monday | AlarmDays.Wednesday | AlarmDays.Friday }).DaysWords);
        Assert.Equal("Once", new Alarm("o", 1, 0).DaysWords);
    }

    [Fact]
    public void TheStoreKeepsItsAlarmsInTimeOrderAndSaysWhichAreDue()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nyatch-alarms-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AlarmStore(folder);
            store.Put(new Alarm("late", 22, 0, "Bed"));
            store.Put(new Alarm("early", 6, 45, "Up"));
            Assert.Equal(["early", "late"], store.All.Select(a => a.Id));
            Assert.Equal(["early", "late"], new AlarmStore(folder).All.Select(a => a.Id));

            var next = store.NextAfter(Wednesday9);
            Assert.Equal("late", next?.Alarm.Id);
            Assert.Equal(["late"], store.Due(new DateTime(2026, 11, 25, 21, 59, 59), new DateTime(2026, 11, 25, 22, 0, 0)).Select(a => a.Id));
            Assert.Empty(store.Due(Wednesday9, Wednesday9.AddSeconds(1)));

            store.Remove("late");
            Assert.Single(new AlarmStore(folder).All);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheTimeLeftIsSaidShortly()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        Assert.Equal("in 45 min", AlarmStore.Until(Wednesday9, Wednesday9.AddMinutes(45), culture));
        Assert.Equal("in 3 h 20 min", AlarmStore.Until(Wednesday9, Wednesday9.AddMinutes(200), culture));
        Assert.Equal("in 2 h", AlarmStore.Until(Wednesday9, Wednesday9.AddHours(2), culture));
        Assert.Equal("Fri 07:30", AlarmStore.Until(Wednesday9, new DateTime(2026, 11, 27, 7, 30, 0), culture));
    }
}

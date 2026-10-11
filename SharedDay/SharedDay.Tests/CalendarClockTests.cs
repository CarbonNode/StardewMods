using System.Text.Json;
using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

public sealed class CalendarClockTests
{
    [Fact]
    public void FirstTraversalGrantsEveryMinuteExactlyOnce()
    {
        var clock = new CalendarClock(5);
        Assert.Equal(360, clock.HighWaterMinute);
        Assert.Equal(0, clock.AdvanceToTimeOfDay(600));
        Assert.Equal(10, clock.AdvanceToTimeOfDay(610));
        Assert.Equal(10, clock.AdvanceToTimeOfDay(620));
        Assert.Equal(40, clock.AdvanceToTimeOfDay(700));
        Assert.Equal(420, clock.HighWaterMinute);
        Assert.Equal(5, clock.AbsoluteDay);
    }

    [Fact]
    public void RepeatingWholeDayCannotProduceAnotherDaysMachineTime()
    {
        var clock = new CalendarClock(5);
        int firstPass = 0;
        for (int minute = CalendarClock.MorningMinute; minute <= CalendarClock.EndOfDayMinute; minute += 10)
            firstPass += clock.AdvanceToMinute(minute);
        int replayPass = 0;
        for (int minute = CalendarClock.MorningMinute; minute <= CalendarClock.EndOfDayMinute; minute += 10)
            replayPass += clock.AdvanceToMinute(minute);
        Assert.Equal(1200, firstPass);
        Assert.Equal(0, replayPass);
        Assert.Equal(CalendarClock.EndOfDayMinute, clock.HighWaterMinute);
    }

    [Fact]
    public void LaterSessionCanExtendTimeBeyondEarlierPlayersFinish()
    {
        var clock = new CalendarClock(5);
        Assert.Equal(360, clock.AdvanceToTimeOfDay(1200));
        Assert.Equal(0, clock.AdvanceToTimeOfDay(600));
        Assert.Equal(0, clock.AdvanceToTimeOfDay(1150));
        Assert.Equal(0, clock.AdvanceToTimeOfDay(1200));
        Assert.Equal(10, clock.AdvanceToTimeOfDay(1210));
        Assert.Equal(10, clock.AdvanceToTimeOfDay(1220));
        Assert.Equal(740, clock.HighWaterMinute);
    }

    [Fact]
    public void NewMachineDuringReplayReceivesOnlyTimeBeyondGlobalHighWater()
    {
        var clock = new CalendarClock(5);
        clock.AdvanceToTimeOfDay(1200);
        clock.AdvanceToTimeOfDay(600);
        // A machine newly placed at 06:00 gets no separate clock or production allowance.
        int newMachineMinutes = clock.AdvanceToTimeOfDay(1100);
        newMachineMinutes += clock.AdvanceToTimeOfDay(1200);
        newMachineMinutes += clock.AdvanceToTimeOfDay(1210);
        Assert.Equal(10, newMachineMinutes);
    }

    [Fact]
    public void SerializationRetainsHighWaterAcrossReload()
    {
        var clock = new CalendarClock(5);
        clock.AdvanceToTimeOfDay(1850);
        var serialized = JsonSerializer.Serialize(clock.ExportState());
        var restored = CalendarClock.Restore(JsonSerializer.Deserialize<CalendarClockState>(serialized)!);
        Assert.Equal(5, restored.AbsoluteDay);
        Assert.Equal(1130, restored.HighWaterMinute);
        Assert.Equal(0, restored.AdvanceToTimeOfDay(600));
        Assert.Equal(0, restored.AdvanceToTimeOfDay(1850));
        Assert.Equal(10, restored.AdvanceToTimeOfDay(1900));
    }

    [Fact]
    public void ConfirmedNextDayGetsFreshBudget()
    {
        var clock = new CalendarClock(5);
        clock.AdvanceToTimeOfDay(2600);
        clock.ConfirmNextDay(6);
        Assert.Equal(6, clock.AbsoluteDay);
        Assert.Equal(360, clock.HighWaterMinute);
        Assert.Equal(10, clock.AdvanceToTimeOfDay(610));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void InvalidOrRepeatedDayConfirmationCannotResetBudget(int savedDay)
    {
        var clock = new CalendarClock(5);
        clock.AdvanceToTimeOfDay(1200);
        Assert.Throws<DayCoordinationException>(() => clock.ConfirmNextDay(savedDay));
        Assert.Equal(5, clock.AbsoluteDay);
        Assert.Equal(720, clock.HighWaterMinute);
    }

    [Theory]
    [InlineData(600, 360)]
    [InlineData(650, 410)]
    [InlineData(2350, 1430)]
    [InlineData(2400, 1440)]
    [InlineData(2550, 1550)]
    [InlineData(2600, 1560)]
    public void TimeConversionHandlesMidnightAndHourBoundaries(int time, int minute)
        => Assert.Equal(minute, CalendarClock.ToMinutes(time));

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(550)]
    [InlineData(660)]
    [InlineData(1260)]
    [InlineData(2601)]
    public void InvalidDisplayedClockIsRejectedWithoutChangingBudget(int time)
    {
        var clock = new CalendarClock(5);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceToTimeOfDay(time));
        Assert.Equal(360, clock.HighWaterMinute);
    }

    [Fact]
    public void InvalidPersistedBudgetIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarClock.Restore(new(-1, 360)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarClock.Restore(new(5, 359)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarClock.Restore(new(5, 1561)));
    }

    [Fact]
    public void StateSnapshotDoesNotChangeWhenBudgetAdvances()
    {
        var clock = new CalendarClock(5);
        var snapshot = clock.ExportState();
        clock.AdvanceToTimeOfDay(1200);
        Assert.Equal(360, snapshot.HighWaterMinute);
        Assert.Equal(720, clock.HighWaterMinute);
    }
}

using System.Text.Json;
using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>0.1.35: "Give a farmer a fresh day" (the owner, 2026-10-11: "reset someone's day").</summary>
public sealed class FreshDayTests
{
    private static readonly FarmerIdentity[] Roster = { new(101, "Alice"), new(202, "Bob"), new(303, "Charlie") };
    private static DayCoordinator Create() => DayCoordinator.Create("farm-42", 5, Roster);
    private static FarmerSnapshot Farmer(DayCoordinator day, long id) => day.Snapshot.Farmers.Single(f => f.Id == id);

    [Fact]
    public void AFinishedFarmerAloneOnTheFarmStartsAMorningOfTheirOwnAtOnce()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        int sessionBefore = day.Snapshot.SessionNumber;

        var fresh = day.RestartFarmer(101);

        Assert.Equal(FarmerStatus.Completed, fresh.Before);
        Assert.Equal(FreshDayStart.Now, fresh.Start);
        Assert.Equal(JoinDisposition.ResumeOwnClock, fresh.Join!.Disposition);
        Assert.True(fresh.Join.ResumesOwnClock);
        Assert.Equal(sessionBefore + 1, fresh.Join.SessionNumber);
        Assert.Equal(DayClock.Opens, Farmer(day, 101).TimeReached);
        Assert.True(Farmer(day, 101).IsActive);
        Assert.Equal(new long[] { 101 }, day.Snapshot.SessionParticipants);
        // The turn can be finished again.
        Assert.True(day.Complete(101));
    }

    [Fact]
    public void AFarmerMidTurnAloneGoesBackToMorningInANewTurn()
    {
        var day = Create();
        day.Join(101);
        int sessionBefore = day.Snapshot.SessionNumber;

        var fresh = day.RestartFarmer(101);

        Assert.Equal(FarmerStatus.Pending, fresh.Before);
        Assert.Equal(FreshDayStart.Now, fresh.Start);
        Assert.Equal(sessionBefore + 1, day.Snapshot.SessionNumber);
    }

    [Fact]
    public void WhileSomebodyElsePlaysTheyWaitHeldAndGetTheirMorningWhenThatTurnEnds()
    {
        var day = Create();
        day.Join(202);
        day.Join(101);
        Assert.True(Farmer(day, 101).IsInCurrentSession);

        var fresh = day.RestartFarmer(101);

        Assert.Equal(FreshDayStart.AfterTurnInProgress, fresh.Start);
        Assert.Null(fresh.Join);
        Assert.True(Farmer(day, 101).Waiting);
        Assert.False(Farmer(day, 101).IsInCurrentSession);
        Assert.False(Farmer(day, 101).IsActive);
        // Bob's turn is untouched.
        Assert.True(Farmer(day, 202).IsActive);
        Assert.Null(day.PromoteWaiter());

        day.Complete(202);
        var promoted = day.PromoteWaiter();

        Assert.NotNull(promoted);
        Assert.Equal(101, promoted!.Value.Id);
        Assert.Equal(JoinDisposition.ResumeOwnClock, promoted.Value.Join.Disposition);
        Assert.Equal(DayClock.Opens, Farmer(day, 101).TimeReached);
        Assert.True(Farmer(day, 101).IsActive);
    }

    [Fact]
    public void AnAwayFarmerGetsTheirMorningWhenTheyJoinAndNobodyPlays()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1340);

        var fresh = day.RestartFarmer(101);

        Assert.Equal(FreshDayStart.WhenTheyJoin, fresh.Start);
        Assert.Equal(DayClock.Opens, Farmer(day, 101).TimeReached);
        var joined = day.Join(101);
        Assert.Equal(JoinDisposition.ResumeOwnClock, joined.Disposition);
    }

    [Fact]
    public void AnAwayFarmerArrivingWhileSomebodyPlaysIsAskedToJoinOrWaitForTheirMorning()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Disconnect(101);
        day.RestartFarmer(101);
        day.Join(202);

        Assert.Equal(JoinDisposition.ChooseJoinOrWait, day.Join(101).Disposition);
        Assert.True(day.Wait(101));
        day.Complete(202);
        Assert.Equal(101, day.PromoteWaiter()!.Value.Id);
    }

    [Fact]
    public void ASkipOrAStepAwayNoLongerStands()
    {
        var day = Create();
        day.Skip(303);
        Assert.Equal(FarmerStatus.Skipped, day.RestartFarmer(303).Before);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 303).Status);

        day.Join(202);
        day.StepAway(202, 900);
        Assert.True(Farmer(day, 202).Away);
        day.RestartFarmer(202);
        Assert.False(Farmer(day, 202).Away);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 202).Status);
        Assert.Equal(DayClock.Opens, Farmer(day, 202).TimeReached);
    }

    [Fact]
    public void NobodyElseIsTouched()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Disconnect(101);
        day.Join(202);
        day.Disconnect(202, 1500);
        day.Skip(303);

        day.RestartFarmer(101);

        Assert.Equal(1500, Farmer(day, 202).TimeReached);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 202).Status);
        Assert.Equal(FarmerStatus.Skipped, Farmer(day, 303).Status);
    }

    [Fact]
    public void TheDayDoesNotEndWithoutThem()
    {
        var day = Create();
        foreach (var f in Roster) { day.Join(f.Id); day.Complete(f.Id); }
        Assert.True(day.IsReadyToAdvance);

        day.RestartFarmer(202);

        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void NotOnceTheNightHasStartedNorForSomeoneNotOnTheRoster()
    {
        var day = Create();
        Assert.Throws<DayCoordinationException>(() => day.RestartFarmer(999));
        foreach (var f in Roster) { day.Join(f.Id); day.Complete(f.Id); }
        day.BeginAdvance();
        Assert.Throws<DayCoordinationException>(() => day.RestartFarmer(101));
    }

    [Fact]
    public void AFreshDaySurvivesTheJournal()
    {
        var day = Create();
        day.Join(202);
        day.Join(101);
        day.RestartFarmer(101);

        var json = JsonSerializer.Serialize(day.ExportJournal());
        var back = DayCoordinator.Restore(JsonSerializer.Deserialize<DayJournal>(json)!);

        Assert.Equal(DayClock.Opens, back.Snapshot.Farmers.Single(f => f.Id == 101).TimeReached);
        Assert.Equal(FarmerStatus.Pending, back.Snapshot.Farmers.Single(f => f.Id == 101).Status);
    }

    [Fact]
    public void ARestartOfTheWholeDayAfterwardsDropsTheParkedMorning()
    {
        var day = Create();
        day.Join(202);
        day.Join(101);
        day.RestartFarmer(101);

        day.RestartDay();

        Assert.Null(Farmer(day, 101).TimeReached);
        Assert.False(Farmer(day, 101).Waiting);
        Assert.True(Farmer(day, 101).IsInCurrentSession);
    }

    [Fact]
    public void TheMarkCollectsEachFarmersMorningForTheDay()
    {
        string mark = FreshDayMark.Add(null, 5, 101, 17);
        mark = FreshDayMark.Add(mark, 5, -2283117480133497730, 19);
        var read = FreshDayMark.Read(mark)!;
        Assert.Equal(5, read.Day);
        Assert.Equal(17, read.Entries[101]);
        Assert.Equal(19, read.Entries[-2283117480133497730]);
        // Yesterday's entries go.
        var tomorrow = FreshDayMark.Read(FreshDayMark.Add(mark, 6, 202, 3))!;
        Assert.Equal(new long[] { 202 }, tomorrow.Entries.Keys.ToArray());
    }

    [Fact]
    public void OnlyTheNamedFarmerGoesToBedAndOnlyOncePerFreshDay()
    {
        string mark = FreshDayMark.Add(null, 5, 101, 17);
        string? due = FreshDayMark.Due(mark, null, 5, 101);
        Assert.Equal(FreshDayMark.Seen(5, 17), due);
        Assert.Null(FreshDayMark.Due(mark, due, 5, 101));
        // Somebody else on the farm is not moved.
        Assert.Null(FreshDayMark.Due(mark, null, 5, 202));
        // Bob's fresh day the same morning does not send Alice to bed again.
        string both = FreshDayMark.Add(mark, 5, 202, 23);
        Assert.Null(FreshDayMark.Due(both, due, 5, 101));
        Assert.Equal(FreshDayMark.Seen(5, 23), FreshDayMark.Due(both, null, 5, 202));
        // A second fresh day for Alice is a new number.
        Assert.NotNull(FreshDayMark.Due(FreshDayMark.Add(both, 5, 101, 30), due, 5, 101));
        // Yesterday's is nothing today.
        Assert.Null(FreshDayMark.Due(mark, null, 6, 101));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5")]
    [InlineData("x|101=1")]
    [InlineData("5|101")]
    [InlineData("5|abc=1")]
    [InlineData("5|101=-1")]
    public void ABrokenMarkIsNoMark(string? text) => Assert.Null(FreshDayMark.Read(text));

    [Theory]
    [InlineData(600, true)]
    [InlineData(610, false)]
    [InlineData(1340, false)]
    public void OnlyAParkedClockOf6AmIsAMorning(int clock, bool morning) => Assert.Equal(morning, FreshDayMark.IsMorning(clock));
}

using System.Text.Json;
using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>0.1.34: "Restart today for everyone" (the owner, 2026-10-09).</summary>
public sealed class DayRestartTests
{
    private static readonly FarmerIdentity[] Roster = { new(101, "Alice"), new(202, "Bob"), new(303, "Charlie") };
    private static DayCoordinator Create() => DayCoordinator.Create("farm-42", 5, Roster);
    private static FarmerSnapshot Farmer(DayCoordinator day, long id) => day.Snapshot.Farmers.Single(f => f.Id == id);

    [Fact]
    public void FinishedFarmersPlayAgainAndTheOnesOnTheFarmMakeOneNewTurn()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Disconnect(101);
        day.Join(202);
        int sessionBefore = day.Snapshot.SessionNumber;

        var playing = day.RestartDay();

        Assert.Equal(new long[] { 202 }, playing);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 101).Status);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 202).Status);
        Assert.Equal(sessionBefore + 1, day.Snapshot.SessionNumber);
        Assert.Equal(new long[] { 202 }, day.Snapshot.SessionParticipants);
        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void AFinishedFarmerStillOnTheFarmIsInTheNewTurn()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);

        var playing = day.RestartDay();

        Assert.Equal(new long[] { 101 }, playing);
        Assert.True(Farmer(day, 101).IsInCurrentSession);
        // And they can finish it again.
        Assert.True(day.Complete(101));
    }

    [Fact]
    public void ParkedClocksAndWaitsAreDropped()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1340);
        day.Join(202);
        Assert.Equal(1340, Farmer(day, 101).TimeReached);

        day.RestartDay();

        Assert.Null(Farmer(day, 101).TimeReached);
        // Back with no parked clock while Bob plays: straight into his turn, no "join or wait".
        Assert.Equal(JoinDisposition.JoinCurrentSession, day.Join(101).Disposition);
    }

    [Fact]
    public void AFarmerAwayComesBackAtMorningNotToTheirParkedClock()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1500);

        day.RestartDay();

        Assert.Equal(JoinDisposition.StartSession, day.Join(101).Disposition);
    }

    [Fact]
    public void SkipsStand()
    {
        var day = Create();
        day.Join(101);
        day.Skip(303);
        day.Complete(101);

        day.RestartDay();

        Assert.Equal(FarmerStatus.Skipped, Farmer(day, 303).Status);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 101).Status);
    }

    [Fact]
    public void NobodyOnTheFarmLeavesNoTurnAndTheFirstToArriveStartsAtMorning()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Disconnect(101);

        Assert.Empty(day.RestartDay());
        Assert.Empty(day.Snapshot.SessionParticipants);
        var joined = day.Join(202);
        Assert.Equal(JoinDisposition.StartSession, joined.Disposition);
        Assert.True(joined.StartsNewSession);
    }

    [Fact]
    public void NotOnceTheNightHasStarted()
    {
        var day = Create();
        foreach (var f in Roster) { day.Join(f.Id); day.Complete(f.Id); }
        day.BeginAdvance();

        Assert.Throws<DayCoordinationException>(() => day.RestartDay());
    }

    [Fact]
    public void TheRestartedDaySurvivesTheJournal()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Join(202);
        day.RestartDay();

        var json = JsonSerializer.Serialize(day.ExportJournal());
        var back = DayCoordinator.Restore(JsonSerializer.Deserialize<DayJournal>(json)!);

        Assert.Equal(day.Snapshot.SessionNumber, back.Snapshot.SessionNumber);
        Assert.All(back.Snapshot.Farmers, f => Assert.NotEqual(FarmerStatus.Completed, f.Status));
    }

    [Fact]
    public void TheMarkReadsBackAsWritten()
    {
        string mark = DayRestart.Write(5, 17, new long[] { 101, -2283117480133497730 });
        var read = DayRestart.Read(mark)!;
        Assert.Equal(5, read.Day);
        Assert.Equal(17, read.Number);
        Assert.Equal(new long[] { 101, -2283117480133497730 }, read.OnFarm);
        Assert.Equal(new long[0], DayRestart.Read(DayRestart.Write(5, 18, Array.Empty<long>()))!.OnFarm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5:17")]
    [InlineData("x:17:101")]
    [InlineData("5:17:abc")]
    public void ABrokenMarkIsNoMark(string? text) => Assert.Null(DayRestart.Read(text));

    [Fact]
    public void AFarmerOnTheFarmGoesToBedOnceForEachRestart()
    {
        string mark = DayRestart.Write(5, 17, new long[] { 101 });
        Assert.Equal(RestartAction.Bed, DayRestart.For(mark, null, 5, 101, 600));
        // Their game has dealt with it.
        Assert.Equal(RestartAction.None, DayRestart.For(mark, mark, 5, 101, 600));
        // A second restart the same day is a new mark.
        string again = DayRestart.Write(5, 23, new long[] { 101 });
        Assert.Equal(RestartAction.Bed, DayRestart.For(again, mark, 5, 101, 900));
    }

    [Fact]
    public void AFarmerArrivingLaterGoesToBedOnlyForTheMorning()
    {
        string mark = DayRestart.Write(5, 17, new long[] { 101 });
        Assert.Equal(RestartAction.Bed, DayRestart.For(mark, null, 5, 202, 600));
        Assert.Equal(RestartAction.Bed, DayRestart.For(mark, null, 5, 202, DayRestart.MorningUntil));
        Assert.Equal(RestartAction.Note, DayRestart.For(mark, null, 5, 202, 1130));
    }

    [Fact]
    public void YesterdaysRestartIsNothingToday()
    {
        string mark = DayRestart.Write(4, 17, new long[] { 101 });
        Assert.Equal(RestartAction.None, DayRestart.For(mark, null, 5, 101, 600));
        Assert.Equal(RestartAction.None, DayRestart.For(null, null, 5, 101, 600));
    }

    [Fact]
    public void TheHostSaysWhyItWillNotRestart()
    {
        Assert.Null(DayRestart.Refusal(open: true, nightUnderWay: false, festivalOrCutscene: false, paused: false));
        Assert.Contains("paused", DayRestart.Refusal(true, false, false, paused: true));
        Assert.Contains("night", DayRestart.Refusal(true, nightUnderWay: true, false, false));
        Assert.Contains("night", DayRestart.Refusal(open: false, false, false, false));
        Assert.Contains("festival", DayRestart.Refusal(true, false, festivalOrCutscene: true, false));
    }
}

using System.Text.Json;
using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>
/// 0.1.29: a farmer who leaves a turn unfinished keeps a clock of their own and holds nobody
/// else's; coming back while somebody else plays they choose between that turn and waiting; and
/// the nights that ran without a farmer are owed to them until shown.
/// </summary>
public sealed class ReturnAndNightsTests
{
    private static readonly FarmerIdentity[] Roster = { new(101, "Alice"), new(202, "Bob"), new(303, "Charlie") };
    private static DayCoordinator Create() => DayCoordinator.Create("farm-42", 5, Roster);
    private static FarmerSnapshot Farmer(DayCoordinator day, long id) => day.Snapshot.Farmers.Single(f => f.Id == id);

    // ---- a parked turn holds nobody ----

    [Fact]
    public void AnOfflineUnfinishedFarmerNoLongerPinsTheTurn()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        Assert.Equal(1410, Farmer(day, 101).TimeReached);
        var bob = day.Join(202);
        Assert.True(bob.StartsNewSession);
        Assert.Equal(2, bob.SessionNumber);
        Assert.False(Farmer(day, 101).IsInCurrentSession);
        Assert.True(Farmer(day, 202).IsActive);
    }

    [Fact]
    public void ADisconnectWithoutAClockOrOfAFinishedFarmerParksNothing()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101);
        Assert.Null(Farmer(day, 101).TimeReached);
        day.Join(202);
        day.Complete(202);
        day.Disconnect(202, 1500);
        Assert.Null(Farmer(day, 202).TimeReached);
        // An impossible clock is not kept either.
        day.Join(303);
        day.Disconnect(303, 2700);
        Assert.Null(Farmer(day, 303).TimeReached);
    }

    [Fact]
    public void ComingBackAloneResumesYourOwnClockInYourOwnTurn()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        var back = day.Join(101);
        Assert.Equal(JoinDisposition.ResumeOwnClock, back.Disposition);
        Assert.True(back.ResumesOwnClock);
        Assert.False(back.StartsNewSession);
        // Still the turn they left: its number is kept.
        Assert.Equal(1, back.SessionNumber);
        Assert.Equal(1410, Farmer(day, 101).TimeReached);
        Assert.True(Farmer(day, 101).IsActive);
        // Finishing clears the parked clock.
        day.Complete(101);
        Assert.Null(Farmer(day, 101).TimeReached);
    }

    [Fact]
    public void ComingBackAfterSomebodyElsesTurnEndedResumesYourClockInANewTurn()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        day.Join(202);
        day.Complete(202);
        var back = day.Join(101);
        Assert.Equal(JoinDisposition.ResumeOwnClock, back.Disposition);
        Assert.Equal(3, back.SessionNumber);
        Assert.Equal(new long[] { 101 }, day.Snapshot.SessionParticipants);
    }

    // ---- the choice ----

    [Fact]
    public void ComingBackWhileSomebodyPlaysAsksAndPutsYouInNoTurn()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        day.Join(202);
        var alice = day.Join(101);
        Assert.Equal(JoinDisposition.ChooseJoinOrWait, alice.Disposition);
        Assert.True(Farmer(day, 101).IsOnline);
        Assert.False(Farmer(day, 101).IsInCurrentSession);
        Assert.False(Farmer(day, 101).IsActive);
        Assert.Equal(new long[] { 202 }, day.Snapshot.SessionParticipants);
        // Bob's turn is not held still by a question Alice has not answered.
        Assert.True(Farmer(day, 202).IsActive);
        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void JoinNowPutsYouIntoTheTurnInProgressAtItsClock()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        day.Join(202);
        day.Join(101);
        var joined = day.JoinNow(101);
        Assert.Equal(JoinDisposition.JoinCurrentSession, joined!.Disposition);
        Assert.Equal(2, joined.SessionNumber);
        Assert.True(Farmer(day, 101).IsActive);
        // Asked once; already in a turn now.
        Assert.Null(day.JoinNow(101));
        Assert.False(day.Wait(101));
    }

    [Fact]
    public void WaitHoldsYouUntilTheTurnEndsThenYourOwnClockComesBack()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        day.Join(202);
        day.Join(101);
        Assert.True(day.Wait(101));
        Assert.True(Farmer(day, 101).Waiting);
        Assert.False(Farmer(day, 101).IsActive);
        // Nothing to promote while Bob plays.
        Assert.Null(day.PromoteWaiter());
        day.Complete(202);
        var promoted = day.PromoteWaiter();
        Assert.NotNull(promoted);
        Assert.Equal(101, promoted!.Value.Id);
        Assert.Equal(JoinDisposition.ResumeOwnClock, promoted.Value.Join.Disposition);
        Assert.Equal(3, promoted.Value.Join.SessionNumber);
        Assert.False(Farmer(day, 101).Waiting);
        Assert.True(Farmer(day, 101).IsActive);
        Assert.Equal(1410, Farmer(day, 101).TimeReached);
        // Promoted once.
        Assert.Null(day.PromoteWaiter());
    }

    [Fact]
    public void AWaiterIsPromotedWhenTheTurnsPlayerLeavesOrIsSkippedToo()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        day.Join(202);
        day.Join(101);
        day.Wait(101);
        day.Disconnect(202, 900);
        Assert.Equal(101, day.PromoteWaiter()!.Value.Id);

        var skipped = Create();
        skipped.Join(101);
        skipped.Disconnect(101, 1410);
        skipped.Join(202);
        skipped.Join(101);
        skipped.Wait(101);
        skipped.Skip(202);
        Assert.Equal(101, skipped.PromoteWaiter()!.Value.Id);
    }

    [Fact]
    public void TwoWaitersTakeTheirTurnsOneAfterTheOtherInRosterOrder()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        day.Join(202);
        day.Disconnect(202, 1300);
        day.Join(303);
        day.Join(101);
        day.Wait(101);
        day.Join(202);
        day.Wait(202);
        day.Complete(303);
        Assert.Equal(101, day.PromoteWaiter()!.Value.Id);
        Assert.Null(day.PromoteWaiter());
        Assert.True(Farmer(day, 202).Waiting);
        day.Complete(101);
        Assert.Equal(202, day.PromoteWaiter()!.Value.Id);
    }

    [Fact]
    public void AWaiterWhoLeavesStopsWaitingAndIsAskedAgainWhenBack()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        day.Join(202);
        day.Join(101);
        day.Wait(101);
        day.Disconnect(101);
        Assert.False(Farmer(day, 101).Waiting);
        Assert.Equal(1410, Farmer(day, 101).TimeReached);
        Assert.Equal(JoinDisposition.ChooseJoinOrWait, day.Join(101).Disposition);
    }

    [Fact]
    public void AJoinerWhileAWaiterPlaysTheirResumedTurnSharesThatClock()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        day.Join(202);
        day.Join(101);
        day.Wait(101);
        day.Complete(202);
        day.PromoteWaiter();
        Assert.Equal(JoinDisposition.JoinCurrentSession, day.Join(303).Disposition);
        Assert.Equal(new long[] { 101, 303 }, day.Snapshot.SessionParticipants);
    }

    [Fact]
    public void ParkingACurrentPlayersClockKeepsThemPlayingAndSurvivesAReload()
    {
        var day = Create();
        day.Join(101);
        Assert.True(day.Park(101, 1200));
        Assert.True(Farmer(day, 101).IsActive);
        Assert.Equal(1200, Farmer(day, 101).TimeReached);
        Assert.False(day.Park(202, 1200));
        var restored = DayCoordinator.Restore(JsonSerializer.Deserialize<DayJournal>(JsonSerializer.Serialize(day.ExportJournal()))!);
        Assert.Equal(JoinDisposition.ResumeOwnClock, restored.Join(101).Disposition);
    }

    // ---- nights owed ----

    [Fact]
    public void ANightThatRanWithoutAFarmerIsOwedToThemUntilShown()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Skip(202);
        day.Skip(303);
        var token = day.BeginAdvance();
        day.AcknowledgeSavedNextDay(token, 6, Roster);
        Assert.Null(Farmer(day, 101).NightsOwedFrom);
        Assert.Equal(5, Farmer(day, 202).NightsOwedFrom);
        Assert.Equal(5, Farmer(day, 303).NightsOwedFrom);
        // Shown through day 5: nothing owed any more.
        Assert.True(day.AcknowledgeNightsShown(202, 5));
        Assert.Null(Farmer(day, 202).NightsOwedFrom);
        Assert.False(day.AcknowledgeNightsShown(202, 5));
        // An acknowledgement that does not reach what is owed changes nothing.
        Assert.False(day.AcknowledgeNightsShown(303, 4));
        Assert.Equal(5, Farmer(day, 303).NightsOwedFrom);
    }

    [Fact]
    public void TwoSkippedDaysAreOwedFromTheFirstAndCanBeShownOneAtATime()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Skip(202);
        day.Skip(303);
        day.AcknowledgeSavedNextDay(day.BeginAdvance(), 6, Roster);
        day.Join(101);
        day.Complete(101);
        day.Skip(202);
        day.Skip(303);
        day.AcknowledgeSavedNextDay(day.BeginAdvance(), 7, Roster);
        Assert.Equal(5, Farmer(day, 202).NightsOwedFrom);
        Assert.True(day.AcknowledgeNightsShown(202, 5));
        Assert.Equal(6, Farmer(day, 202).NightsOwedFrom);
        Assert.True(day.AcknowledgeNightsShown(202, 6));
        Assert.Null(Farmer(day, 202).NightsOwedFrom);
        // Charlie is shown both at once.
        Assert.True(day.AcknowledgeNightsShown(303, 6));
        Assert.Null(Farmer(day, 303).NightsOwedFrom);
    }

    [Fact]
    public void AFarmerStillConnectedThroughTheNightIsOwedNothingButKeepsWhatWasOwed()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Skip(202);
        day.Skip(303);
        day.AcknowledgeSavedNextDay(day.BeginAdvance(), 6, Roster);
        // Bob connects on day 6 without having been shown day 5's night, and stays through it.
        day.Join(202);
        day.Complete(202);
        day.Complete(101);
        day.Skip(303);
        day.AcknowledgeSavedNextDay(day.BeginAdvance(), 7, Roster);
        Assert.Null(Farmer(day, 101).NightsOwedFrom);
        Assert.Equal(5, Farmer(day, 202).NightsOwedFrom);
        Assert.Equal(5, Farmer(day, 303).NightsOwedFrom);
    }

    [Fact]
    public void NightsOwedAndParkedClocksSurviveTheJournal()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1410);
        day.Join(202);
        day.Complete(202);
        day.Disconnect(202);
        var json = JsonSerializer.Serialize(day.ExportJournal());
        Assert.Contains("\"TimeReached\":1410", json);
        var restored = DayCoordinator.Restore(JsonSerializer.Deserialize<DayJournal>(json)!);
        Assert.Equal(1410, Farmer(restored, 101).TimeReached);
        Assert.Equal(1, restored.ExportJournal().SchemaVersion);
    }

    [Fact]
    public void AJournalFromBeforeTheseMembersStillLoads()
    {
        // Exactly what 0.1.28 wrote: no TimeReached, Waiting or NightsOwedFrom anywhere.
        const string json = "{\"SchemaVersion\":1,\"SaveId\":\"farm-42\",\"AbsoluteDay\":5,\"Revision\":3,\"Mode\":0,\"Phase\":0,\"SessionNumber\":1,\"AdvanceToken\":null,"
            + "\"Farmers\":[{\"Id\":101,\"Name\":\"Alice\",\"Status\":0,\"AbsentDays\":0,\"SeenToday\":true,\"Away\":false},{\"Id\":202,\"Name\":\"Bob\",\"Status\":1,\"AbsentDays\":2,\"SeenToday\":false,\"Away\":false}],"
            + "\"CurrentSessionFarmerIds\":[101]}";
        var restored = DayCoordinator.Restore(JsonSerializer.Deserialize<DayJournal>(json)!);
        Assert.Null(Farmer(restored, 101).TimeReached);
        Assert.False(Farmer(restored, 101).Waiting);
        Assert.Null(Farmer(restored, 101).NightsOwedFrom);
        Assert.Equal(2, Farmer(restored, 202).AbsentDays);
        // Alice left her turn under 0.1.28, with no parked clock: back into it as it stands.
        Assert.Equal(JoinDisposition.ResumeSession, restored.Join(101).Disposition);
    }

    [Fact]
    public void AJournalWithAnImpossibleParkedClockOrAWaitingFinishedFarmerIsRejected()
    {
        var bad = new DayJournal { SaveId = "farm", AbsoluteDay = 5, Farmers = { new FarmerJournal(1, "Alice", FarmerStatus.Pending, TimeReached: 2700) } };
        Assert.Throws<DayCoordinationException>(() => DayCoordinator.Restore(bad));
        var waiting = new DayJournal { SaveId = "farm", AbsoluteDay = 5, Farmers = { new FarmerJournal(1, "Alice", FarmerStatus.Completed, Waiting: true) } };
        Assert.Throws<DayCoordinationException>(() => DayCoordinator.Restore(waiting));
    }
}

/// <summary>"Save my turn": the host holds every connected player, saves once all have answered, or gives up.</summary>
public sealed class HeldSaveTests
{
    private static readonly DateTime Start = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheSaveIsWrittenOnTheTickAfterTheLastAnswer()
    {
        var held = new HeldSave();
        Assert.True(held.Begin(202, new long[] { 202, 303 }, Start));
        Assert.True(held.Holding);
        Assert.Equal(202, held.Requester);
        Assert.Equal(HeldSaveStep.Wait, held.Tick(Start));
        Assert.False(held.Acknowledge(202));
        Assert.Equal(HeldSaveStep.Wait, held.Tick(Start.AddSeconds(1)));
        Assert.True(held.Acknowledge(303));
        Assert.True(held.AllHeld);
        Assert.True(held.IsHeld(202) && held.IsHeld(303));
        Assert.Equal(HeldSaveStep.Wait, held.Tick(Start.AddSeconds(1.1)));
        Assert.Equal(HeldSaveStep.Save, held.Tick(Start.AddSeconds(1.2)));
        held.End();
        Assert.False(held.Holding);
        Assert.Equal(HeldSaveStep.Nothing, held.Tick(Start.AddSeconds(2)));
    }

    [Fact]
    public void WithoutEveryAnswerInThreeSecondsNothingIsSaved()
    {
        var held = new HeldSave();
        held.Begin(202, new long[] { 202, 303 }, Start);
        held.Acknowledge(202);
        Assert.Equal(HeldSaveStep.Wait, held.Tick(Start.AddSeconds(2.9)));
        Assert.Equal(HeldSaveStep.GiveUp, held.Tick(Start.AddSeconds(3)));
        Assert.False(held.IsHeld(303));
    }

    [Fact]
    public void AnAnswerFromSomebodyNotAskedCountsForNothingAndOneHoldAtATime()
    {
        var held = new HeldSave();
        held.Begin(202, new long[] { 202 }, Start);
        Assert.False(held.Acknowledge(999));
        Assert.False(held.AllHeld);
        Assert.False(held.Begin(303, new long[] { 303 }, Start));
        Assert.Equal(202, held.Requester);
    }

    [Fact]
    public void APlayerWhoLeavesIsNoLongerWaitedFor()
    {
        var held = new HeldSave();
        held.Begin(202, new long[] { 202, 303 }, Start);
        held.Acknowledge(202);
        held.Forget(303);
        Assert.True(held.AllHeld);
    }

    [Fact]
    public void NobodyToAskMeansNoHold()
    {
        var held = new HeldSave();
        Assert.False(held.Begin(202, Array.Empty<long>(), Start));
        Assert.False(held.Holding);
    }

    [Theory]
    [InlineData(0, 1000, 360, false)]
    [InlineData(60, 419, 360, false)]
    [InlineData(60, 420, 360, true)]
    [InlineData(30, 400, 360, true)]
    public void ThePeriodicSaveIsDueEveryNGameMinutes(int every, int now, int last, bool due)
        => Assert.Equal(due, HeldSave.PeriodicDue(every, now, last));
}

/// <summary>What the turn tracker answers to a report: who is barred, and whether new farmers may join.</summary>
public sealed class TrackerOrdersTests
{
    [Fact]
    public void BarredIdsComeAsStringsAndNewFarmersAsABool()
    {
        var orders = TrackerOrders.Parse("{\"barred\":[\"-6154521813318541371\",\"2283117480133497730\"],\"new_farmers\":false}");
        Assert.NotNull(orders);
        Assert.Equal(new[] { -6154521813318541371L, 2283117480133497730L }, orders!.Barred);
        Assert.False(orders.NewFarmers);
    }

    [Fact]
    public void NumbersAndRepeatsAreTakenAndUnknownFieldsIgnored()
    {
        var orders = TrackerOrders.Parse("{\"barred\":[101, \"101\", \"x\"],\"new_farmers\":true,\"later\":{\"a\":1}}");
        Assert.Equal(new[] { 101L }, orders!.Barred);
        Assert.True(orders.NewFarmers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"barred\":[]}")]
    public void NoOrdersFromAnEmptyOrOddAnswer(string? body) => Assert.Null(TrackerOrders.Parse(body));

    [Fact]
    public void OnlyNewFarmersIsStillAnOrder()
    {
        var orders = TrackerOrders.Parse("{\"new_farmers\":false}");
        Assert.Empty(orders!.Barred);
        Assert.False(orders.NewFarmers);
    }
}

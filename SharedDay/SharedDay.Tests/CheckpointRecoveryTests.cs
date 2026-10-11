using System.Text.Json;
using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

public sealed class CheckpointRecoveryTests
{
    private const string FarmId = "RelayFarm_4242";
    private static readonly FarmerIdentity[] Roster = { new(101, "Alice"), new(202, "Bob"), new(303, "Charlie") };

    [Fact]
    public void CompletedFarmerRejoinsAfterReloadWithoutStartingOrReplayingTheirDay()
    {
        var day = Create();
        var clock = new CalendarClock(5);
        day.Join(101);
        clock.AdvanceToTimeOfDay(1200);
        day.Complete(101);
        day.Disconnect(101);

        var recovered = RoundTrip(day, clock, 5);
        Assert.Equal(JoinDisposition.Observer, recovered.Coordinator.Join(101).Disposition);
        Assert.Equal(1, recovered.Coordinator.Snapshot.SessionNumber);
        Assert.False(recovered.Coordinator.IsReadyToAdvance);
        Assert.Equal(JoinDisposition.StartSession, recovered.Coordinator.Join(202).Disposition);
        Assert.Equal(2, recovered.Coordinator.Snapshot.SessionNumber);
        Assert.Equal(0, recovered.Clock.AdvanceToTimeOfDay(600));
        Assert.Equal(0, recovered.Clock.AdvanceToTimeOfDay(1200));
        Assert.Equal(10, recovered.Clock.AdvanceToTimeOfDay(1210));
    }

    [Fact]
    public void InterruptedCohortResumesAndKeepsOfflineParticipantRequiredAcrossReload()
    {
        var day = Create();
        var clock = new CalendarClock(5);
        day.Join(101);
        day.Join(202);
        clock.AdvanceToTimeOfDay(2200);
        day.Complete(101);
        day.Disconnect(101);
        day.Disconnect(202);

        var recovered = RoundTrip(day, clock, 5);
        // Bob left unfinished and holds nobody (0.1.29): Charlie gets a turn of his own, at morning.
        Assert.Equal(JoinDisposition.StartSession, recovered.Coordinator.Join(303).Disposition);
        Assert.Equal(2, recovered.Coordinator.Snapshot.SessionNumber);
        recovered.Coordinator.Complete(303);
        Assert.False(recovered.Coordinator.IsReadyToAdvance);
        Assert.Throws<DayCoordinationException>(() => recovered.Coordinator.BeginAdvance());
        // Bob's disconnect carried no clock (a journal from before 0.1.29 has none): a turn of his own.
        Assert.Equal(JoinDisposition.StartSession, recovered.Coordinator.Join(202).Disposition);
        // The machine budget is the day's, whatever turn is played: those hours were granted once.
        Assert.Equal(0, recovered.Clock.AdvanceToTimeOfDay(2200));
        recovered.Coordinator.Complete(202);
        Assert.True(recovered.Coordinator.IsReadyToAdvance);
    }

    [Fact]
    public void TogetherFinishedReconnectsDoNotGrantSessionsButSatisfyPresenceBarrier()
    {
        var day = ReadyDay(PlayMode.Together);
        var clock = new CalendarClock(5);
        clock.AdvanceToTimeOfDay(2350);
        var recovered = RoundTrip(day, clock, 5);
        var session = recovered.Coordinator.Snapshot.SessionNumber;
        foreach (var farmer in Roster)
        {
            Assert.False(recovered.Coordinator.IsReadyToAdvance);
            Assert.Equal(JoinDisposition.Observer, recovered.Coordinator.Join(farmer.Id).Disposition);
            Assert.Equal(session, recovered.Coordinator.Snapshot.SessionNumber);
        }
        Assert.True(recovered.Coordinator.IsReadyToAdvance);
        Assert.Equal(1430, recovered.Clock.HighWaterMinute);
    }

    [Fact]
    public void SameDateAdvanceRecoveryRetainsBudgetAndStableToken()
    {
        var day = ReadyDay();
        var clock = new CalendarClock(5);
        clock.AdvanceToTimeOfDay(2550);
        var token = day.BeginAdvance();

        var recovered = RoundTrip(day, clock, 5);
        Assert.Equal(ReconciliationResult.RecoveredPendingAdvance, recovered.Reconciliation);
        Assert.Equal(5, recovered.Coordinator.Snapshot.AbsoluteDay);
        Assert.Equal(5, recovered.Clock.AbsoluteDay);
        Assert.Equal(1550, recovered.Clock.HighWaterMinute);
        Assert.Equal(token, recovered.Coordinator.BeginAdvance());
        Assert.Equal(0, recovered.Clock.AdvanceToTimeOfDay(1200));
    }

    [Fact]
    public void CrashAfterDurableNextDaySaveFinalizesCoordinatorAndBudgetTogether()
    {
        var day = ReadyDay();
        var clock = new CalendarClock(5);
        clock.AdvanceToTimeOfDay(2550);
        day.BeginAdvance();

        var recovered = RoundTrip(day, clock, 6);
        Assert.Equal(ReconciliationResult.FinalizedAdvance, recovered.Reconciliation);
        Assert.Equal(6, recovered.Coordinator.Snapshot.AbsoluteDay);
        Assert.Equal(6, recovered.Clock.AbsoluteDay);
        Assert.Equal(360, recovered.Clock.HighWaterMinute);
        Assert.All(recovered.Coordinator.Snapshot.Farmers, farmer => Assert.Equal(FarmerStatus.Pending, farmer.Status));
        Assert.Equal(JoinDisposition.StartSession, recovered.Coordinator.Join(101).Disposition);
        Assert.Equal(10, recovered.Clock.AdvanceToTimeOfDay(610));

        // Saving and loading that new day must retain the new budget, not reset again.
        var loadedAgain = RoundTrip(recovered.Coordinator, recovered.Clock, 6);
        Assert.Equal(ReconciliationResult.SameDay, loadedAgain.Reconciliation);
        Assert.Equal(370, loadedAgain.Clock.HighWaterMinute);
        Assert.Equal(0, loadedAgain.Clock.AdvanceToTimeOfDay(610));
        Assert.Equal(JoinDisposition.ResumeSession, loadedAgain.Coordinator.Join(101).Disposition);
    }

    [Fact]
    public void MissingBudgetCannotBeReconstructedFromRewoundDisplayTime()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Join(202); // Displayed clock is back at 06:00; the real high-water time is unknown.
        Assert.Throws<DayCoordinationException>(() => CheckpointRecovery.Restore(FarmId, 5, day.ExportJournal(), null, Roster));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public void StaleOrFutureBudgetCannotBeAttachedToOpenDay(int budgetDay)
    {
        var day = Create();
        Assert.Throws<DayCoordinationException>(() => CheckpointRecovery.Restore(FarmId, 5,
            day.ExportJournal(), new CalendarClockState(budgetDay, 1200), Roster));
    }

    [Fact]
    public void AlreadyAdvancedBudgetCannotBeAttachedToPendingOvernightJournal()
    {
        var day = ReadyDay();
        day.BeginAdvance();
        Assert.Throws<DayCoordinationException>(() => CheckpointRecovery.Restore(FarmId, 6,
            day.ExportJournal(), new CalendarClockState(6, 360), Roster));
    }

    [Fact]
    public void CrossFarmCheckpointIsRejectedEvenWhenDatesAndBudgetMatch()
    {
        var day = Create();
        Assert.Throws<DayCoordinationException>(() => CheckpointRecovery.Restore("AnotherFarm_99", 5,
            day.ExportJournal(), new CalendarClockState(5, 360), Roster));
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(false, 6)]
    [InlineData(true, 4)]
    [InlineData(true, 7)]
    public void MismatchedWorldDateDoesNotPartiallyRewriteInputCheckpoint(bool advancing, int savedDay)
    {
        var day = ReadyDay();
        if (advancing) day.BeginAdvance();
        var journal = day.ExportJournal();
        var clock = new CalendarClockState(5, 1500);
        var originalJson = JsonSerializer.Serialize(journal);
        Assert.Throws<DayCoordinationException>(() => CheckpointRecovery.Restore(FarmId, savedDay, journal, clock, Roster));
        Assert.Equal(originalJson, JsonSerializer.Serialize(journal));
        Assert.Equal(5, clock.AbsoluteDay);
        Assert.Equal(1500, clock.HighWaterMinute);
    }

    [Fact]
    public void InvalidNextRosterCannotPartiallyRecoverCheckpoint()
    {
        var day = ReadyDay();
        day.BeginAdvance();
        var journal = day.ExportJournal();
        var clock = new CalendarClockState(5, 1500);
        Assert.Throws<DayCoordinationException>(() => CheckpointRecovery.Restore(FarmId, 6, journal, clock,
            new[] { Roster[0], Roster[0] }));
        Assert.Equal(DayPhase.Advancing, journal.Phase);
        Assert.Equal(5, journal.AbsoluteDay);
        Assert.Equal(1500, clock.HighWaterMinute);
    }

    [Fact]
    public void SkippedPlayersReturnAsRequiredOnNextDayAfterCrashRecovery()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Skip(202);
        day.Skip(303);
        day.BeginAdvance();
        var recovered = RoundTrip(day, new CalendarClock(5), 6);
        Assert.All(recovered.Coordinator.Snapshot.Farmers, farmer => Assert.Equal(FarmerStatus.Pending, farmer.Status));
        recovered.Coordinator.Join(101);
        recovered.Coordinator.Complete(101);
        Assert.False(recovered.Coordinator.IsReadyToAdvance);
    }

    private static DayCoordinator Create(PlayMode mode = PlayMode.Relay) => DayCoordinator.Create(FarmId, 5, Roster, mode);

    private static DayCoordinator ReadyDay(PlayMode mode = PlayMode.Relay)
    {
        var day = Create(mode);
        foreach (var farmer in Roster) day.Join(farmer.Id);
        foreach (var farmer in Roster) day.Complete(farmer.Id);
        return day;
    }

    private static RecoveredCheckpoint RoundTrip(DayCoordinator day, CalendarClock clock, int savedDay)
    {
        var journal = JsonSerializer.Deserialize<DayJournal>(JsonSerializer.Serialize(day.ExportJournal()))!;
        var budget = JsonSerializer.Deserialize<CalendarClockState>(JsonSerializer.Serialize(clock.ExportState()))!;
        return CheckpointRecovery.Restore(FarmId, savedDay, journal, budget, Roster);
    }
}

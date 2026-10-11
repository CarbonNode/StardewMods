using System.Text.Json;
using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

public sealed class DayCoordinatorTests
{
    private static readonly FarmerIdentity[] Roster = { new(101, "Alice"), new(202, "Bob"), new(303, "Charlie") };
    private static DayCoordinator Create(PlayMode mode = PlayMode.Relay) => DayCoordinator.Create("farm-42", 5, Roster, mode);
    private static FarmerSnapshot Farmer(DayCoordinator day, long id) => day.Snapshot.Farmers.Single(f => f.Id == id);

    [Fact]
    public void OfflineRequiredPlayersBlockOvernight()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Disconnect(101);
        Assert.False(day.IsReadyToAdvance);
        Assert.Throws<DayCoordinationException>(() => day.BeginAdvance());
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 202).Status);
        Assert.Equal(5, day.Snapshot.AbsoluteDay);
    }

    [Fact]
    public void MidDayArrivalSharesCurrentSession()
    {
        var day = Create();
        Assert.Equal(JoinDisposition.StartSession, day.Join(101).Disposition);
        Assert.Equal(JoinDisposition.JoinCurrentSession, day.Join(202).Disposition);
        Assert.Equal(1, day.Snapshot.SessionNumber);
        Assert.Equal(new long[] { 101, 202 }, day.Snapshot.SessionParticipants);
    }

    [Fact]
    public void NextPlayersGetNewSessionAfterPriorParticipantsFinish()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        var bob = day.Join(202);
        Assert.True(bob.StartsNewSession);
        Assert.Equal(2, bob.SessionNumber);
        Assert.Equal(new long[] { 202 }, day.Snapshot.SessionParticipants);
        Assert.Equal(FarmerStatus.Completed, Farmer(day, 101).Status);
        Assert.Equal(5, day.Snapshot.AbsoluteDay);
    }

    [Fact]
    public void IncompleteDisconnectDoesNotGrantFreshMorning()
    {
        var day = Create();
        day.Join(101);
        day.Disconnect(101);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 101).Status);
        Assert.Equal(JoinDisposition.ResumeSession, day.Join(101).Disposition);
        Assert.Equal(1, day.Snapshot.SessionNumber);
        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void DifferentPlayerGetsAMorningOfTheirOwnWhenUnfinishedParticipantDisconnected()
    {
        // Until 0.1.28 Bob resumed Alice's clock here. An unfinished farmer who left no longer
        // pins the turn: Alice keeps her own clock (TimeReached) and Bob starts a turn of his own.
        var day = Create();
        day.Join(101);
        day.Disconnect(101, 1130);
        var bob = day.Join(202);
        Assert.Equal(JoinDisposition.StartSession, bob.Disposition);
        Assert.Equal(new long[] { 202 }, day.Snapshot.SessionParticipants);
        Assert.Equal(2, day.Snapshot.SessionNumber);
        Assert.Equal(1130, Farmer(day, 101).TimeReached);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 101).Status);
    }

    [Fact]
    public void OneFinishingPlayerDoesNotRestartOthersActiveSession()
    {
        var day = Create();
        day.Join(101);
        day.Join(202);
        day.Complete(101);
        Assert.Equal(JoinDisposition.JoinCurrentSession, day.Join(303).Disposition);
        Assert.Equal(1, day.Snapshot.SessionNumber);
    }

    [Fact]
    public void DuplicateJoinCompleteAndDisconnectAreIdempotent()
    {
        var day = Create();
        day.Join(101);
        var joinedRevision = day.Snapshot.Revision;
        Assert.Equal(JoinDisposition.AlreadyPresent, day.Join(101).Disposition);
        Assert.Equal(joinedRevision, day.Snapshot.Revision);
        Assert.True(day.Complete(101));
        var completedRevision = day.Snapshot.Revision;
        Assert.False(day.Complete(101));
        Assert.Equal(completedRevision, day.Snapshot.Revision);
        Assert.True(day.Disconnect(101));
        var disconnectedRevision = day.Snapshot.Revision;
        Assert.False(day.Disconnect(101));
        Assert.Equal(disconnectedRevision, day.Snapshot.Revision);
    }

    [Fact]
    public void FinishedAndSkippedPlayersRejoinAsObservers()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Disconnect(101);
        day.Skip(202);
        Assert.Equal(JoinDisposition.Observer, day.Join(101).Disposition);
        Assert.Equal(JoinDisposition.Observer, day.Join(202).Disposition);
        Assert.Equal(1, day.Snapshot.SessionNumber);
        Assert.False(Farmer(day, 202).IsInCurrentSession);
    }

    [Fact]
    public void OfflinePlayerCannotCompleteWithoutParticipating()
    {
        var day = Create();
        Assert.Throws<DayCoordinationException>(() => day.Complete(101));
        day.Join(101);
        day.Disconnect(101);
        Assert.Throws<DayCoordinationException>(() => day.Complete(101));
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 101).Status);
    }

    [Fact]
    public void UnknownFarmerActionsFailWithoutChangingState()
    {
        var day = Create();
        Assert.Throws<DayCoordinationException>(() => day.Join(999));
        Assert.Throws<DayCoordinationException>(() => day.Disconnect(999));
        Assert.Throws<DayCoordinationException>(() => day.Complete(999));
        Assert.Throws<DayCoordinationException>(() => day.Skip(999));
        Assert.Equal(0, day.Snapshot.Revision);
        Assert.Equal(3, day.Snapshot.Farmers.Count);
    }

    [Fact]
    public void SkippingAbsentPlayersUnblocksRelayAndIsIdempotent()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Disconnect(101);
        Assert.True(day.Skip(202));
        Assert.True(day.Skip(303));
        var revision = day.Snapshot.Revision;
        Assert.False(day.Skip(303));
        Assert.Equal(revision, day.Snapshot.Revision);
        Assert.True(day.IsReadyToAdvance);
    }

    [Fact]
    public void TogetherRequiresCompletionAndNonSkippedRosterOnline()
    {
        var day = Create(PlayMode.Together);
        foreach (var farmer in Roster) day.Join(farmer.Id);
        Assert.False(day.IsReadyToAdvance);
        foreach (var farmer in Roster) day.Complete(farmer.Id);
        Assert.True(day.IsReadyToAdvance);
        day.Disconnect(202);
        Assert.False(day.IsReadyToAdvance);
        Assert.Throws<DayCoordinationException>(() => day.BeginAdvance());
        Assert.Equal(JoinDisposition.Observer, day.Join(202).Disposition);
        Assert.True(day.IsReadyToAdvance);
        Assert.Equal(5, day.Snapshot.AbsoluteDay);
    }

    [Fact]
    public void SkippedPlayersNeedNotBeOnlineInTogetherMode()
    {
        var day = Create(PlayMode.Together);
        day.Join(101);
        day.Complete(101);
        day.Skip(202);
        day.Skip(303);
        Assert.True(day.IsReadyToAdvance);
    }

    [Fact]
    public void ChangingModeChangesGateButNeverDate()
    {
        var day = ReadyDay();
        day.Disconnect(101);
        Assert.True(day.IsReadyToAdvance);
        Assert.True(day.SetMode(PlayMode.Together));
        Assert.False(day.IsReadyToAdvance);
        Assert.Equal(5, day.Snapshot.AbsoluteDay);
        Assert.True(day.SetMode(PlayMode.Relay));
        Assert.True(day.IsReadyToAdvance);
        Assert.False(day.SetMode(PlayMode.Relay));
    }

    [Fact]
    public void BeginAdvanceIsStableAndDoesNotCommitNextDate()
    {
        var day = ReadyDay();
        var token = day.BeginAdvance();
        var revision = day.Snapshot.Revision;
        Assert.Equal(token, day.BeginAdvance());
        Assert.Equal(revision, day.Snapshot.Revision);
        Assert.Equal(DayPhase.Advancing, day.Snapshot.Phase);
        Assert.Equal(5, day.Snapshot.AbsoluteDay);
        Assert.Throws<DayCoordinationException>(() => day.SetMode(PlayMode.Together));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void OnlyExactlyNextSavedDayCanAcknowledge(int savedDay)
    {
        var day = ReadyDay();
        var token = day.BeginAdvance();
        Assert.Throws<DayCoordinationException>(() => day.AcknowledgeSavedNextDay(token, savedDay, Roster));
        Assert.Equal(5, day.Snapshot.AbsoluteDay);
        Assert.Equal(DayPhase.Advancing, day.Snapshot.Phase);
    }

    [Fact]
    public void WrongTokenAndDuplicateAcknowledgementCannotAdvanceTwice()
    {
        var day = ReadyDay();
        var token = day.BeginAdvance();
        Assert.Throws<DayCoordinationException>(() => day.AcknowledgeSavedNextDay(Guid.NewGuid().ToString("N"), 6, Roster));
        day.AcknowledgeSavedNextDay(token, 6, Roster);
        Assert.Throws<DayCoordinationException>(() => day.AcknowledgeSavedNextDay(token, 7, Roster));
        Assert.Equal(6, day.Snapshot.AbsoluteDay);
        Assert.Equal(DayPhase.Open, day.Snapshot.Phase);
        Assert.Null(day.Snapshot.AdvanceToken);
        Assert.All(day.Snapshot.Farmers, farmer => Assert.Equal(FarmerStatus.Pending, farmer.Status));
        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void ConnectedPlayersParticipateInActualNextMorningWithoutRejoining()
    {
        var day = ReadyDay();
        var token = day.BeginAdvance();
        day.AcknowledgeSavedNextDay(token, 6, Roster);
        Assert.Equal(1, day.Snapshot.SessionNumber);
        Assert.All(day.Snapshot.Farmers, farmer => Assert.True(farmer.IsInCurrentSession));
        Assert.True(day.Complete(101));
    }

    [Fact]
    public void RosterChangesOnlyAtActualNextDayCommit()
    {
        var day = ReadyDay();
        var token = day.BeginAdvance();
        var nextRoster = new[] { Roster[0], new FarmerIdentity(404, "Dana") };
        day.AcknowledgeSavedNextDay(token, 6, nextRoster);
        Assert.Equal(new long[] { 101, 404 }, day.Snapshot.Farmers.Select(f => f.Id));
        Assert.Throws<DayCoordinationException>(() => day.Join(202));
        Assert.Equal(JoinDisposition.JoinCurrentSession, day.Join(404).Disposition);
    }

    [Fact]
    public void InvalidNextRosterLeavesPendingTransitionUntouched()
    {
        var day = ReadyDay();
        var token = day.BeginAdvance();
        var revision = day.Snapshot.Revision;
        Assert.Throws<DayCoordinationException>(() => day.AcknowledgeSavedNextDay(token, 6, new[] { new FarmerIdentity(101, " ") }));
        Assert.Equal(5, day.Snapshot.AbsoluteDay);
        Assert.Equal(revision, day.Snapshot.Revision);
        Assert.Equal(token, day.Snapshot.AdvanceToken);
    }

    [Fact]
    public void EmptyRosterWaitsForTheFirstFarmer()
    {
        var day = DayCoordinator.Create("farm-42", 5, Array.Empty<FarmerIdentity>());
        Assert.False(day.IsReadyToAdvance);
        Assert.Throws<DayCoordinationException>(() => day.BeginAdvance());
        Assert.True(day.AddFarmer(new FarmerIdentity(404, "Dana")));
        Assert.Equal(JoinDisposition.StartSession, day.Join(404).Disposition);
        Assert.False(day.IsReadyToAdvance);
        day.Complete(404);
        Assert.True(day.IsReadyToAdvance);
    }

    [Fact]
    public void NewFarmerJoinsTheCurrentDayAndMustFinishIt()
    {
        var day = Create();
        day.Join(101);
        Assert.True(day.AddFarmer(new FarmerIdentity(404, "Dana")));
        Assert.False(day.AddFarmer(new FarmerIdentity(404, "Dana")));
        Assert.Equal(JoinDisposition.JoinCurrentSession, day.Join(404).Disposition);
        day.Complete(101);
        day.Skip(202);
        day.Skip(303);
        Assert.False(day.IsReadyToAdvance);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 404).Status);
        var restored = DayCoordinator.Restore(day.ExportJournal());
        Assert.Equal("Dana", restored.Snapshot.Farmers.Single(f => f.Id == 404).Name);
        day.Complete(404);
        Assert.True(day.IsReadyToAdvance);
    }

    [Fact]
    public void NewFarmerNeedsANameAndAnOpenDay()
    {
        var day = ReadyDay();
        Assert.Throws<DayCoordinationException>(() => day.AddFarmer(new FarmerIdentity(404, "")));
        day.BeginAdvance();
        Assert.Throws<DayCoordinationException>(() => day.AddFarmer(new FarmerIdentity(404, "Dana")));
    }

    [Fact]
    public void JournalRoundTripRetainsProgressButClearsOnlinePresence()
    {
        var day = Create();
        day.Join(101);
        day.Join(202);
        day.Complete(101);
        var json = JsonSerializer.Serialize(day.ExportJournal());
        var restored = DayCoordinator.Restore(JsonSerializer.Deserialize<DayJournal>(json)!);
        Assert.All(restored.Snapshot.Farmers, farmer => Assert.False(farmer.IsOnline));
        Assert.Equal(FarmerStatus.Completed, Farmer(restored, 101).Status);
        Assert.Equal(FarmerStatus.Pending, Farmer(restored, 202).Status);
        Assert.Equal(ReconciliationResult.SameDay, restored.ReconcileSavedDay(5, Roster));
        Assert.Equal(JoinDisposition.ResumeSession, restored.Join(202).Disposition);
        Assert.Equal(1, restored.Snapshot.SessionNumber);
    }

    [Fact]
    public void RestartBeforeOvernightSaveReopensSameDayWithSameTransitionToken()
    {
        var day = ReadyDay();
        var token = day.BeginAdvance();
        var restarted = DayCoordinator.Restore(day.ExportJournal());
        Assert.Equal(ReconciliationResult.RecoveredPendingAdvance, restarted.ReconcileSavedDay(5, Roster));
        Assert.Equal(5, restarted.Snapshot.AbsoluteDay);
        Assert.Equal(DayPhase.Open, restarted.Snapshot.Phase);
        Assert.Equal(token, restarted.BeginAdvance());
    }

    [Fact]
    public void RestartAfterOvernightSaveFinalizesExactlyOneDay()
    {
        var day = ReadyDay();
        day.BeginAdvance();
        var restarted = DayCoordinator.Restore(day.ExportJournal());
        Assert.Equal(ReconciliationResult.FinalizedAdvance, restarted.ReconcileSavedDay(6, Roster));
        Assert.Equal(6, restarted.Snapshot.AbsoluteDay);
        Assert.Equal(DayPhase.Open, restarted.Snapshot.Phase);
        Assert.All(restarted.Snapshot.Farmers, farmer => Assert.Equal(FarmerStatus.Pending, farmer.Status));
        Assert.Equal(ReconciliationResult.SameDay, restarted.ReconcileSavedDay(6, Roster));
        Assert.Throws<DayCoordinationException>(() => restarted.ReconcileSavedDay(7, Roster));
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(false, 6)]
    [InlineData(true, 4)]
    [InlineData(true, 7)]
    public void InconsistentSaveDateFailsClosed(bool advancing, int savedDay)
    {
        var day = ReadyDay();
        if (advancing) day.BeginAdvance();
        var restarted = DayCoordinator.Restore(day.ExportJournal());
        var revision = restarted.Snapshot.Revision;
        Assert.Throws<DayCoordinationException>(() => restarted.ReconcileSavedDay(savedDay, Roster));
        Assert.Equal(5, restarted.Snapshot.AbsoluteDay);
        Assert.Equal(revision, restarted.Snapshot.Revision);
    }

    [Fact]
    public void TogetherRestartRequiresPlayersToReconnectBeforeRetryingAdvance()
    {
        var day = ReadyDay(PlayMode.Together);
        day.BeginAdvance();
        var restarted = DayCoordinator.Restore(day.ExportJournal());
        restarted.ReconcileSavedDay(5, Roster);
        Assert.False(restarted.IsReadyToAdvance);
        Assert.Throws<DayCoordinationException>(() => restarted.BeginAdvance());
        foreach (var farmer in Roster) restarted.Join(farmer.Id);
        Assert.True(restarted.IsReadyToAdvance);
    }

    [Fact]
    public void JournalAndSnapshotCannotBeUsedToMutateCoordinator()
    {
        var day = Create();
        var journal = day.ExportJournal();
        journal.Farmers.Clear();
        journal.CurrentSessionFarmerIds.Add(999);
        Assert.Equal(3, day.Snapshot.Farmers.Count);
        Assert.Empty(day.Snapshot.SessionParticipants);

        var original = day.ExportJournal();
        var restored = DayCoordinator.Restore(original);
        original.Farmers.Clear();
        Assert.Equal(3, restored.Snapshot.Farmers.Count);
        var snapshot = day.Snapshot;
        day.Join(101);
        Assert.False(snapshot.Farmers[0].IsOnline);
    }

    [Fact]
    public void InvalidJournalVersionTokenRosterAndSessionAreRejected()
    {
        var journal = Create().ExportJournal();
        Assert.Throws<DayCoordinationException>(() => DayCoordinator.Restore(journal with { SchemaVersion = 2 }));
        Assert.Throws<DayCoordinationException>(() => DayCoordinator.Restore(journal with { AdvanceToken = "not-a-token" }));
        Assert.Throws<DayCoordinationException>(() => DayCoordinator.Restore(journal with { Phase = DayPhase.Advancing }));
        Assert.Throws<DayCoordinationException>(() => DayCoordinator.Restore(journal with { Farmers = new() { journal.Farmers[0], journal.Farmers[0] } }));
        Assert.Throws<DayCoordinationException>(() => DayCoordinator.Restore(journal with { CurrentSessionFarmerIds = new() { 999 } }));
    }

    private static DayCoordinator ReadyDay(PlayMode mode = PlayMode.Relay)
    {
        var day = Create(mode);
        foreach (var farmer in Roster)
        {
            day.Join(farmer.Id);
            day.Complete(farmer.Id);
        }
        return day;
    }

    [Fact]
    public void AnUndoneFinishResumesTheSameSessionAndEarnsNoFreshMorning()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        Assert.Equal(FarmerStatus.Completed, day.Reinstate(101));
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 101).Status);
        Assert.False(Farmer(day, 101).IsOnline);
        var back = day.Join(101);
        Assert.Equal(JoinDisposition.ResumeSession, back.Disposition);
        Assert.False(back.StartsNewSession);
        Assert.Equal(1, back.SessionNumber);
        // They can finish again, and the day is held on them until they do.
        Assert.False(day.IsReadyToAdvance);
        Assert.True(day.Complete(101));
    }

    [Fact]
    public void AReopenedFarmerJoinsWhoeverIsPlayingNowOrStartsASessionWhenNobodyIs()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Join(202); // Bob's later session is under way
        Assert.Equal(2, day.Snapshot.SessionNumber);
        Assert.Equal(FarmerStatus.Completed, day.Reinstate(101));
        Assert.Equal(JoinDisposition.JoinCurrentSession, day.Join(101).Disposition);
        Assert.Equal(2, day.Snapshot.SessionNumber);
        // Having joined Bob's session, a second undo puts Alice back in that same session, not a new one.
        day.Complete(101);
        day.Complete(202);
        day.Reinstate(101);
        Assert.Equal(JoinDisposition.ResumeSession, day.Join(101).Disposition);
        Assert.Equal(2, day.Snapshot.SessionNumber);

        // A later session Alice was never part of has come and gone: her reopened day is a session of its own.
        var later = Create();
        later.Join(101);
        later.Complete(101);
        later.Join(202);
        later.Complete(202);
        later.Reinstate(101);
        var fresh = later.Join(101);
        Assert.True(fresh.StartsNewSession);
        Assert.Equal(3, fresh.SessionNumber);
    }

    [Fact]
    public void ASkippedFarmerCanBeBroughtBackAndHoldsTheDayAgain()
    {
        var day = Create();
        day.Join(101);
        day.Complete(101);
        day.Skip(202);
        day.Skip(303);
        Assert.True(day.IsReadyToAdvance);
        Assert.Equal(FarmerStatus.Skipped, day.Reinstate(303));
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 303).Status);
        Assert.False(day.IsReadyToAdvance);
        // Someone still to play is left as they are; a stranger is refused; so is anyone once the night has begun.
        Assert.Null(day.Reinstate(303));
        Assert.Throws<DayCoordinationException>(() => day.Reinstate(999));
        day.Skip(303);
        day.BeginAdvance();
        Assert.Throws<DayCoordinationException>(() => day.Reinstate(101));
        Assert.Equal(FarmerStatus.Completed, Farmer(day, 101).Status);
    }

    // One whole day, played by the given farmers, ending in a saved next day.
    private static void PlayDay(DayCoordinator day, params long[] players)
    {
        foreach (long id in players) { day.Join(id); day.Complete(id); day.Disconnect(id); }
        foreach (var farmer in day.Snapshot.Farmers.Where(f => f.Status == FarmerStatus.Pending).ToArray()) day.Skip(farmer.Id);
        string token = day.BeginAdvance();
        day.AcknowledgeSavedNextDay(token, day.Snapshot.AbsoluteDay + 1, Roster);
    }

    [Fact]
    public void AFarmerAwayPastTheLimitSitsOutWithoutAVoteAndIsBackTheMomentTheyConnect()
    {
        var day = Create();
        day.AbsenceLimit = 3;
        PlayDay(day, 101, 202); // Charlie misses day one...
        Assert.Equal(1, Farmer(day, 303).AbsentDays);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 303).Status);
        PlayDay(day, 101, 202);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 303).Status); // ...and two: still waited for
        PlayDay(day, 101, 202);
        // Three in a row: from this morning nobody has to vote him out.
        Assert.Equal(3, Farmer(day, 303).AbsentDays);
        Assert.True(Farmer(day, 303).Away);
        Assert.Equal(FarmerStatus.Skipped, Farmer(day, 303).Status);
        day.Join(101); day.Complete(101);
        day.Join(202); day.Complete(202);
        Assert.True(day.IsReadyToAdvance);

        // He turns up before the day ends: back in it, and the day waits for him again.
        var back = day.Join(303);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 303).Status);
        Assert.False(Farmer(day, 303).Away);
        Assert.NotEqual(JoinDisposition.Observer, back.Disposition);
        Assert.False(day.IsReadyToAdvance);
        day.Complete(303);
        string token = day.BeginAdvance();
        day.AcknowledgeSavedNextDay(token, day.Snapshot.AbsoluteDay + 1, Roster);
        Assert.Equal(0, Farmer(day, 303).AbsentDays);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 303).Status);
    }

    [Fact]
    public void AFarmWhoseWholeGroupIsAwayDoesNotRunThroughItsCalendarAlone()
    {
        var day = Create();
        day.AbsenceLimit = 1;
        // Nobody connects on the first day; the host lets all three off it.
        PlayDay(day);
        Assert.All(day.Snapshot.Farmers, f => Assert.True(f.Away));
        Assert.All(day.Snapshot.Farmers, f => Assert.Equal(FarmerStatus.Skipped, f.Status));
        // Everyone sits out, and still the day does not end by itself.
        Assert.False(day.IsReadyToAdvance);
        Assert.Throws<DayCoordinationException>(() => day.BeginAdvance());
        // One of them comes back and plays: that day ends, without the other two.
        day.Join(101);
        Assert.False(day.IsReadyToAdvance);
        day.Complete(101);
        Assert.True(day.IsReadyToAdvance);
    }

    [Fact]
    public void WithNoLimitAnAbsentFarmerIsWaitedForAsBefore()
    {
        var day = Create();
        for (int i = 0; i < 5; i++) PlayDay(day, 101, 202);
        Assert.Equal(5, Farmer(day, 303).AbsentDays);
        Assert.False(Farmer(day, 303).Away);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 303).Status);
    }

    [Fact]
    public void AbsenceSurvivesARestartAndADeliberateSkipIsNotAnAbsence()
    {
        var day = Create();
        day.AbsenceLimit = 2;
        PlayDay(day, 101, 202);
        PlayDay(day, 101, 202);
        Assert.True(Farmer(day, 303).Away);
        // The farm is put to sleep and woken: the journal carries it.
        var journal = System.Text.Json.JsonSerializer.Deserialize<DayJournal>(System.Text.Json.JsonSerializer.Serialize(day.ExportJournal()))!;
        var woken = DayCoordinator.Restore(journal);
        woken.AbsenceLimit = 2;
        Assert.True(Farmer(woken, 303).Away);
        Assert.Equal(2, Farmer(woken, 303).AbsentDays);
        // A journal written before the rule existed reads as nobody away.
        var old = DayCoordinator.Restore(System.Text.Json.JsonSerializer.Deserialize<DayJournal>(
            "{\"SchemaVersion\":1,\"SaveId\":\"farm-42\",\"AbsoluteDay\":5,\"Farmers\":[{\"Id\":101,\"Name\":\"Alice\",\"Status\":0}]}")!);
        Assert.False(Farmer(old, 101).Away);
        Assert.Equal(0, Farmer(old, 101).AbsentDays);

        // Bob is here today, is voted off the day anyway, and comes back online: he stays off it
        // until someone brings him back, because that was a decision and not an absence.
        woken.Join(202);
        woken.Disconnect(202);
        woken.Skip(202);
        Assert.False(Farmer(woken, 202).Away);
        Assert.Equal(JoinDisposition.Observer, woken.Join(202).Disposition);
        Assert.Equal(FarmerStatus.Skipped, Farmer(woken, 202).Status);
    }

    [Fact]
    public void AFarmerPlayingAloneWhoStepsAwayDoesNotComeBackToTheNextMorning()
    {
        // Squeet Farm, 2026-10-07 00:03: its one farmer stopped for ten minutes, was "skipped",
        // and the farm ended Spring 2 by itself.
        var day = DayCoordinator.Create("farm-42", 5, new[] { new FarmerIdentity(101, "Alice") });
        day.Join(101);
        Assert.True(day.StepAway(101));
        Assert.False(Farmer(day, 101).IsOnline);
        Assert.True(Farmer(day, 101).Away);
        Assert.False(day.IsReadyToAdvance);
        Assert.Throws<DayCoordinationException>(() => day.BeginAdvance());
        // Back at the keyboard: back in the same session, at the time it had reached.
        var back = day.Join(101);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 101).Status);
        Assert.False(back.StartsNewSession);
    }

    [Fact]
    public void AFarmerWhoStepsAwayStopsHoldingTheOthers()
    {
        var day = Create();
        day.Join(101);
        day.Join(202); day.Complete(202);
        day.Skip(303);
        Assert.False(day.IsReadyToAdvance); // Alice is still playing
        day.StepAway(101);
        Assert.True(day.IsReadyToAdvance);  // Bob played; nobody waits on Alice's empty seat
        Assert.False(day.StepAway(202));    // a finished turn is finished
    }
}

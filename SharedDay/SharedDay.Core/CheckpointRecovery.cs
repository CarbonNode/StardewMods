namespace SharedDay.Core;

/// <summary>A fully validated pair. Callers must publish both references together.</summary>
public sealed record RecoveredCheckpoint(
    DayCoordinator Coordinator,
    CalendarClock Clock,
    ReconciliationResult Reconciliation);

/// <summary>
/// Restores the coordinator and machine budget as one checkpoint. Never reconstruct a
/// missing machine budget from the displayed clock: that clock may have been rewound
/// for a later player's session while machines already consumed the earlier hours.
/// </summary>
public static class CheckpointRecovery
{
    public static RecoveredCheckpoint Restore(
        string expectedSaveId,
        int actualSavedDay,
        DayJournal journal,
        CalendarClockState? clock,
        IEnumerable<FarmerIdentity> nextRoster,
        int absenceLimit = 0)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (string.IsNullOrWhiteSpace(expectedSaveId) || !StringComparer.Ordinal.Equals(expectedSaveId, journal.SaveId))
            throw new DayCoordinationException("The SharedDay checkpoint belongs to a different farm.");
        if (clock is null)
            throw new DayCoordinationException("The SharedDay checkpoint is missing its machine-time budget; restore a complete matching checkpoint.");
        if (clock.AbsoluteDay != journal.AbsoluteDay)
            throw new DayCoordinationException("The day journal and machine-time budget belong to different calendar days.");

        // All work happens on new instances. A rejected save, roster, or budget cannot
        // partially mutate an existing live coordinator or either input snapshot.
        var restoredCoordinator = DayCoordinator.Restore(journal);
        restoredCoordinator.AbsenceLimit = absenceLimit;
        var restoredClock = CalendarClock.Restore(clock);
        var reconciliation = restoredCoordinator.ReconcileSavedDay(actualSavedDay, nextRoster);
        if (reconciliation == ReconciliationResult.FinalizedAdvance)
            restoredClock.ConfirmNextDay(actualSavedDay);

        return new(restoredCoordinator, restoredClock, reconciliation);
    }
}

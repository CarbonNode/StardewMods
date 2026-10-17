namespace SharedDay.Core;

public enum PlayMode { Relay, Together }
public enum DayPhase { Open, Advancing }
public enum FarmerStatus { Pending, Completed, Skipped }

/// <summary>
/// Where a farmer who connects is put. <see cref="ResumeOwnClock"/> (0.1.29): they left a turn
/// unfinished at a parked clock and nobody else is playing, so the clock goes back to where they
/// left it. <see cref="ChooseJoinOrWait"/> (0.1.29): they have such a parked clock but somebody
/// else is playing now, so they are asked whether to join that turn at its clock or to wait for
/// it to end and then carry on at their own; until they answer they are on the farm but not in
/// any turn.
/// </summary>
public enum JoinDisposition { StartSession, ResumeSession, JoinCurrentSession, Observer, AlreadyPresent, ResumeOwnClock, ChooseJoinOrWait }
public enum ReconciliationResult { SameDay, RecoveredPendingAdvance, FinalizedAdvance }

public sealed record FarmerIdentity(long Id, string Name);

/// <summary>
/// A member of the farm's ModRelay group (0.1.37), as the turn tracker reports it with every
/// answer: the account id, the name the launcher shows, and the farmer they said they play
/// (null when they have not picked one). A member nobody on the farm can be matched to stands
/// on the roster as a placeholder, "Pat (no farmer yet)", until they make a farmer or are
/// skipped (see <see cref="DayCoordinator.SetGroup"/>).
/// </summary>
/// <param name="Host">The account that set up the group's host PC (0.1.39): a dedicated host, not a player, unless the host's own farmer takes turns.</param>
public sealed record GroupMember(string AccountId, string Name, long? FarmerId = null, bool Host = false)
{
    /// <summary>
    /// The group with the farm's own ownership records filled in (0.1.37): a member who picked no
    /// farmer in the launcher, but owns one here (the account whose launcher made it), is matched
    /// to that farmer. A pick the member made stands.
    /// </summary>
    public static List<GroupMember> WithOwnedFarmers(IEnumerable<GroupMember> members, IEnumerable<(long FarmerId, string OwnerId)> owned)
    {
        var byOwner = new Dictionary<string, long>();
        foreach (var (farmerId, ownerId) in owned) byOwner.TryAdd(ownerId, farmerId);
        return members.Select(m => m.FarmerId is null && byOwner.TryGetValue(m.AccountId, out long mine) ? m with { FarmerId = mine } : m).ToList();
    }
}

/// <param name="TimeReached">The clock, HHMM, at which this unfinished farmer last left their turn; null when they have no parked turn.</param>
/// <param name="Waiting">They chose to wait for the turn in progress to end before carrying on at <paramref name="TimeReached"/>.</param>
/// <param name="NightsOwedFrom">The first date whose night ran while they were away and has not been shown to them yet; null when nothing is owed.</param>
/// <param name="MemberId">Set on a placeholder (0.1.37): the ModRelay account of the group member who has no farmer on the farm yet. <paramref name="Name"/> then reads "Pat (no farmer yet)" and <paramref name="MemberName"/> is "Pat".</param>
/// <param name="MemberName">The group member's own name, for a placeholder; null for a farmer.</param>
public sealed record FarmerSnapshot(long Id, string Name, FarmerStatus Status, bool IsOnline, bool IsInCurrentSession, int AbsentDays = 0, bool Away = false,
    int? TimeReached = null, bool Waiting = false, int? NightsOwedFrom = null, string? MemberId = null, string? MemberName = null)
{
    /// <summary>Playing right now: connected, still to play, and in the turn in progress (not waiting for it to end).</summary>
    public bool IsActive => IsOnline && Status == FarmerStatus.Pending && IsInCurrentSession && !Waiting;

    /// <summary>A group member with no farmer on the farm yet (0.1.37): nobody can play this entry, but it is waited for, or skipped, like a farmer.</summary>
    public bool IsPlaceholder => MemberId is not null;
}

public sealed record DaySnapshot(
    string SaveId,
    int AbsoluteDay,
    long Revision,
    PlayMode Mode,
    DayPhase Phase,
    int SessionNumber,
    string? AdvanceToken,
    bool IsReadyToAdvance,
    IReadOnlyList<FarmerSnapshot> Farmers)
{
    public IReadOnlyList<long> SessionParticipants => Array.AsReadOnly(Farmers.Where(f => f.IsInCurrentSession).Select(f => f.Id).ToArray());
}

public sealed record JoinResult(JoinDisposition Disposition, int SessionNumber)
{
    /// <summary>Only a new relay session asks the adapter to reset its clock to morning.</summary>
    public bool StartsNewSession => Disposition == JoinDisposition.StartSession;

    /// <summary>The adapter puts the clock back to the farmer's own parked time (their journal's TimeReached).</summary>
    public bool ResumesOwnClock => Disposition == JoinDisposition.ResumeOwnClock;
}

/// <summary>When a farmer given a fresh day (DayCoordinator.RestartFarmer) gets their 6:00 AM.</summary>
public enum FreshDayStart
{
    /// <summary>They are on the farm and nobody else is playing: their morning starts now (<see cref="FreshDayResult.Join"/>).</summary>
    Now,
    /// <summary>They are on the farm and somebody else is playing: they wait, held, until that turn ends.</summary>
    AfterTurnInProgress,
    /// <summary>They are away: the morning is theirs when they next join.</summary>
    WhenTheyJoin
}

/// <param name="Before">What they were before: still to play, finished or sitting out.</param>
/// <param name="Join">With <see cref="FreshDayStart.Now"/>, where they were put (a turn of their own at 6:00 AM).</param>
public sealed record FreshDayResult(FarmerStatus Before, FreshDayStart Start, JoinResult? Join);

/// <summary>Serializable data only; online presence is deliberately never persisted.</summary>
public sealed record DayJournal
{
    public int SchemaVersion { get; init; } = 1;
    public string SaveId { get; init; } = "";
    public int AbsoluteDay { get; init; }
    public long Revision { get; init; }
    public PlayMode Mode { get; init; }
    public DayPhase Phase { get; init; }
    public int SessionNumber { get; init; }
    public string? AdvanceToken { get; init; }
    public List<FarmerJournal> Farmers { get; init; } = new();
    public List<long> CurrentSessionFarmerIds { get; init; } = new();
    /// <summary>The farm's ModRelay group as last reported (0.1.37); empty when the farm has none or none was ever reported. A journal from before 0.1.37 has none, and still loads.</summary>
    public List<GroupMember> Group { get; init; } = new();
}

/// <param name="AbsentDays">Whole days in a row this farmer never connected, counted up to this morning.</param>
/// <param name="SeenToday">They connected at some point this date.</param>
/// <param name="Away">They sit this date out only because of <paramref name="AbsentDays"/>, and are back in it the moment they connect.</param>
/// <param name="TimeReached">The clock, HHMM, at which this unfinished farmer last left their turn (0.1.29). Null: no parked turn. A journal from before 0.1.29 has none, and still loads.</param>
/// <param name="Waiting">They are on the farm and have chosen to wait for the turn in progress to end, then carry on at <paramref name="TimeReached"/> (0.1.29).</param>
/// <param name="NightsOwedFrom">The first date whose night ran without them and has not been shown to them yet (0.1.29). Null: nothing owed.</param>
/// <param name="MemberId">Set on a placeholder (0.1.37): the ModRelay account of the group member this entry stands in for until they have a farmer. <paramref name="Name"/> is then the member's name. Null for a farmer.</param>
public sealed record FarmerJournal(long Id, string Name, FarmerStatus Status, int AbsentDays = 0, bool SeenToday = false, bool Away = false,
    int? TimeReached = null, bool Waiting = false, int? NightsOwedFrom = null, string? MemberId = null)
{
    /// <summary>A group member with no farmer on the farm yet (0.1.37).</summary>
    public bool IsPlaceholder => MemberId is not null;
}

public sealed class DayCoordinationException : InvalidOperationException
{
    public DayCoordinationException(string message) : base(message) { }
}

/// <summary>
/// A farm whose group had members was told it now has none (0.1.41). That cannot be true: a group
/// nobody is left in is deleted, and its farm is then told of no group rather than of an empty one.
/// So it is a server that could not read its own members, and the farm keeps the group it has.
/// </summary>
public sealed class EmptyGroupException : InvalidOperationException
{
    public EmptyGroupException() : base("The farm's group came back empty; the group it already had is kept.") { }
}

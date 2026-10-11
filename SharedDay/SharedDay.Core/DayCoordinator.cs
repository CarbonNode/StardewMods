namespace SharedDay.Core;

/// <summary>
/// Authoritative, single-threaded day state machine. Call on the game thread and persist
/// ExportJournal together with its matching world at checkpoint boundaries. Stage the
/// advancing journal into the world before starting an overnight save.
/// This class never advances game time or saves the world itself.
/// </summary>
public sealed class DayCoordinator
{
    private const int CurrentSchemaVersion = 1;
    private readonly Dictionary<long, FarmerJournal> farmers;
    private readonly HashSet<long> online = new();
    private readonly HashSet<long> currentSession;
    private string saveId;
    private int absoluteDay;
    private long revision;
    private PlayMode mode;
    private DayPhase phase;
    private int sessionNumber;
    private string? advanceToken;

    private DayCoordinator(DayJournal journal)
    {
        ValidateJournal(journal);
        saveId = journal.SaveId;
        absoluteDay = journal.AbsoluteDay;
        revision = journal.Revision;
        mode = journal.Mode;
        phase = journal.Phase;
        sessionNumber = journal.SessionNumber;
        advanceToken = journal.AdvanceToken;
        farmers = journal.Farmers.ToDictionary(f => f.Id, f => f with { });
        currentSession = journal.CurrentSessionFarmerIds.ToHashSet();
    }

    public static DayCoordinator Create(string saveId, int absoluteDay, IEnumerable<FarmerIdentity> roster, PlayMode mode = PlayMode.Relay)
        => new(new DayJournal
        {
            SaveId = saveId,
            AbsoluteDay = absoluteDay,
            Mode = mode,
            Farmers = CreateRoster(roster)
        });

    /// <summary>
    /// Restores journal data with every player offline. Before admitting players, call
    /// ReconcileSavedDay with the date actually loaded from the game's durable save.
    /// </summary>
    public static DayCoordinator Restore(DayJournal journal) => new(journal);

    /// <summary>
    /// All required players finished; Together also requires every non-skipped player online.
    /// A day with nobody on its roster (a new server before anyone has claimed a cabin) waits
    /// for its first farmer instead of advancing on its own.
    /// </summary>
    /// <remarks>
    /// Farmers who only sit out because they have been away (see <see cref="AbsenceLimit"/>) do not
    /// end a day between them: someone has to have played it, or been let off it by the others.
    /// Otherwise a farm whose whole group is on holiday would run through its calendar by itself
    /// every time it was woken.
    /// </remarks>
    public bool IsReadyToAdvance => farmers.Count > 0 && farmers.Values.All(f => f.Status != FarmerStatus.Pending)
        && farmers.Values.Any(f => !f.Away)
        && (mode != PlayMode.Together || farmers.Values.Where(f => f.Status != FarmerStatus.Skipped).All(f => online.Contains(f.Id)));

    public DaySnapshot Snapshot => new(saveId, absoluteDay, revision, mode, phase, sessionNumber, advanceToken,
        IsReadyToAdvance, Array.AsReadOnly(farmers.Values.Select(f => new FarmerSnapshot(
            f.Id, f.Name, f.Status, online.Contains(f.Id), currentSession.Contains(f.Id), f.AbsentDays, f.Away, f.TimeReached, f.Waiting, f.NightsOwedFrom)).ToArray()));

    /// <summary>
    /// How many days in a row a farmer may miss before the farm stops waiting for them: from
    /// the next morning they sit each day out without anyone having to vote, until they connect
    /// again, which puts them straight back into that day. 0 waits for ever, as before. Policy
    /// of the host; it applies from the next new day.
    /// </summary>
    public int AbsenceLimit { get; set; }

    /// <summary>Returns a deep copy. Mutating this DTO cannot change the coordinator.</summary>
    public DayJournal ExportJournal() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        SaveId = saveId,
        AbsoluteDay = absoluteDay,
        Revision = revision,
        Mode = mode,
        Phase = phase,
        SessionNumber = sessionNumber,
        AdvanceToken = advanceToken,
        Farmers = farmers.Values.Select(f => f with { }).ToList(),
        CurrentSessionFarmerIds = currentSession.OrderBy(id => id).ToList()
    };

    /// <summary>
    /// The farmers playing right now: connected, still to play, in the turn in progress and not
    /// waiting for it to end. Only they keep a turn's clock; since 0.1.29 a farmer who left a
    /// turn unfinished keeps their own clock (<see cref="FarmerJournal.TimeReached"/>) instead of
    /// holding everyone else's.
    /// </summary>
    private IEnumerable<long> ActivePlayers() => currentSession.Where(id => farmers[id].Status == FarmerStatus.Pending && online.Contains(id) && !farmers[id].Waiting);

    public JoinResult Join(long farmerId)
    {
        var farmer = FindFarmer(farmerId);
        if (online.Contains(farmerId))
            return new(JoinDisposition.AlreadyPresent, sessionNumber);
        if (!farmer.SeenToday || (farmer.Away && phase == DayPhase.Open))
        {
            // Someone who was only sitting out because they had been away is back, and plays today.
            bool returns = farmer.Away && farmer.Status == FarmerStatus.Skipped && phase == DayPhase.Open;
            farmer = farmer with { SeenToday = true, Away = returns ? false : farmer.Away, Status = returns ? FarmerStatus.Pending : farmer.Status };
            farmers[farmerId] = farmer;
        }

        if (farmer.Status != FarmerStatus.Pending || phase == DayPhase.Advancing)
        {
            online.Add(farmerId);
            Changed();
            return new(JoinDisposition.Observer, sessionNumber);
        }

        online.Add(farmerId);
        // A farmer with a parked turn of their own, while somebody else is playing: they choose
        // between that turn at its clock and waiting for it to end. Until then they are in no turn.
        if (farmer.TimeReached is not null && ActivePlayers().Any(id => id != farmerId))
        {
            currentSession.Remove(farmerId);
            // A wait chosen before they last left (or before a restart) is asked again.
            farmers[farmerId] = farmer with { Waiting = false };
            Changed();
            return new(JoinDisposition.ChooseJoinOrWait, sessionNumber);
        }
        return new(Place(farmerId), sessionNumber);
    }

    /// <summary>
    /// Puts a connected, unfinished farmer into play. Somebody else playing: into their turn at
    /// its clock. Nobody playing: a parked clock of their own is resumed; otherwise, back into
    /// their own turn as it stands (no parked clock: a journal from before 0.1.29, or a reopened
    /// day), or a turn of their own at morning. A resumed turn keeps its number when it is still
    /// the current one; a promoted waiter gets a new one.
    /// </summary>
    private JoinDisposition Place(long farmerId)
    {
        var farmer = farmers[farmerId];
        bool othersActive = ActivePlayers().Any(id => id != farmerId);
        bool inSession = currentSession.Contains(farmerId);
        JoinDisposition disposition;
        if (othersActive)
            disposition = inSession ? JoinDisposition.ResumeSession : JoinDisposition.JoinCurrentSession;
        else if (inSession)
            disposition = farmer.TimeReached is not null ? JoinDisposition.ResumeOwnClock : JoinDisposition.ResumeSession;
        else
        {
            sessionNumber = checked(sessionNumber + 1);
            currentSession.Clear();
            disposition = farmer.TimeReached is not null ? JoinDisposition.ResumeOwnClock : JoinDisposition.StartSession;
        }
        currentSession.Add(farmerId);
        farmers[farmerId] = farmer with { Waiting = false };
        Changed();
        return disposition;
    }

    /// <summary>
    /// The answer "join now" to <see cref="JoinDisposition.ChooseJoinOrWait"/>: into the turn in
    /// progress at its clock (or, if it has ended meanwhile, back onto their own clock). Also lets
    /// a waiting farmer change their mind. Null when the farmer is not on the farm, has played,
    /// or is already in a turn.
    /// </summary>
    public JoinResult? JoinNow(long farmerId)
    {
        var farmer = FindFarmer(farmerId);
        if (!online.Contains(farmerId) || farmer.Status != FarmerStatus.Pending || currentSession.Contains(farmerId)) return null;
        RequireOpen();
        return new(Place(farmerId), sessionNumber);
    }

    /// <summary>
    /// The answer "wait" to <see cref="JoinDisposition.ChooseJoinOrWait"/>: the farmer stays on
    /// the farm, held, until the turn in progress has ended; <see cref="PromoteWaiter"/> then puts
    /// them on their own clock. False when they are not on the farm, have played, or are in a turn.
    /// </summary>
    public bool Wait(long farmerId)
    {
        var farmer = FindFarmer(farmerId);
        if (!online.Contains(farmerId) || farmer.Status != FarmerStatus.Pending || currentSession.Contains(farmerId)) return false;
        RequireOpen();
        if (farmer.Waiting) return true;
        farmers[farmerId] = farmer with { Waiting = true };
        Changed();
        return true;
    }

    /// <summary>
    /// Once nobody is playing, the first waiting farmer (in roster order) gets a turn of their own
    /// at their parked clock. Call it whenever the day may have changed; it answers null when there
    /// is nothing to do, so calling it often is fine.
    /// </summary>
    public (long Id, JoinResult Join)? PromoteWaiter()
    {
        if (phase != DayPhase.Open || ActivePlayers().Any()) return null;
        var waiter = farmers.Values.FirstOrDefault(f => f.Waiting && f.Status == FarmerStatus.Pending && online.Contains(f.Id) && !currentSession.Contains(f.Id));
        if (waiter is null) return null;
        return (waiter.Id, new JoinResult(Place(waiter.Id), sessionNumber));
    }

    /// <summary>
    /// Adds a farmer created after the day began (a friend claiming an empty cabin) to this
    /// day's roster as pending, so they play the current day. Returns false if already listed.
    /// </summary>
    public bool AddFarmer(FarmerIdentity farmer)
    {
        ArgumentNullException.ThrowIfNull(farmer);
        if (string.IsNullOrWhiteSpace(farmer.Name)) throw new DayCoordinationException("A farmer needs a name before joining the day.");
        if (farmers.ContainsKey(farmer.Id)) return false;
        RequireOpen();
        farmers.Add(farmer.Id, new FarmerJournal(farmer.Id, farmer.Name, FarmerStatus.Pending));
        Changed();
        return true;
    }

    /// <summary>
    /// A farmer leaves. An unfinished participant's clock is parked (<paramref name="timeReached"/>,
    /// HHMM, when the adapter knows it): they carry on from there, and nobody else's turn is held
    /// for them. A farmer waiting for a turn to end stops waiting.
    /// </summary>
    /// <returns>True only when presence changed. Disconnect never completes the day.</returns>
    public bool Disconnect(long farmerId, int? timeReached = null)
    {
        var farmer = FindFarmer(farmerId);
        if (!online.Remove(farmerId)) return false;
        if (farmer.Status == FarmerStatus.Pending && currentSession.Contains(farmerId) && timeReached is { } time && DayClock.IsValid(time))
            farmer = farmer with { TimeReached = time };
        farmers[farmerId] = farmer with { Waiting = false };
        Changed();
        return true;
    }

    /// <summary>
    /// Parks a connected, unfinished participant's clock without them leaving: a same-date save
    /// made while they play (0.1.29, "Save my turn") records where their turn stands, so a host
    /// that comes back from that save puts them on this clock.
    /// </summary>
    public bool Park(long farmerId, int timeReached)
    {
        var farmer = FindFarmer(farmerId);
        if (!online.Contains(farmerId) || farmer.Status != FarmerStatus.Pending || !currentSession.Contains(farmerId) || !DayClock.IsValid(timeReached)) return false;
        if (farmer.TimeReached == timeReached) return true;
        farmers[farmerId] = farmer with { TimeReached = timeReached };
        Changed();
        return true;
    }

    /// <summary>The adapter must authenticate farmerId as the sender. Only online, participating players can finish.</summary>
    public bool Complete(long farmerId)
    {
        var farmer = FindFarmer(farmerId);
        if (farmer.Status != FarmerStatus.Pending) return false;
        RequireOpen();
        if (!online.Contains(farmerId) || !currentSession.Contains(farmerId))
            throw new DayCoordinationException("Only a connected participant can complete their day.");
        farmers[farmerId] = farmer with { Status = FarmerStatus.Completed, TimeReached = null, Waiting = false };
        Changed();
        return true;
    }

    /// <summary>Host-only policy; the adapter must enforce host authorization.</summary>
    public bool Skip(long farmerId)
    {
        var farmer = FindFarmer(farmerId);
        if (farmer.Status == FarmerStatus.Skipped) return false;
        RequireOpen();
        // A deliberate skip (the host's, or the farm's vote) is not an absence rule's.
        farmers[farmerId] = farmer with { Status = FarmerStatus.Skipped, Away = false, Waiting = false };
        Changed();
        return true;
    }

    /// <summary>
    /// A farmer who was playing has gone and left their turn unfinished: the others stop waiting
    /// for them, and they are back in the day the moment they return (the same standing as a
    /// farmer the absence rule sits out). Unlike a skip it is nobody's decision that the day may
    /// end without them, so it never ends a day by itself: a farmer playing alone who walks away
    /// for ten minutes does not come back to the next morning.
    /// </summary>
    public bool StepAway(long farmerId, int? timeReached = null)
    {
        var farmer = FindFarmer(farmerId);
        if (farmer.Status != FarmerStatus.Pending) return false;
        RequireOpen();
        if (currentSession.Contains(farmerId) && timeReached is { } time && DayClock.IsValid(time)) farmer = farmer with { TimeReached = time };
        farmers[farmerId] = farmer with { Status = FarmerStatus.Skipped, Away = true, Waiting = false };
        online.Remove(farmerId);
        Changed();
        return true;
    }

    /// <summary>
    /// Puts a farmer back to "still to play today": a finish made by mistake, or a skip that
    /// should not stand. Returns what they were, or null when they were still to play anyway.
    ///
    /// Their presence is dropped, so the adapter joins them afresh and the ordinary rules decide
    /// where: back into their own session at the time it had reached while that session is still
    /// the current one (an undone finish earns no fresh morning), into whoever is playing now, or
    /// a new session when nobody is. Nothing they own or did is touched.
    /// </summary>
    public FarmerStatus? Reinstate(long farmerId)
    {
        var farmer = FindFarmer(farmerId);
        if (farmer.Status == FarmerStatus.Pending) return null;
        RequireOpen();
        farmers[farmerId] = farmer with { Status = FarmerStatus.Pending, Away = false, Waiting = false };
        online.Remove(farmerId);
        Changed();
        return farmer.Status;
    }

    /// <summary>
    /// Starts today's turns again from the beginning (0.1.34), a repair for a day that went wrong.
    /// The owner, 2026-10-09: "a setting to reset day to beginning and moves everyone to beds and
    /// resets the timer (as a bugfix tool?)". Every farmer who finished is to play again; parked
    /// clocks and waits are dropped, so nobody comes back to the middle of a turn; the farmers on
    /// the farm who are to play make up one new turn, which the adapter starts at morning.
    ///
    /// Skips stand: a farmer the farm let off today, or one sitting out after days away, is not
    /// waited for because of a restart (one who stepped away mid-turn comes back at morning, like
    /// everyone). Nothing anyone owns or did is touched, and the machine budget
    /// (<see cref="CalendarClock"/>) is the adapter's to keep: hours a furnace has had today are
    /// not given to it twice.
    /// </summary>
    /// <returns>The farmers in the new turn, in roster order. Empty when nobody on the farm is to play.</returns>
    public IReadOnlyList<long> RestartDay()
    {
        RequireOpen();
        foreach (var farmer in farmers.Values.ToList())
        {
            var status = farmer.Status == FarmerStatus.Completed ? FarmerStatus.Pending : farmer.Status;
            farmers[farmer.Id] = farmer with { Status = status, TimeReached = null, Waiting = false };
        }
        currentSession.Clear();
        var playing = farmers.Values.Where(f => f.Status == FarmerStatus.Pending && online.Contains(f.Id)).Select(f => f.Id).ToList();
        sessionNumber = checked(sessionNumber + 1);
        foreach (long id in playing) currentSession.Add(id);
        Changed();
        return playing.AsReadOnly();
    }

    /// <summary>
    /// Gives one farmer today again from 6:00 AM (0.1.35), a repair for one player's day that went
    /// wrong. The owner, 2026-10-11: "reset someone's day". They are put back to "still to play" (a
    /// finish, a skip or a step-away no longer stands), and their turn is parked at 6:00 AM, so
    /// the ordinary parked-turn rules hand them a morning of their own: at once when nobody else
    /// is playing, after the turn in progress when somebody is (they wait for it, held, if they
    /// are on the farm), and on their return when they are away. Nobody else's turn or clock is
    /// touched, and nothing anyone owns or did is.
    /// </summary>
    public FreshDayResult RestartFarmer(long farmerId)
    {
        var farmer = FindFarmer(farmerId);
        RequireOpen();
        var before = farmer.Status;
        currentSession.Remove(farmerId);
        bool here = online.Contains(farmerId);
        bool othersPlaying = ActivePlayers().Any(id => id != farmerId);
        farmers[farmerId] = farmer with
        {
            Status = FarmerStatus.Pending, Away = false, SeenToday = farmer.SeenToday || here,
            TimeReached = DayClock.Opens, Waiting = here && othersPlaying
        };
        Changed();
        if (!here) return new(before, FreshDayStart.WhenTheyJoin, null);
        if (othersPlaying) return new(before, FreshDayStart.AfterTurnInProgress, null);
        return new(before, FreshDayStart.Now, new JoinResult(Place(farmerId), sessionNumber));
    }

    /// <summary>
    /// The nights that ran while this farmer was away have been shown to them up to and including
    /// <paramref name="throughDay"/>. Clears what is owed when that covers it, or moves the start
    /// of what is owed past it. False when nothing was owed through that day.
    /// </summary>
    public bool AcknowledgeNightsShown(long farmerId, int throughDay)
    {
        var farmer = FindFarmer(farmerId);
        if (farmer.NightsOwedFrom is not { } from || throughDay < from) return false;
        int lastOwed = absoluteDay - 1;
        farmers[farmerId] = farmer with { NightsOwedFrom = throughDay >= lastOwed ? null : throughDay + 1 };
        Changed();
        return true;
    }

    public bool SetMode(PlayMode value)
    {
        if (!Enum.IsDefined(typeof(PlayMode), value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (mode == value) return false;
        RequireOpen();
        mode = value;
        Changed();
        return true;
    }

    /// <summary>
    /// Latches one token for this day's overnight transition. Stage the journal into the
    /// matching world BEFORE invoking the transition; commit them together. A duplicate
    /// call returns the same token. Unsaved changes roll back to the previous checkpoint.
    /// </summary>
    public string BeginAdvance()
    {
        if (phase == DayPhase.Advancing) return advanceToken!;
        if (!IsReadyToAdvance)
            throw new DayCoordinationException("The day is still waiting for required players.");
        advanceToken ??= Guid.NewGuid().ToString("N");
        phase = DayPhase.Advancing;
        Changed();
        return advanceToken;
    }

    /// <summary>
    /// Called only after the adapter confirms a durable next-day game save. A scheduled
    /// transition or a DayStarted event alone is not acknowledgement of a durable save.
    /// </summary>
    public void AcknowledgeSavedNextDay(string token, int actualSavedDay, IEnumerable<FarmerIdentity> nextRoster)
    {
        if (phase != DayPhase.Advancing || advanceToken is null || !StringComparer.Ordinal.Equals(token, advanceToken))
            throw new DayCoordinationException("The save acknowledgement does not match the pending transition.");
        if (actualSavedDay != checked(absoluteDay + 1))
            throw new DayCoordinationException("A transition is committed only by a saved date exactly one day later.");
        StartNextDay(actualSavedDay, nextRoster);
    }

    /// <summary>
    /// Reconciles restart state against the game's durable save date. Same-date recovery
    /// clears an unfinished transition without running overnight twice. An advancing
    /// journal plus exactly the next saved day finalizes that transition. Any other
    /// mismatch stops the adapter from accepting play.
    /// </summary>
    public ReconciliationResult ReconcileSavedDay(int actualSavedDay, IEnumerable<FarmerIdentity> nextRoster)
    {
        if (actualSavedDay == absoluteDay)
        {
            if (phase != DayPhase.Advancing) return ReconciliationResult.SameDay;
            phase = DayPhase.Open;
            Changed();
            return ReconciliationResult.RecoveredPendingAdvance;
        }

        if (phase == DayPhase.Advancing && actualSavedDay == checked(absoluteDay + 1))
        {
            StartNextDay(actualSavedDay, nextRoster);
            return ReconciliationResult.FinalizedAdvance;
        }

        throw new DayCoordinationException("The game save date and SharedDay journal disagree; restore a matching checkpoint before playing.");
    }

    private void StartNextDay(int day, IEnumerable<FarmerIdentity> nextRoster)
    {
        // Validate before changing any state, so a bad roster cannot half-commit a day.
        var next = CreateRoster(nextRoster);
        ValidateRoster(next);
        int ended = absoluteDay;
        // A farmer who never connected on the date just ended has been away one day more; one
        // who did starts again from none. Past the limit they sit the new date out until they
        // connect. A farmer still connected through the night is here, whatever the count.
        // A farmer who was not here for the night is owed it: its totals and level-ups are
        // shown to them before their next turn (NightsOwedFrom keeps the first such date).
        for (int i = 0; i < next.Count; i++)
        {
            if (!farmers.TryGetValue(next[i].Id, out var before)) continue;
            bool here = online.Contains(next[i].Id);
            int absent = before.SeenToday || here ? 0 : checked(before.AbsentDays + 1);
            bool away = AbsenceLimit > 0 && absent >= AbsenceLimit && !here;
            int? owed = here ? before.NightsOwedFrom : before.NightsOwedFrom ?? ended;
            next[i] = next[i] with { AbsentDays = absent, SeenToday = here, Away = away, Status = away ? FarmerStatus.Skipped : FarmerStatus.Pending, NightsOwedFrom = owed };
        }
        farmers.Clear();
        foreach (var farmer in next) farmers.Add(farmer.Id, farmer);
        online.IntersectWith(farmers.Keys);
        currentSession.Clear();
        absoluteDay = day;
        phase = DayPhase.Open;
        advanceToken = null;
        sessionNumber = 0;
        // Connected farmers all enter the real next morning together, without requiring
        // artificial disconnect/reconnect events from the multiplayer adapter.
        foreach (var id in online) currentSession.Add(id);
        if (currentSession.Count > 0) sessionNumber = 1;
        Changed();
    }

    private FarmerJournal FindFarmer(long id) => farmers.TryGetValue(id, out var farmer)
        ? farmer : throw new DayCoordinationException($"Farmer {id} is not on this day's roster.");

    private void RequireOpen()
    {
        if (phase != DayPhase.Open) throw new DayCoordinationException("The overnight transition has already started.");
    }

    private void Changed() => revision = checked(revision + 1);

    private static List<FarmerJournal> CreateRoster(IEnumerable<FarmerIdentity> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        return roster.Select(f => f is null ? throw new ArgumentException("A farmer cannot be null.", nameof(roster))
            : new FarmerJournal(f.Id, f.Name, FarmerStatus.Pending)).ToList();
    }

    private static void ValidateRoster(List<FarmerJournal> roster)
    {
        if (roster is null) throw new DayCoordinationException("A day needs a roster.");
        if (roster.Any(f => f is null || string.IsNullOrWhiteSpace(f.Name) || !Enum.IsDefined(typeof(FarmerStatus), f.Status)
                || f.AbsentDays < 0 || (f.Away && f.Status != FarmerStatus.Skipped)
                || (f.TimeReached is { } time && !DayClock.IsValid(time)) || (f.Waiting && f.Status != FarmerStatus.Pending)
                || f.NightsOwedFrom is < 0))
            throw new DayCoordinationException("The roster contains an invalid farmer.");
        if (roster.Select(f => f.Id).Distinct().Count() != roster.Count)
            throw new DayCoordinationException("A farmer can appear only once in a day's roster.");
    }

    private static void ValidateJournal(DayJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.SchemaVersion != CurrentSchemaVersion) throw new DayCoordinationException("Unsupported SharedDay journal version.");
        if (string.IsNullOrWhiteSpace(journal.SaveId) || journal.AbsoluteDay < 0 || journal.Revision < 0 || journal.SessionNumber < 0)
            throw new DayCoordinationException("The journal has invalid save identity, date, revision, or session data.");
        if (!Enum.IsDefined(typeof(PlayMode), journal.Mode) || !Enum.IsDefined(typeof(DayPhase), journal.Phase))
            throw new DayCoordinationException("The journal has an unknown mode or phase.");
        ValidateRoster(journal.Farmers);
        if (journal.CurrentSessionFarmerIds is null
            || journal.CurrentSessionFarmerIds.Distinct().Count() != journal.CurrentSessionFarmerIds.Count
            || journal.CurrentSessionFarmerIds.Any(id => journal.Farmers.All(f => f.Id != id))
            || (journal.SessionNumber == 0 && journal.CurrentSessionFarmerIds.Count > 0))
            throw new DayCoordinationException("The journal has an invalid session roster.");
        if (journal.AdvanceToken is not null && !Guid.TryParseExact(journal.AdvanceToken, "N", out _))
            throw new DayCoordinationException("The journal has an invalid transition token.");
        if (journal.Phase == DayPhase.Advancing
            && (journal.AdvanceToken is null || journal.Farmers.Any(f => f.Status == FarmerStatus.Pending)))
            throw new DayCoordinationException("An advancing journal must identify a fully completed transition.");
    }
}

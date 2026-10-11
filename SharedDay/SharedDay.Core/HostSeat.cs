namespace SharedDay.Core;

/// <summary>What to do about the host PC's own farmer right now.</summary>
public enum HostSeatAction
{
    /// <summary>Leave things as they are.</summary>
    None,
    /// <summary>Somebody sat down: put the host's farmer on today's roster and into the day.</summary>
    AddAndJoin,
    /// <summary>Somebody is there and the farmer is on the roster but not in the day: join them.</summary>
    Join,
    /// <summary>Somebody came back to a farmer who was only sitting out because the seat was empty.</summary>
    BringBack,
    /// <summary>Nobody is there any more and the farmer has not finished: stop waiting for them.</summary>
    StepOut
}

/// <summary>
/// A farm's host PC is usually nobody's seat: the farm runs by itself and its own farmer plays no
/// turn (IncludeHost false). But the same PC is sometimes played at. The person then IS that
/// farmer, and three things were wrong for them: the turn board had no face for them, the farm
/// counted nobody on it and was put to sleep under them after fifteen minutes, and the stand-in
/// for an absent person pressed their menus.
///
/// So the seat is decided by what is true at the moment. While somebody is really using the PC
/// its farmer is a player like any other; when they leave without finishing, the farm stops
/// waiting for them, and it is nobody's seat again.
/// </summary>
public static class HostSeat
{
    /// <param name="includeHost">The host's farmer always plays (a PC that is only ever a player's).</param>
    /// <param name="present">Somebody has used this PC's keyboard, mouse or controller lately.</param>
    /// <param name="dayOpen">The day can still be changed (the night has not begun).</param>
    /// <param name="farmer">The host's farmer on today's roster, or null when not on it.</param>
    public static HostSeatAction Decide(bool includeHost, bool present, bool dayOpen, FarmerSnapshot? farmer)
    {
        // A PC that is always a player's is handled by the ordinary rules, as before.
        if (includeHost || !dayOpen) return HostSeatAction.None;
        if (farmer is null) return present ? HostSeatAction.AddAndJoin : HostSeatAction.None;
        if (present)
        {
            // Away: sitting out only because the seat was empty (or they had not been seen for days).
            if (farmer.Status == FarmerStatus.Skipped && farmer.Away) return HostSeatAction.BringBack;
            if (farmer.Status == FarmerStatus.Pending && !farmer.IsOnline) return HostSeatAction.Join;
            return HostSeatAction.None;
        }
        // Finished is finished, and a deliberate skip stands. Only an unfinished turn nobody is
        // sitting at would hold everyone else.
        return farmer.Status == FarmerStatus.Pending ? HostSeatAction.StepOut : HostSeatAction.None;
    }

    /// <summary>
    /// Whether the host PC's own farmer is one of the farm's players, for anything that lists them
    /// (another mod's list of a farm's people asks this through Shared Day's API). A PC whose farmer
    /// always plays, one somebody is sitting at now, and one whose farmer is on today's roster
    /// (they sat down earlier today) all count; a host PC that only hosts does not.
    /// </summary>
    /// <param name="includeHost">The host's farmer always plays.</param>
    /// <param name="present">Somebody has used this PC's keyboard, mouse or controller lately.</param>
    /// <param name="onTodaysRoster">The host's farmer is on today's roster.</param>
    public static bool Plays(bool includeHost, bool present, bool onTodaysRoster) => includeHost || present || onTodaysRoster;
}

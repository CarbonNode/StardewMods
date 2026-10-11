namespace SharedDay.Core;

/// <summary>What one press at a farm's host PC means for its seat.</summary>
public enum HostSeatPress
{
    /// <summary>Nobody is seated and this is not the seat key: the press is nobody's, and does nothing.</summary>
    Ignore,
    /// <summary>The seat key on an empty seat: somebody sits down on purpose.</summary>
    Sit,
    /// <summary>Somebody is seated and playing: the press keeps them seated.</summary>
    Stay,
    /// <summary>The seat key from somebody seated: they get up now, without waiting for the seat to go quiet.</summary>
    Leave
}

/// <summary>
/// A farm that hosts itself sits on a PC that is used for other things. While its window is the
/// one in front, every key typed on that PC lands in the game: on 2026-10-07 another program's
/// automation typed into the live farm's window, which seated the farm's own farmer, opened its
/// map, kept the farm from sleeping and, once, slept it into the next day. "Any press means
/// somebody is playing" cannot tell a person from that.
///
/// So sitting down is one deliberate key. Until it is pressed nothing typed at the host PC
/// reaches the game; once it is, the person plays like anybody else, and the same key gets up.
/// </summary>
public static class HostSeatKey
{
    public static HostSeatPress Decide(bool seated, bool isSeatKey) =>
        isSeatKey ? (seated ? HostSeatPress.Leave : HostSeatPress.Sit)
                  : (seated ? HostSeatPress.Stay : HostSeatPress.Ignore);
}

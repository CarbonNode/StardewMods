namespace SharedDay.Core;

/// <summary>What the adapter should do with a held save on this tick.</summary>
public enum HeldSaveStep
{
    /// <summary>No save is being held for.</summary>
    Nothing,
    /// <summary>Keep waiting for the players to answer.</summary>
    Wait,
    /// <summary>Every player has frozen and answered, and one tick has passed: write the save now.</summary>
    Save,
    /// <summary>Not everyone answered in time: write nothing and let everyone go.</summary>
    GiveUp
}

/// <summary>
/// "Save my turn" (0.1.29): a same-date save while a player is still playing. The game cannot
/// safely write connected farmhands unless they stand still, so the host asks every connected
/// player to freeze ("hold"), each answers once frozen ("held"), the host waits one more tick
/// and writes the save, then lets everyone go ("release"). A player who does not answer within
/// <see cref="Patience"/> means no save at all: the save is all or nothing. The host and the
/// players exchange the messages; this only keeps the score.
/// </summary>
public sealed class HeldSave
{
    /// <summary>How long the host waits for every player to answer before giving up.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    private readonly HashSet<long> wanted = new();
    private readonly HashSet<long> held = new();
    private DateTime since;
    private int ticksAllHeld;

    /// <summary>A save is being held for.</summary>
    public bool Holding { get; private set; }

    /// <summary>Who asked for the save.</summary>
    public long Requester { get; private set; }

    /// <summary>The players whose answer is needed.</summary>
    public IReadOnlyCollection<long> Wanted => wanted;

    /// <summary>Every wanted player has answered.</summary>
    public bool AllHeld => Holding && held.Count >= wanted.Count;

    /// <summary>This player has frozen and answered.</summary>
    public bool IsHeld(long farmerId) => Holding && held.Contains(farmerId);

    /// <summary>Starts holding for a save. False when one is already under way or nobody needs asking.</summary>
    public bool Begin(long requester, IEnumerable<long> remoteFarmers, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(remoteFarmers);
        if (Holding) return false;
        wanted.Clear();
        held.Clear();
        wanted.UnionWith(remoteFarmers);
        if (wanted.Count == 0) return false;
        Requester = requester;
        since = now;
        ticksAllHeld = 0;
        Holding = true;
        return true;
    }

    /// <summary>A player says they are frozen. An answer from somebody not asked is ignored.</summary>
    /// <returns>True when that was the last answer needed.</returns>
    public bool Acknowledge(long farmerId)
    {
        if (!Holding || !wanted.Contains(farmerId)) return false;
        held.Add(farmerId);
        return AllHeld;
    }

    /// <summary>A player asked has left: their answer is no longer needed, nor is their save.</summary>
    public void Forget(long farmerId)
    {
        wanted.Remove(farmerId);
        held.Remove(farmerId);
    }

    /// <summary>Once a tick. The save is written on the tick after the last answer, never in the same one.</summary>
    public HeldSaveStep Tick(DateTime now)
    {
        if (!Holding) return HeldSaveStep.Nothing;
        if (AllHeld) return ++ticksAllHeld >= 2 ? HeldSaveStep.Save : HeldSaveStep.Wait;
        return now - since >= Patience ? HeldSaveStep.GiveUp : HeldSaveStep.Wait;
    }

    /// <summary>The save was written or given up: everyone is let go.</summary>
    public void End()
    {
        Holding = false;
        wanted.Clear();
        held.Clear();
        ticksAllHeld = 0;
    }

    /// <summary>
    /// The periodic save (SaveTurnEveryMinutes): due once the farm's clock has moved that many
    /// game minutes past the last such save. 0 never.
    /// </summary>
    public static bool PeriodicDue(int everyMinutes, int minutesNow, int minutesAtLastSave)
        => everyMinutes > 0 && minutesNow - minutesAtLastSave >= everyMinutes;
}

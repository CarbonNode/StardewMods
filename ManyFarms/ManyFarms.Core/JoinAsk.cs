namespace ManyFarms.Core;

/// <summary>
/// Whether the farm picker opens by itself on a join (0.5.2). Once, ever, per farmer: a farmer who
/// has been asked (and picked, or pressed Decide later) is not asked again on the next join; the
/// picker is theirs to open from the menu or the key. The owner, 2026-10-08, on being asked every
/// session: "doesnt need to popup everytime i join".
/// </summary>
public static class JoinAsk
{
    /// <param name="askedThisSession">The picker already opened by itself in this session.</param>
    /// <param name="askOnJoin">The setting.</param>
    /// <param name="settled">The player is standing free in the world, past the first seconds, not in an event, with a made character.</param>
    /// <param name="hasPlots">The farm has plots to pick from.</param>
    /// <param name="hasFarm">The farmer already farms somewhere (a plot, or the main farm by name).</param>
    /// <param name="askedEver">The farmer remembers being asked on an earlier join.</param>
    public static bool ShouldOpen(bool askedThisSession, bool askOnJoin, bool settled, bool hasPlots, bool hasFarm, bool askedEver)
        => !askedThisSession && askOnJoin && settled && hasPlots && !hasFarm && !askedEver;
}

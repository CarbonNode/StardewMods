using StardewValley;

namespace SharedDay.Mod;

/// <summary>
/// Whose farmer is whose (0.1.37, see SharedDay.Core.FarmerOwnership): the ModRelay account a
/// farmer belongs to, kept on the farmer's own modData so it travels with the save. Set when a
/// launcher-linked player makes the farmer, when the group's tracker says a member picked it
/// ("I'm playing as", or a claim in the launcher), or by the host's console
/// (`shared_day owner &lt;farmer&gt; &lt;account id&gt; [name]`; `shared_day disown &lt;farmer&gt;`).
///
/// Every copy of a farmer is written, and a read looks at every copy (0.1.44). The host holds a
/// farmhand twice while they are connected: the copy it plays with (Game1.getAllFarmers) and the
/// copy it saves (netWorldState.farmhandData). Writing only the first is lost with the connection,
/// which is what Parked.WriteEverywhere already works around; ownership had the same hole, so
/// OAKLEY's claim on 2026-10-16 never reached the save and the farm would have asked the owner for a
/// farmer again at its next wake, holding the day for him twice.
/// </summary>
internal static class Ownership
{
    internal const string OwnerKey = "CarbonNode.SharedDay/Owner";
    internal const string OwnerNameKey = "CarbonNode.SharedDay/OwnerName";

    /// <summary>What one copy of a farmer says. A copy that says nothing leaves the answer to the others.</summary>
    private static (string? Id, string? Name) OfOne(Farmer? farmer)
    {
        if (farmer is null || !farmer.modData.TryGetValue(OwnerKey, out string id) || string.IsNullOrWhiteSpace(id)) return (null, null);
        return (id, farmer.modData.TryGetValue(OwnerNameKey, out string name) && !string.IsNullOrWhiteSpace(name) ? name : null);
    }

    internal static (string? Id, string? Name) Of(Farmer? farmer)
    {
        if (farmer is null) return (null, null);
        // The copy in hand almost always answers (every write reaches every copy); the rest are
        // only looked at for a farmer written before 0.1.44, or read before the first write.
        var owner = OfOne(farmer);
        if (owner.Id is not null) return owner;
        foreach (var copy in Copies(farmer))
        {
            owner = OfOne(copy);
            if (owner.Id is not null) return owner;
        }
        return (null, null);
    }

    /// <summary>
    /// Every copy of this farmer the host holds: the one in hand, the ones the game lists, and the
    /// farmhand data it saves. The same copy is never returned twice.
    /// </summary>
    private static IEnumerable<Farmer> Copies(Farmer farmer)
    {
        var copies = new List<Farmer> { farmer };
        try
        {
            foreach (var other in Game1.getAllFarmers())
                if (other.UniqueMultiplayerID == farmer.UniqueMultiplayerID && !copies.Any(c => ReferenceEquals(c, other))) copies.Add(other);
            if (Game1.netWorldState?.Value?.farmhandData is { } data
                && data.TryGetValue(farmer.UniqueMultiplayerID, out var kept) && kept is not null
                && !copies.Any(c => ReferenceEquals(c, kept))) copies.Add(kept);
        }
        catch (Exception)
        {
            // No world loaded, or a game that keeps its farmhands another way: the copy in hand still gets written.
        }
        return copies;
    }

    /// <returns>True when something changed.</returns>
    internal static bool Set(Farmer farmer, string accountId, string? name)
    {
        bool changed = false;
        foreach (var copy in Copies(farmer))
        {
            var (id, had) = OfOne(copy);
            if (id == accountId && (name is null || had == name)) continue;
            copy.modData[OwnerKey] = accountId;
            if (!string.IsNullOrWhiteSpace(name)) copy.modData[OwnerNameKey] = name;
            changed = true;
        }
        return changed;
    }

    internal static bool Clear(Farmer farmer)
    {
        bool had = false;
        foreach (var copy in Copies(farmer))
        {
            had |= copy.modData.Remove(OwnerKey);
            copy.modData.Remove(OwnerNameKey);
        }
        return had;
    }
}

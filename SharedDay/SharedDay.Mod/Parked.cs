using System.Text.Json;
using StardewValley;

namespace SharedDay.Mod;

/// <summary>Where a farmer stood when their turn was parked: the spot their own game takes them back to.</summary>
public sealed class ParkedSpot
{
    public int Day { get; set; }
    public int Session { get; set; }
    public string Location { get; set; } = "";
    public int TileX { get; set; }
    public int TileY { get; set; }
    public int Facing { get; set; }
    public int TimeOfDay { get; set; }
}

/// <summary>
/// Exact resume (0.1.29). A farmer who leaves a turn unfinished comes back to the clock they
/// left (DayCoordinator.TimeReached); this puts them back on the tile they left too. The spot is
/// kept in that farmer's own modData, which the host saves with the farmhand and the client gets
/// on joining. The host writes it when the farmer disconnects (its copy of them is then the only
/// one); the farmer's own game writes it when it freezes for a mid-turn save ("Save my turn"),
/// since a connected farmhand's fields are its own game's to write; and the farmer's own game
/// removes it once it has taken them there.
/// </summary>
internal static class Parked
{
    internal const string Key = "CarbonNode.SharedDay/Parked";

    internal static ParkedSpot? Of(Farmer farmer)
    {
        if (!farmer.modData.TryGetValue(Key, out string json) || string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ParkedSpot>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>The farmer's spot now, or null when the game does not know where they are (no location on this copy of them).</summary>
    internal static ParkedSpot? Capture(Farmer farmer, int day, int session)
    {
        var where = farmer.currentLocation;
        if (where is null || string.IsNullOrEmpty(where.NameOrUniqueName)) return null;
        return new ParkedSpot
        {
            Day = day,
            Session = session,
            Location = where.NameOrUniqueName,
            TileX = farmer.TilePoint.X,
            TileY = farmer.TilePoint.Y,
            Facing = farmer.FacingDirection,
            TimeOfDay = Game1.timeOfDay
        };
    }

    internal static void Write(Farmer farmer, ParkedSpot spot) => farmer.modData[Key] = JsonSerializer.Serialize(spot);

    /// <summary>
    /// The host's write for a farmer who has just left. At that moment the host may hold two
    /// copies of them: the one that was connected (on its way out) and the one it keeps and saves
    /// (the farmhand data). A write on the wrong one is lost with it (the exact-resume lab of
    /// 2026-10-08 read no spot), so every copy gets it.
    /// </summary>
    /// <returns>How many copies were written.</returns>
    internal static int WriteEverywhere(long farmerId, ParkedSpot spot)
    {
        var copies = new List<Farmer>();
        foreach (var farmer in Game1.getAllFarmers())
            if (farmer.UniqueMultiplayerID == farmerId && !copies.Any(c => ReferenceEquals(c, farmer))) copies.Add(farmer);
        try
        {
            if (Game1.netWorldState.Value.farmhandData.TryGetValue(farmerId, out var kept) && kept is not null && !copies.Any(c => ReferenceEquals(c, kept))) copies.Add(kept);
        }
        catch (Exception) { }
        foreach (var copy in copies) Write(copy, spot);
        return copies.Count;
    }

    internal static void Clear(Farmer farmer)
    {
        if (farmer.modData.ContainsKey(Key)) farmer.modData.Remove(Key);
    }

    /// <summary>A spot that no longer stands (the day was restarted), off every copy of an absent farmer the host keeps.</summary>
    internal static void ClearEverywhere(long farmerId)
    {
        foreach (var farmer in Game1.getAllFarmers())
            if (farmer.UniqueMultiplayerID == farmerId) Clear(farmer);
        try
        {
            if (Game1.netWorldState.Value.farmhandData.TryGetValue(farmerId, out var kept) && kept is not null) Clear(kept);
        }
        catch (Exception) { }
    }

    /// <summary>"Beach (12, 30) facing left at 1:40 PM", for the log.</summary>
    internal static string Words(ParkedSpot spot)
    {
        string facing = spot.Facing switch { 0 => "up", 1 => "right", 2 => "down", 3 => "left", _ => "direction " + spot.Facing };
        return $"{spot.Location} ({spot.TileX}, {spot.TileY}) facing {facing} at {SharedDay.Core.DayClock.Words(spot.TimeOfDay)}";
    }
}

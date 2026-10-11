using ManyFarms.Core;
using StardewValley.GameData.WorldMaps;

namespace ManyFarms.Mod;

/// <summary>
/// The plots on the game's world map. The map places a player by the name of the location they
/// stand in, and knows "Farm" only, so a player on a plot was nowhere on it. Each plot (and its
/// cave) is entered under the same area as the main farm, on every player's game alike: the map
/// has one farm, and that is where every farmer at home is shown. The entry's own name on the
/// map's scroll is the plot's (see Patches.ScrollText_Postfix).
/// </summary>
internal static class WorldMapPlots
{
    /// <returns>How many entries were added (8 the first time, 0 when they are there or the map has no farm).</returns>
    public static int AddTo(IDictionary<string, WorldMapRegionData> regions)
    {
        int added = 0;
        foreach (var region in regions.Values)
        {
            if (region?.MapAreas is null) continue;
            foreach (var area in region.MapAreas)
            {
                if (area?.WorldPositions is null) continue;
                int at = area.WorldPositions.FindIndex(IsMainFarm);
                if (at < 0) continue;
                var farm = area.WorldPositions[at];
                foreach (int slot in FarmSlots.All.Reverse())
                {
                    string id = WorldMapIds.For(slot);
                    if (area.WorldPositions.Any(p => p?.Id == id)) continue;
                    // The whole of the plot's own map, whatever its size, onto the farm's place on the world map.
                    area.WorldPositions.Insert(at + 1, new WorldMapAreaPositionData
                    {
                        Id = id,
                        LocationNames = WorldMapIds.Locations(slot).ToList(),
                        MapPixelArea = farm.MapPixelArea,
                        ScrollText = farm.ScrollText,
                    });
                    added++;
                }
                // One farm on the map is enough: a second area that also names "Farm" is left as it is.
                return added;
            }
        }
        return added;
    }

    private static bool IsMainFarm(WorldMapAreaPositionData position) =>
        position is not null && (position.LocationName == FarmSlots.MainFarm || (position.LocationName is null && position.LocationNames is { Count: > 0 } names && names.Contains(FarmSlots.MainFarm)));
}

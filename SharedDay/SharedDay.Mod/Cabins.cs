using Microsoft.Xna.Framework;
using SharedDay.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;

namespace SharedDay.Mod;

/// <summary>
/// A cabin for every group member who still has to make a farmer (0.1.40). The owner, 2026-10-15:
/// "cabins shouldnt matter". Since 0.1.37 such a member holds the day open until they make a
/// farmer or are skipped, so a farm with no free cabin would hold it for ever: the one thing the
/// day waits for could not be done. The host now builds what the group needs, the game's own way
/// (a magical cabin, finished the moment it is placed, as the game itself stands the starting
/// cabins), with the game's full safety checks: a spot with a crop, a chest, a building or a
/// farmer on it is simply not used, and nothing of anyone's is ever cleared to make room.
/// Host only; the cabin reaches every player through the game's own syncing.
/// </summary>
internal static class Cabins
{
    /// <summary>The game's building type for a cabin; its look is a skin of that type.</summary>
    private const string Type = "Cabin";

    /// <summary>Empty cabins standing on the farm: one is all a joiner needs.</summary>
    internal static int Free() => Game1.getFarm().buildings.Count(b => b.GetIndoors() is Cabin cabin && cabin.owner is { isUnclaimedFarmhand: true });

    /// <summary>
    /// Builds what the group is short of. Returns how many went up (0 when none was needed, or
    /// when the farm had no room the game would take).
    /// </summary>
    /// <param name="membersWithNoFarmer">Group members on today's roster who have no farmer here.</param>
    internal static int Ensure(int membersWithNoFarmer, IMonitor monitor)
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady) return 0;
        int wanted = CabinPlan.HowMany(membersWithNoFarmer, Free());
        if (wanted == 0) return 0;
        var farm = Game1.getFarm();
        var origin = Origin(farm);
        int built = 0;
        foreach (var spot in CabinPlan.Spots(origin))
        {
            if (built == wanted) break;
            try
            {
                if (!farm.buildStructure(Type, new Vector2(spot.X, spot.Y), Game1.MasterPlayer, out var building, magicalConstruction: true) || building is null) continue;
                // The look the game would have given the next starting cabin; the plain one if this game has no skins.
                try { building.skinId.Value = Skin(built); } catch (Exception) { }
                built++;
                monitor.Log($"Built a cabin at {spot.X}, {spot.Y} for a group member who has no farmer yet.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Could not build a cabin at {spot.X}, {spot.Y}: {ex.Message}", LogLevel.Trace);
            }
        }
        if (built < wanted)
            monitor.Log($"The farm has room for {built} of the {wanted} cabin(s) the group still needs. Clear a space near the farmhouse, or let the member be skipped for today.", LogLevel.Warn);
        return built;
    }

    /// <summary>Where to start looking: beside the farmhouse, which is where the game puts the starting cabins too.</summary>
    private static Spot Origin(Farm farm)
    {
        var house = farm.buildings.FirstOrDefault(b => b.buildingType.Value == "Farmhouse");
        if (house is not null) return new Spot(Math.Max(0, house.tileX.Value - (CabinPlan.Width + 1)), house.tileY.Value);
        var warp = farm.GetMainFarmHouseEntry();
        return new Spot(Math.Max(0, warp.X - (CabinPlan.Width + 1)), Math.Max(0, warp.Y));
    }

    private static readonly string[] Skins = { "Log Cabin", "Stone Cabin", "Plank Cabin" };

    private static string Skin(int nth) => Skins[Math.Abs(nth) % Skins.Length];
}

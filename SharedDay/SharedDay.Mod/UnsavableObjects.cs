using System.Reflection;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace SharedDay.Mod;

/// <summary>
/// Things in the world that the game's save cannot write, and that their own mod therefore takes
/// out before every night's save and puts back in the morning: Unlockable Bundles' shop objects,
/// a spawner's own monsters, a companion. The night's save never contains them. Shared Day's
/// same-date save runs in the middle of a day, when they are all still standing there, and one
/// of them was enough to fail it ("The type Unlockable_Bundles.Lib.ShopObject was not expected").
///
/// So for the length of that save they are lifted out, and put straight back: the same objects,
/// in the same places. The file then holds what a night's save would hold, and a farm loaded
/// from it gets them back from their own mod, as it does every morning. Nobody sees it happen;
/// the same-date save only runs while no other player is connected.
///
/// What counts: an object whose class comes from a mod and is not marked for the save the way
/// SpaceCore asks ([XmlType("Mods_...")]). A class from the game itself, or one a mod has
/// registered with the save, is written as usual and never touched.
/// </summary>
internal static class UnsavableObjects
{
    private static readonly Assembly Game = typeof(Game1).Assembly;
    private static readonly Dictionary<Type, bool> Known = new();

    internal static bool IsUnsavable(object? thing)
    {
        if (thing is null) return false;
        var type = thing.GetType();
        if (Known.TryGetValue(type, out bool unsavable)) return unsavable;
        unsavable = false;
        // Every class up to the game's own must be one the save knows.
        for (var t = type; t is not null && t.Assembly != Game && t != typeof(object); t = t.BaseType)
            if (t.GetCustomAttribute<XmlTypeAttribute>(inherit: false)?.TypeName?.StartsWith("Mods_", StringComparison.Ordinal) != true)
                unsavable = true;
        return Known[type] = unsavable;
    }

    /// <summary>Take them out of every location. The returned action puts each one back where it was.</summary>
    internal static Action Lift(IMonitor monitor)
    {
        var restore = new List<Action>();
        var counts = new Dictionary<string, int>();
        void Count(object thing) => counts[thing.GetType().FullName ?? thing.GetType().Name] = counts.GetValueOrDefault(thing.GetType().FullName ?? thing.GetType().Name) + 1;

        Utility.ForEachLocation(location =>
        {
            foreach (var pair in location.objects.Pairs.Where(p => IsUnsavable(p.Value)).ToArray())
            {
                Vector2 tile = pair.Key;
                var value = pair.Value;
                location.objects.Remove(tile);
                restore.Add(() => { if (!location.objects.ContainsKey(tile)) location.objects.Add(tile, value); });
                Count(value);
            }
            foreach (var pair in location.terrainFeatures.Pairs.Where(p => IsUnsavable(p.Value)).ToArray())
            {
                Vector2 tile = pair.Key;
                var value = pair.Value;
                location.terrainFeatures.Remove(tile);
                restore.Add(() => { if (!location.terrainFeatures.ContainsKey(tile)) location.terrainFeatures.Add(tile, value); });
                Count(value);
            }
            foreach (var value in location.characters.Where(c => IsUnsavable(c)).ToArray())
            {
                location.characters.Remove(value);
                restore.Add(() => { if (!location.characters.Contains(value)) location.characters.Add(value); });
                Count(value);
            }
            foreach (var value in location.largeTerrainFeatures.Where(f => IsUnsavable(f)).ToArray())
            {
                location.largeTerrainFeatures.Remove(value);
                restore.Add(() => { if (!location.largeTerrainFeatures.Contains(value)) location.largeTerrainFeatures.Add(value); });
                Count(value);
            }
            foreach (var value in location.resourceClumps.Where(c => IsUnsavable(c)).ToArray())
            {
                location.resourceClumps.Remove(value);
                restore.Add(() => { if (!location.resourceClumps.Contains(value)) location.resourceClumps.Add(value); });
                Count(value);
            }
            foreach (var value in location.furniture.Where(f => IsUnsavable(f)).ToArray())
            {
                location.furniture.Remove(value);
                restore.Add(() => { if (!location.furniture.Contains(value)) location.furniture.Add(value); });
                Count(value);
            }
            return true;
        }, includeInteriors: true, includeGenerated: false);

        // A mod can also carry its own synced data as an entry among the locations' weather,
        // where the game sends it to every player for free (Unlockable Bundles keeps its bundles
        // there). The save writes that whole table, so such an entry comes out too.
        var weather = Game1.netWorldState.Value.LocationWeather;
        foreach (var pair in weather.Pairs.Where(p => IsUnsavable(p.Value)).ToArray())
        {
            string key = pair.Key;
            var value = pair.Value;
            weather.Remove(key);
            restore.Add(() => { if (!weather.ContainsKey(key)) weather.Add(key, value); });
            Count(value);
        }

        if (counts.Count > 0)
            monitor.LogOnce("Left out of the same-date save, as their own mods leave them out of the night's, and put straight back: "
                + string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key} ({c.Value})")) + ".", LogLevel.Info);
        return () =>
        {
            // Each on its own: one that will not go back must not strand the rest.
            foreach (var put in restore)
            {
                try { put(); }
                catch (Exception ex) { monitor.Log("Shared Day could not put an object back after its same-date save: " + ex.Message, LogLevel.Warn); }
            }
        };
    }
}

using HarmonyLib;
using Microsoft.Xna.Framework;
using SharedDay.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace SharedDay.Mod;

/// <summary>
/// Brings the villagers up to a clock the farm jumped to (0.1.33). A resumed turn, or a same-date
/// checkpoint loaded part way through the day, sets Game1.timeOfDay directly, and the game only
/// starts a villager's route at the exact minute of its schedule key: every key between the
/// checkpoint's clock and the new one is skipped, so a villager saved mid-walk stays mid-walk,
/// on a cliff path or in a bush, with no route (the owner, 2026-10-08: "stuck up there instead of
/// where they should be"). This puts each villager with a schedule at the end of the latest
/// route at or before the clock, facing as it ends, runs that route's end behaviour the way the
/// game does when a route has no path, and marks the key as attempted so the next one fires
/// normally. A villager in an event, with no schedule today, or already where that route ends
/// is left alone. Host only: the villagers are the host's.
/// </summary>
internal static class NpcCatchUp
{
    private static readonly System.Reflection.FieldInfo? WalkingInSquareField = AccessTools.Field(typeof(NPC), "isWalkingInSquare");

    /// <summary>A villager pacing a square at the end of a route (a private flag on the game's NPC).</summary>
    internal static bool WalkingInSquare(NPC npc) => WalkingInSquareField?.GetValue(npc) is true;

    /// <summary>What the last catch-up moved, one line per villager, for the log and the lab. Null until one has run.</summary>
    internal static IReadOnlyList<string>? LastMoved { get; private set; }

    internal static IReadOnlyList<string> ToClock(int time, IMonitor monitor)
    {
        var moved = new List<string>();
        if (!Context.IsMainPlayer || Game1.eventUp || Game1.isFestival()) { LastMoved = moved; return moved; }
        foreach (var npc in Utility.getAllCharacters().Where(c => c.IsVillager && !c.IsMonster).GroupBy(c => c.Name).Select(g => g.First()).ToList())
        {
            try
            {
                if (npc.Schedule is null || npc.Schedule.Count == 0 || npc.ignoreScheduleToday || npc.currentLocation is null) continue;
                int key = npc.Schedule.Keys.Where(k => k <= time).DefaultIfEmpty(-1).Max();
                // Before their first route of the day: their morning spot is right.
                if (key < 0) continue;
                var path = npc.Schedule[key];
                var target = path.targetLocationName is { Length: > 0 } name ? Game1.getLocationFromName(name) : null;
                if (target is null) continue;
                bool idle = npc.controller is null && npc.temporaryController is null && !npc.isMovingOnPathFindPath.Value && !WalkingInSquare(npc);
                bool there = npc.currentLocation == target && npc.TilePoint == path.targetTile;
                if (idle && there && npc.lastAttemptedSchedule >= key) continue;

                // What was under way is dropped, as the morning replay drops it: callbacks must not run for a route nobody is on.
                npc.Sprite.CurrentAnimation = null;
                npc.Halt();
                npc.controller = null;
                npc.temporaryController = null;
                npc.queuedSchedulePaths.Clear();
                AccessTools.Field(typeof(NPC), "directionsToNewLocation").SetValue(npc, null);
                npc.doingEndOfRouteAnimation.Value = false;
                npc.goingToDoEndOfRouteAnimation.Value = false;
                npc.isMovingOnPathFindPath.Value = false;
                npc.endOfRouteBehaviorName.Value = null;
                npc.endOfRouteMessage.Value = null;
                npc.IsWalkingTowardPlayer = false;
                npc.faceTowardFarmer = false;
                npc.faceTowardFarmerTimer = 0;
                npc.drawOffset = Vector2.Zero;
                npc.appliedRouteAnimationOffset = Vector2.Zero;
                npc.rotation = npc.yOffset = npc.swimTimer = npc.timerSinceLastMovement = 0;

                string from = $"{npc.currentLocation.NameOrUniqueName} ({npc.TilePoint.X},{npc.TilePoint.Y})";
                Game1.warpCharacter(npc, target, new Vector2(path.targetTile.X, path.targetTile.Y));
                npc.lastPosition = npc.Position;
                npc.previousEndPoint = npc.TilePoint;
                npc.lastCrossroad = Rectangle.Empty;
                npc.faceDirection(path.facingDirection);
                npc.lastAttemptedSchedule = key;
                // The end of that route, the way the game runs it when a route has no path to walk.
                var end = AccessTools.Method(typeof(NPC), "getRouteEndBehaviorFunction")?.Invoke(npc, new object?[] { path.endOfRouteBehavior, path.endOfRouteMessage }) as PathFindController.endBehavior;
                end?.Invoke(npc, target);
                moved.Add($"{npc.Name} from {from} to {path.targetLocationName} ({path.targetTile.X},{path.targetTile.Y}), their {DayClock.Words(key)} route");
            }
            catch (Exception ex)
            {
                // One villager a mod has made strange must not stop the rest from being put right.
                monitor.Log($"Could not bring {npc.Name} up to the clock: {ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
            }
        }
        LastMoved = moved;
        if (moved.Count > 0) monitor.Log($"Brought {moved.Count} villager{(moved.Count == 1 ? "" : "s")} up to {DayClock.Words(time)}: {string.Join("; ", moved)}.", LogLevel.Info);
        return moved;
    }
}

using HarmonyLib;
using Netcode;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace SharedDay.Mod;

/// <summary>
/// Puts a farm nobody sits at into the game's own "dedicated host" mode.
///
/// Stardew 1.6 carries this mode itself (FarmerTeam.hasDedicatedHost, DedicatedServer) and nothing
/// in the plain game switches it on. With it on, the game stops counting the host as a player
/// who has to act: the host follows the others into a festival when they are ready, any farmer
/// can ask the festival's host to start the main event, the festival ends when the others want
/// to leave, the host is shown no shipping totals at night, and a choice only the host could
/// make (the cave, the pet's name) goes to a player instead. Without it a festival needs the
/// host's own farmer to walk in ("festivalStart" is a ready check of every connected farmer),
/// and on Squeet Farm that farmer never moves, so the first festival would have waited for ever.
///
/// Two things in that mode work against Shared Day, and are put right here:
/// - it pauses the world whenever no player is connected, and a Relay day often ends with
///   everyone gone: the host still has to take itself to bed and save. The pause is lifted.
/// - it counts its festival steps once a day, and a Relay date is played more than once. The
///   count is cleared when a later turn starts the date again (see <see cref="NewTurn"/>).
/// </summary>
internal static class DedicatedHost
{
    private const string Marker = "CarbonNode.SharedDay/DedicatedHost";

    private static NetBool? Flag => AccessTools.Field(typeof(FarmerTeam), "hasDedicatedHost")?.GetValue(Game1.player?.team) as NetBool;

    /// <summary>False on a game too old to have the mode; the farm then runs as it did before.</summary>
    internal static bool Supported => AccessTools.Field(typeof(FarmerTeam), "hasDedicatedHost") is not null
        && AccessTools.TypeByName("StardewValley.Network.Dedicated.DedicatedServer") is not null;

    internal static void InstallPatches(Harmony harmony)
    {
        var tick = AccessTools.Method(AccessTools.TypeByName("StardewValley.Network.Dedicated.DedicatedServer"), "Tick");
        if (tick is not null) harmony.Patch(tick, postfix: new HarmonyMethod(typeof(DedicatedHost), nameof(AfterTick)));
    }

    /// <summary>Called by the host when it has read the day. Switches the mode on for a farm nobody sits at, and off again for one somebody does.</summary>
    internal static void Apply(bool unattended, IMonitor monitor)
    {
        if (!Context.IsMainPlayer || !Supported || Flag is not { } flag) return;
        var data = Game1.MasterPlayer.modData;
        if (unattended && !flag.Value)
        {
            flag.Value = true;
            data[Marker] = "1";
            monitor.Log("Nobody is at this PC, so the farm runs in the game's dedicated-host mode: festivals, the night's totals and host-only choices no longer wait on this PC.", LogLevel.Info);
        }
        else if (!unattended && flag.Value && data.ContainsKey(Marker))
        {
            // Only undo what Shared Day switched on; another mod's dedicated host is its own.
            flag.Value = false;
            data.Remove(Marker);
            monitor.Log("Somebody is at this PC again, so the farm left dedicated-host mode.", LogLevel.Info);
        }
    }

    /// <summary>A later Relay turn starts the same date again: the game's once-a-day festival steps must be free to happen for this turn too.</summary>
    internal static void NewTurn()
    {
        if (Game1.IsDedicatedHost) Game1.dedicatedServer?.ResetForNewDay();
        // The game also keeps each "is everyone ready?" check once it has passed, until the
        // night. A festival's three (going in, the main event, leaving) passed during the
        // earlier turn, so the next farmer's would wait for ever on a check that never runs
        // again. They are dropped; the first farmer to ask makes a fresh one.
        try
        {
            if (AccessTools.Field(Game1.netReady.GetType(), "ReadyChecks")?.GetValue(Game1.netReady) is not System.Collections.IDictionary checks) return;
            foreach (string id in checks.Keys.Cast<string>().Where(id => id is "festivalStart" or "festivalEnd" || id.StartsWith("MainEvent_", StringComparison.Ordinal)).ToArray())
                checks.Remove(id);
        }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException) { }
    }

    /// <summary>A sleep the game or Shared Day has already started; a second "yes" to the bed would start it over.</summary>
    internal static bool SleepUnderWay => Game1.newDay || Game1.activeClickableMenu is ReadyCheckDialog;

    // The mode pauses an empty world. Shared Day holds its own clock, and an empty farm may
    // still have a finished day to put to bed and save, which a paused world never does.
    private static void AfterTick()
    {
        if (ModEntry.Instance?.Active != true || !Game1.IsDedicatedHost) return;
        if (Game1.netWorldState.Value.IsPaused) Game1.netWorldState.Value.IsPaused = false;
    }
}

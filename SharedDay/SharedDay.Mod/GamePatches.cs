using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using SharedDay.Core;

namespace SharedDay.Mod;

internal static class GamePatches
{
    internal static void Install(Harmony harmony, bool junimo)
    {
        Patch(harmony, typeof(GameLocation), nameof(GameLocation.answerDialogueAction), nameof(SleepPrefix), new[] { typeof(string), typeof(string[]) });
        Patch(harmony, typeof(Game1), nameof(Game1.performTenMinuteClockUpdate), nameof(ClockPrefix), Type.EmptyTypes);
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Game1), nameof(Game1.performTenMinuteClockUpdate)),
            finalizer: new HarmonyMethod(typeof(GamePatches), nameof(ClockFinalizer)));
        Patch(harmony, typeof(StardewValley.Object), nameof(StardewValley.Object.minutesElapsed), nameof(MachinePrefix), new[] { typeof(int) });
        Patch(harmony, typeof(Game1), nameof(Game1.newDayAfterFade), nameof(NewDayPrefix), new[] { typeof(Action) });
        Patch(harmony, typeof(Farmer), "performPassOut", nameof(PassOutPrefix), Type.EmptyTypes);
        // The night shown to a returning farmer (0.1.29) reuses the game's own shipping menu,
        // which carries a save of its own; outside the real night that save must not run.
        Patch(harmony, typeof(StardewValley.Menus.SaveGameMenu), nameof(StardewValley.Menus.SaveGameMenu.update), nameof(SaveMenuPrefix), new[] { typeof(GameTime) });
        if (DedicatedHost.Supported) DedicatedHost.InstallPatches(harmony);
        if (junimo)
        {
            var type = AccessTools.TypeByName("JunimoServer.Services.AlwaysOn.AlwaysOnServer")
                ?? throw new MissingMemberException("JunimoServer's host automation type changed.");
            Patch(harmony, type, "SleepHostNow", nameof(JunimoSleepPrefix), new[] { typeof(string) });
            Patch(harmony, type, "HandleAutoPause", nameof(JunimoPausePrefix), Type.EmptyTypes);
            Patch(harmony, type, "HandleDialogueBox", nameof(JunimoDialoguePrefix), Type.EmptyTypes);
            var saveType = AccessTools.TypeByName("JunimoServer.Util.SaveNow")
                ?? throw new MissingMemberException("JunimoServer's same-day save type changed.");
            Patch(harmony, saveType, "TrySave", nameof(JunimoSavePrefix),
                new[] { typeof(IModHelper), typeof(string).MakeByRefType() });
        }
    }

    private static void Patch(Harmony harmony, Type type, string name, string prefix, Type[] args)
    {
        MethodInfo method = AccessTools.DeclaredMethod(type, name, args)
            ?? throw new MissingMethodException(type.FullName, name);
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(GamePatches), prefix));
    }

    private static bool SleepPrefix(string questionAndAnswer, ref bool __result)
    {
        var mod = ModEntry.Instance;
        if (questionAndAnswer != "Sleep_Yes" || mod?.Active != true) return true;
        // The game's dedicated-host mode puts the host to bed too. Whichever of the two asks
        // second finds the sleep already under way and must not start it over.
        if (mod.Released)
        {
            if (!Game1.IsDedicatedHost || !DedicatedHost.SleepUnderWay) return true;
            __result = true;
            return false;
        }
        // A host nobody sits at has no day of its own to finish and nobody to read a popup.
        if (Game1.IsDedicatedHost) { __result = true; return false; }
        mod.RequestFinish();
        // The original prompt is closing now; show status on the next update rather than nesting menus.
        mod.QueueWaitingMenu();
        __result = true;
        return false;
    }

    private static bool ClockPrefix(out bool __state)
    {
        __state = false;
        var mod = ModEntry.Instance;
        if (mod?.Active != true || mod.Released) return true;
        if (Game1.timeOfDay >= 2550)
        {
            mod.RequestFinish();
            return false;
        }
        if (mod.HoldClock) return false;
        int nextMinute = CalendarClock.ToMinutes(Game1.timeOfDay) + 10;
        mod.BeginMachineTick(nextMinute / 60 * 100 + nextMinute % 60);
        __state = true;
        return true;
    }

    private static void ClockFinalizer(Exception? __exception, bool __state)
    {
        if (__state) ModEntry.Instance?.EndMachineTick(__exception is null);
    }

    private static bool MachinePrefix(ref int minutes, ref bool __result)
    {
        var mod = ModEntry.Instance;
        int budget = mod?.MachineMinutesThisTick ?? -1;
        // Anything other than the ten-minute clock (a festival ending, a cutscene, another
        // mod) gets the same rule: only time past the day's furthest point is new.
        if (budget < 0 && minutes > 0) budget = mod?.UnscheduledMachineBudget(minutes) ?? -1;
        if (budget < 0 || minutes <= 0) return true;
        if (budget == 0) { __result = false; return false; }
        minutes = Math.Min(minutes, budget);
        return true;
    }

    private static bool NewDayPrefix() => ModEntry.Instance?.Active != true || ModEntry.Instance.Released;

    // A ShippingMenu runs an embedded SaveGameMenu at its end and waits for its quit flag; the
    // game's own end of night pushes a bare one when nothing was shipped. While Shared Day is
    // showing a farmer an old night, neither may save or wait on a night that is not happening:
    // the menu is told it has finished at once. (verify in 1.6.15: SaveGameMenu.quit is the public
    // flag ShippingMenu reads; the LAN driver already reads it by that name)
    private static bool SaveMenuPrefix(StardewValley.Menus.SaveGameMenu __instance)
    {
        var mod = ModEntry.Instance;
        if (mod?.Active != true || !mod.ShowingOldNight) return true;
        __instance.quit = true;
        return false;
    }

    private static bool PassOutPrefix(Farmer __instance)
    {
        var mod = ModEntry.Instance;
        if (mod?.Active != true || mod.Released || Game1.timeOfDay < 2550) return true;
        if (__instance == Game1.player) mod.RequestFinish();
        return false;
    }

    // Shared Day owns the host's complete warp/bed/ready sequence while enabled. A
    // second automation owner can replace its LocationRequest or start sleep twice.
    private static bool JunimoSleepPrefix() => ModEntry.Instance?.Active != true;

    private static bool JunimoPausePrefix()
    {
        var mod = ModEntry.Instance;
        if (mod?.Active != true) return true;
        if (mod.Released)
        {
            // An empty host may still need to commit a completed Relay day. Junimo's
            // idle-host pause must not stop native bed detection or the overnight fade.
            Game1.netWorldState.Value.IsPaused = false;
            return false;
        }
        return !mod.HoldClock;
    }

    private static bool JunimoDialoguePrefix() => ModEntry.Instance?.Active != true || !ModEntry.Instance.OwnsWaitingMenu;

    private static bool JunimoSavePrefix(ref string error, ref bool __result)
    {
        if (ModEntry.Instance?.Active != true) return true;
        // Junimo's raw serializer does not embed the current coordinator/clock or
        // preserve separate-wallet shipping. Never report a deferred save as success.
        error = "Shared Day controls same-day saves. Disconnect remote farmers and use shared_day checkpoint.";
        __result = false;
        return false;
    }
}

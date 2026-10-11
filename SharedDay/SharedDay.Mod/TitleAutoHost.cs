using SharedDay.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace SharedDay.Mod;

/// <summary>
/// Hosts a farm with nobody at the keyboard, for a dedicated host PC. When the game reaches
/// its title screen it does what a person would: opens Co-op, goes to Host, and picks the farm
/// (<see cref="AutoHostChoice"/>). It goes through the game's own menu and slot, so whatever
/// other mods hook there (save managers, custom save data) runs as it does for a player.
///
/// It acts once per launch. A person who backs out of the menu, or returns to the title
/// later, is left alone. A farm that cannot be chosen exactly is not hosted: the log says why.
/// It works whether or not Shared Day's turns are enabled.
/// </summary>
public sealed class TitleAutoHost
{
    /// <summary>The farm-host loop sets this on the game it starts for a wake. A hand-started game has it unset.</summary>
    public const string WokenByLoop = "SHAREDDAY_WOKEN";

    private const string LastHostedKey = "last-hosted";
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(90);
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly Func<AutoHostConfig> config;
    private int titleTicks;
    private bool opened;
    private bool done;
    private DateTime openedAt;

    /// <summary>What it is doing or why it stopped, for the log and the tests.</summary>
    public string Status { get; private set; } = "idle";

    /// <summary>True once it has loaded a farm by itself this launch: nobody is at this PC.</summary>
    public bool Hosted { get; private set; }

    public TitleAutoHost(IModHelper helper, IMonitor monitor, Func<AutoHostConfig> config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
    }

    private sealed class LastHosted
    {
        public string Save { get; set; } = "";
    }

    public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (done || config() is not { Enabled: true } wanted || Context.IsWorldReady) return;
        // A dedicated host loads the farm by itself. A PC somebody actually plays at should not:
        // it should land on the title screen and wait for the button, so the player joins and picks
        // their farmhand like anyone else (the owner, 2026-10-06: "i def dont want it to log you right
        // into the server ... should be the button, you join select character"). OnlyWhenWoken keeps
        // both: the farm host loop marks the game IT starts for a wake, and a game started by hand
        // carries no such mark, so the same PC can be an unattended host and a seat to play at.
        if (wanted.OnlyWhenWoken && Environment.GetEnvironmentVariable(WokenByLoop) != "1") return;
        if (Game1.activeClickableMenu is not TitleMenu title) return;
        if (!opened)
        {
            // Give the game and the other mods a second at the title before touching it.
            if (++titleTicks < 60) return;
            // Something is already open (a person got there first, or another mod's menu): theirs.
            if (TitleMenu.subMenu is not null) { Stop("left alone: another menu was already open on the title screen"); return; }
            title.ForceSubmenu(new CoopMenu(tooManyFarms: false, splitScreen: false, CoopMenu.Tab.HOST_TAB));
            opened = true;
            openedAt = DateTime.UtcNow;
            Status = "waiting for the Co-op menu to list farms";
            return;
        }
        if (TitleMenu.subMenu is not CoopMenu menu) { Stop("left alone: the Co-op menu was closed"); return; }
        var slots = menu.IsDoingTask() ? null : menu.MenuSlots?.OfType<LoadGameMenu.SaveFileSlot>().Where(slot => slot.Farmer?.slotName is not null).ToList();
        if (slots is null || slots.Count == 0)
        {
            if (DateTime.UtcNow - openedAt > GiveUpAfter) Stop("the game's Co-op menu offered no farm to host", LogLevel.Warn);
            return;
        }
        var saves = slots.Select(slot => slot.Farmer.slotName).ToList();
        string? pick = AutoHostChoice.Pick(saves, wanted.Save, helper.Data.ReadGlobalData<LastHosted>(LastHostedKey)?.Save);
        if (pick is null)
        {
            Stop($"no farm matches AutoHost.Save \"{wanted.Save}\" exactly. The Co-op menu offers: {string.Join(", ", saves)}", LogLevel.Warn);
            return;
        }
        ApplyHostOptions();
        done = true;
        Hosted = true;
        Status = "hosting " + pick;
        monitor.Log($"Hosting {AutoHostChoice.FarmName(pick)} by itself (AutoHost).", LogLevel.Info);
        slots.First(slot => slot.Farmer.slotName == pick).Activate();
    }

    public void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        if (!Context.IsMainPlayer || Game1.multiplayerMode != 2 || Constants.SaveFolderName is not { Length: > 0 } save) return;
        // Remembered whoever started it, so an empty AutoHost.Save means "the farm hosted last".
        helper.Data.WriteGlobalData(LastHostedKey, new LastHosted { Save = save });
        // Loading a save restores its own options; an unattended host has to stay reachable.
        if (config() is { Enabled: true }) ApplyHostOptions();
        // Only a farm this launch hosted by itself: a person who opened the game wants to see it.
        if (Hosted && config() is { HideWindow: true }) HostWindow.HideAll(monitor);
    }

    /// <summary>Friends must be able to connect, and a window nobody is looking at must keep running.</summary>
    private static void ApplyHostOptions()
    {
        Game1.options.enableServer = true;
        Game1.options.ipConnectionsEnabled = true;
        Game1.options.pauseWhenOutOfFocus = false;
    }

    private void Stop(string why, LogLevel level = LogLevel.Info)
    {
        done = true;
        Status = why;
        monitor.Log("AutoHost: " + why + ".", level);
    }
}

public sealed class AutoHostConfig
{
    /// <summary>Host a farm as soon as the game reaches its title screen. For a dedicated host PC.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Only host by itself when the ModRelay farm-host loop started this game for a wake, never when
    /// a person launched it. Leave it off for a box nobody sits at; turn it on for a PC that both
    /// hosts the farm and gets played at, so launching the game there lands on the title screen.
    /// </summary>
    public bool OnlyWhenWoken { get; set; }

    /// <summary>
    /// On a farm this PC loaded by itself, with the host's own farmer left out of the turns
    /// (IncludeHost false), press what nobody is there to press: the night's shipping totals, a
    /// message, a cutscene, another mod's popup. On unless switched off.
    /// </summary>
    public bool Autopilot { get; set; } = true;
    /// <summary>A farm this PC hosted by itself runs with no window on the desktop (Windows only).</summary>
    public bool HideWindow { get; set; }

    /// <summary>The farm to host: its save folder ("Sunny_385772993") or its name ("Sunny"). Empty hosts the farm this PC hosted last, or the one played most recently.</summary>
    public string Save { get; set; } = "";
}

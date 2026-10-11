using HarmonyLib;
using Microsoft.Xna.Framework;
using SharedDay.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace SharedDay.Mod;

/// <summary>Experimental adapter. All authority and world mutations stay on the host game thread.</summary>
public sealed class ModEntry : StardewModdingAPI.Mod
{
    // Version 2 requires every peer to reach a real bed before native sleep.
    internal const int ProtocolVersion = 2;
    internal static ModEntry? Instance;
    internal ModConfig Config = new();
    /// <summary>A left button that went down on the turn board: its release is the board's, not the world's.</summary>
    private bool boardPressed;
    private TurnNotifier notifier = null!;
    private TurnTracker tracker = null!;
    private FarmerPortraits portraits = null!;
    internal TitleJoinButton JoinButton = null!;
    internal TitleHub Hub = null!;
    internal TitleAutoHost AutoHost = null!;
    internal TurnHud TurnHud = null!;
    /// <summary>One shared wallet or a wallet each, set from ModRelay. Does nothing unless the farm-host loop asks (see Wallets).</summary>
    internal Wallets Wallets = null!;
    /// <summary>
    /// A farm was just loaded and its wallets have not been looked at yet. They are looked at once,
    /// on an update tick after SMAPI has given every mod both SaveLoaded and DayStarted (see
    /// <see cref="WalletsWhenSettled"/>); until then no farmhand is put into the day.
    /// </summary>
    private bool walletsDue;
    private bool walletsDayStarted;
    private int walletsTicks;
    /// <summary>Update ticks every mod gets after the load before the wallets are looked at (one second).</summary>
    private const int WalletsSettleTicks = 60;
    /// <summary>Update ticks after which a farm that is still busy (a menu, an event) keeps its wallets as they are for this start.</summary>
    private const int WalletsGiveUpTicks = 60 * 60;
    private DayCoordinator? coordinator;
    private CalendarClock? calendarClock;
    internal int MachineMinutesThisTick { get; private set; } = -1;
    private int machineTickTarget;
    private int unscheduledTargetMinute = -1;
    private StatusMessage? status;
    private string? error;
    private string? patchError;
    private bool checkpointPending;
    private bool savingCheckpoint;
    // A same-date save that failed: why, how often, and when to try again. The farm keeps running.
    private string? checkpointError;
    private int checkpointFailures;
    private DateTime checkpointRetryAt;
    private int clockFaults;
    private DateTime lastRetry;
    private HostAutopilot autopilot = null!;
    private bool sleeping;
    private FarmHouse? pendingSleepHome;
    private int pendingSleepDay;
    private int bedWaitTicks;
    private bool wasLocked;
    private bool waitingMenuQueued;
    private bool previousLocalFinished;
    private IClickableMenu? waitingMenu;
    private bool skipMenuOpen;
    private bool fixMenuOpen;
    private bool fixMenuQueued;
    private bool helpQueued;
    private bool helpVoting;
    private string[] helpChoices = Array.Empty<string>();
    private string? helpAbout;
    // Host only: today's votes to let an absent farmer sit the day out, kept on disk so they
    // outlive the farm being put to sleep and woken between two voters.
    private SkipVoteBook skipVotes = new();
    private const string SkipVotesFile = "data/skip-votes.json";
    private string? waitingMenuDescription;
    private bool waitingMenuCanFinish;
    private (string Key, bool Snap)? waitingMenuSelection;
    private string? previousStatusError;
    private int emptyTicks;
    private string? announcedTurn;
    private int lastSentHealth = -1;
    private bool skipMenuQueued;
    private bool finishConfirmQueued;
    private readonly HashSet<long> pendingJoins = new();
    private readonly HashSet<long> incompatiblePeers = new();
    // Holding the clock cannot stop an unadmitted farmer's own client from walking and
    // changing the world, so the host disconnects it. A peer with no verdict yet gets
    // HelloGrace to load and announce itself. Only a player who HAS Shared Day waits on this
    // (one without it, or without SMAPI, is turned away at once), and what they are doing in
    // that time is loading the farm: seconds on a plain game, minutes with a pack of a hundred
    // mods on a slow PC. Ten seconds disconnected exactly those players, so it is five minutes.
    private static readonly TimeSpan HelloGrace = TimeSpan.FromMinutes(5);
    private readonly Dictionary<long, DateTime> unadmittedSince = new();
    private readonly Dictionary<long, string> rejectReasons = new();
    private readonly HashSet<long> rejectedPeers = new();
    // ---- 0.1.29: parked turns, owed nights, held saves, removal orders ----
    /// <summary>Farmers the group removed from the farm (see Barring). Host only.</summary>
    private HashSet<long> barred = new();
    private bool newFarmersAllowed = true;
    /// <summary>Barred farmers whose farmhand could not be deleted, so the log says so once.</summary>
    private readonly HashSet<long> freeCabinFailed = new();
    /// <summary>Farmers whose current admission put the clock back to where they left their turn, so their own game puts them back where they stood.</summary>
    private readonly HashSet<long> resumedOwnClock = new();
    /// <summary>When each farmer owed a night was sent it, so a game that never answers (an older Shared Day) does not hold the clock for ever.</summary>
    private readonly Dictionary<long, DateTime> nightsSentAt = new();
    private static readonly TimeSpan NightsPatience = TimeSpan.FromSeconds(240);
    /// <summary>The date whose night absent farmers were credited for, so one night credits once.</summary>
    private int creditedDay = -1;
    /// <summary>"Save my turn": the host's score of who has frozen for the save.</summary>
    private readonly HeldSave heldSave = new();
    private int heldSaveToken;
    private bool heldSaveByRequest;
    private int lastHeldSaveMinute = -1;
    // A player's side of a hold: the host asked this game to freeze for a save.
    private bool holdActive;
    private bool holdAnswered;
    private int holdToken;
    private int holdTicks;
    private DateTime holdSince;
    private static readonly TimeSpan HoldPatience = TimeSpan.FromSeconds(10);
    /// <summary>The nights that ran while this player was away, shown by their own game before their turn.</summary>
    private NightReplay nights = null!;
    private bool parkedResumeDone;
    private bool previousChoosing;
    /// <summary>The clock the turn in progress started at (see StatusMessage.TurnStartedAt). Host only.</summary>
    private int turnStartedAt = 600;
    /// <summary>
    /// Where each connected remote farmer stood a moment ago, refreshed once a second. By the time
    /// SMAPI says a peer has left, the game has already put their farmhand back in its cabin, so
    /// the spot kept for their return is this one, not the farmer's at that moment.
    /// </summary>
    private readonly Dictionary<long, ParkedSpot> lastSeen = new();
    /// <summary>Farmers told they were removed and due to be disconnected once that line has had a moment to reach them (ticks left).</summary>
    private readonly Dictionary<long, int> kicksDue = new();
    /// <summary>The bed's question as this game words it right now; null is the game's own.</summary>
    private string? bedWording;
    /// <summary>Many Farms' name for a farmer's own farm ("Alice's farm"), when Many Farms has that API.</summary>
    internal Func<long, string?>? FarmLabelFor;
    private static readonly SButton[] InputButtons = Enum.GetValues<SButton>();
    internal bool Active => Config.Enabled && Context.IsWorldReady;
    internal bool Released => status?.Released == true && error is null;
    // The clock runs only while somebody is playing: connected, still to play, in the turn in
    // progress, not waiting for it to end, and not still being shown the nights they missed.
    // It also stands still while players are held for a save of the turn.
    internal bool HoldClock => Active && !Released && (error is not null || incompatiblePeers.Count > 0 || LoadingIn || heldSave.Holding
        || (Context.IsMainPlayer && (coordinator is null || !coordinator.Snapshot.Farmers.Any(f => f.IsActive && f.NightsOwedFrom is null))));
    private bool LocalFinished => status?.Finished.Contains(Game1.player.UniqueMultiplayerID) == true;
    private bool CanFinishLocal => !LocalFinished && !Released && error is null && status is not null
        && status.Error is null && !LocalChoosing && !LocalWaiting && (!Context.IsMainPlayer || IsRequired(Game1.player.UniqueMultiplayerID));
    /// <summary>The host's question to this player who came back to a parked turn while somebody else plays, or their chosen wait.</summary>
    private ReturnChoiceEntry? MyReturnChoice => Context.IsMainPlayer || status?.ReturnChoices is null ? null
        : status.ReturnChoices.FirstOrDefault(c => c.Id == Game1.player.UniqueMultiplayerID);
    private bool LocalChoosing => MyReturnChoice is { Waiting: false };
    private bool LocalWaiting => MyReturnChoice is { Waiting: true };
    /// <summary>Every other farmer on today's roster has played or sits it out: this player's finish ends the day.</summary>
    private bool IsLastToPlay => status is not null && !Released && (status.Roster.Length > 0
        ? status.Roster.All(r => r.Id == Game1.player.UniqueMultiplayerID || r.Status != FarmerStatus.Pending)
        : !status.WaitingFor.Any(n => n != Game1.player.Name));
    /// <summary>
    /// "Save my turn" is offered to a farmhand who is the only remote farmer on the farm and still
    /// playing, on a host that takes the request (0.1.29). A player's own game, never the host's.
    /// </summary>
    private bool CanSaveTurn => !Context.IsMainPlayer && status is { Fixes: >= 3, Released: false, Error: null } && CanFinishLocal && !holdActive
        && status.Roster.Count(r => r.Online && r.Id != Game1.MasterPlayer.UniqueMultiplayerID) == 1
        && status.Roster.Any(r => r.Id == Game1.player.UniqueMultiplayerID && r.Online && r.Status == FarmerStatus.Pending);
    /// <summary>This game is showing its player an old night (see NightReplay); the game's save screen must not run for it.</summary>
    internal bool ShowingOldNight => nights is not null && nights.Busy;
    internal bool OwnsWaitingMenu => waitingMenu is not null && Game1.activeClickableMenu == waitingMenu;
    // Never lock a farmer who is still being created: the lock closes menus, and vanilla's
    // character creation is a menu a new farmhand cannot get back.
    // A farmer who only stepped away from the host PC is counted with the finished ones (the
    // others do not wait for them), but their own day is not done: no freeze, no "your day is
    // done" popup. They came back to both, and the popup's answers changed under their hands
    // in the second it took to put them back in the day.
    private bool LocalAway => Context.IsMainPlayer && coordinator?.Snapshot.Farmers.FirstOrDefault(f => f.Id == Game1.player.UniqueMultiplayerID) is { Away: true };
    // The farm's password was typed today: Shared Day holds this player back no more until morning.
    private int bypassDay = -1;
    private bool bypassQueued;
    // 0.1.34: "Restart today for everyone" (see RestartToday and DayRestart).
    private bool restartConfirmQueued;
    private bool restartPasswordQueued;
    /// <summary>On the host's own farmer: the last restart of the day (DayRestart.Write). Every game on the farm reads it.</summary>
    internal const string RestartKey = "CarbonNode.SharedDay/restart";
    /// <summary>On each farmer: the restart their game has dealt with, so they go to bed once per restart.</summary>
    internal const string RestartSeenKey = "CarbonNode.SharedDay/restartSeen";
    // 0.1.35: Troubleshooting, and "Give a farmer a fresh day" (see GiveFreshDay and FreshDayMark).
    private bool toolsQueued;
    private bool toolsPasswordQueued;
    private long? freshConfirmFor;
    /// <summary>The farm's password as this player typed it for Troubleshooting, kept for the day so each tool does not ask again.</summary>
    private string? toolsSecret;
    private int toolsDay = -1;
    /// <summary>Host: farmers given a fresh day whose 6:00 AM has not started yet; their game puts them in bed when it does.</summary>
    private readonly HashSet<long> freshDayDue = new();
    /// <summary>On the host's own farmer: whose fresh day started this morning (FreshDayMark). Every game on the farm reads it.</summary>
    internal const string FreshKey = "CarbonNode.SharedDay/fresh";
    /// <summary>On each farmer: the fresh day their game has dealt with, so they go to bed once for it.</summary>
    internal const string FreshSeenKey = "CarbonNode.SharedDay/freshSeen";
    private bool ToolsUnlocked => Context.IsMainPlayer || (toolsSecret is not null && Context.IsWorldReady && toolsDay == Game1.Date.TotalDays);
    private bool Bypassed => Context.IsWorldReady && bypassDay == Game1.Date.TotalDays;
    private string? BypassSalt => Context.IsMainPlayer ? Config.Bypass.Salt : status?.BypassSalt;
    private string? BypassHash => Context.IsMainPlayer ? Config.Bypass.Hash : status?.BypassHash;
    // Held too (0.1.29): a farmer asked to join a turn or wait, one who chose to wait, one frozen
    // for a save of the turn, and one still being shown the nights they missed.
    private bool ShouldLockLocal => !Released && !Bypassed && !NightUnderWay && Game1.player?.isCustomized.Value != false && ((LocalFinished && !LocalAway) || error is not null
        || status?.Error is not null || (!Context.IsMainPlayer && status is null) || LocalChoosing || LocalWaiting || holdActive || (nights is not null && nights.Busy));
    // The night belongs to the game: its shipping totals, level-ups, save and "waiting for
    // players" screens each have to run to their own end, and every player's game has to tell
    // the host it has woken. The lock closes any menu that is not Shared Day's, and a farmhand
    // is locked from the moment the new date starts until the host's first word about it. When
    // the host was quicker than the player (a host nobody sits at always is), that closed the
    // player's totals half way through, their game never reported in, and the host waited on
    // its save screen for good.
    private static bool NightUnderWay => Game1.newDay || Game1.showingEndOfNightStuff || Game1.newDaySync.hasInstance()
        || Game1.activeClickableMenu is ShippingMenu or SaveGameMenu or LevelUpMenu or ReadyCheckDialog;
    internal void QueueWaitingMenu() => waitingMenuQueued = true;
    /// <summary>This PC loaded its farm by itself and its own farmer plays no turn: nobody is sitting at it.</summary>
    internal bool Unattended => Config.AutoHost.Autopilot && AutoHost.Hosted && !Config.IncludeHost && !HostPresent;
    // Real presses on this PC's keyboard, mouse or controller. Nothing Shared Day does for an
    // empty seat makes one, so this is a person. Ten minutes: long enough to read a letter or
    // fetch a drink, short next to the fifteen the farm waits before it sleeps.
    private static readonly TimeSpan SeatTime = TimeSpan.FromMinutes(10);
    private DateTime lastHostInput;
    internal bool HostPresent => Context.IsMainPlayer && DateTime.UtcNow - lastHostInput < SeatTime;
    /// <summary>This PC loaded its farm by itself and its farmer is not a standing player: a seat somebody has to take on purpose.</summary>
    private bool SelfHostedFarm => Context.IsMainPlayer && AutoHost.Hosted && !Config.IncludeHost;

    /// <summary>The empty seat says how to take it, so a person at the host PC is not left pressing keys that do nothing.</summary>
    private void OnSeatHint(object? sender, RenderedHudEventArgs e)
    {
        // SeatKey None: this PC is only ever the server, so there is no seat to offer.
        if (!Active || !Context.IsWorldReady || !SelfHostedFarm || HostPresent || Config.SeatKey == SButton.None || Game1.activeClickableMenu is not null || Game1.eventUp) return;
        IClickableMenu.drawHoverText(e.SpriteBatch, $"This farm is running by itself. Press {Config.SeatKey} to play as {Game1.player.Name}.",
            Game1.smallFont, overrideX: 16, overrideY: Game1.uiViewport.Height - 96);
    }

    /// <summary>The host's own farmer takes turns: always (IncludeHost), or because somebody is playing at this PC right now.</summary>
    private bool HostPlays => Config.IncludeHost || HostPresent;

    // ---- for other mods, and Many Farms' picker (see Api.cs) ----

    public override object? GetApi() => new SharedDayApi(this);

    /// <summary>The host's own farmer is one of the farm's players; see <see cref="HostSeat.Plays"/>. Only the host can tell.</summary>
    internal bool HostFarmerPlays => !Active || !Context.IsMainPlayer
        || HostSeat.Plays(Config.IncludeHost, HostPresent, IsRequired(Game1.player.UniqueMultiplayerID));

    /// <summary>This player is held where they stand (their day is done, or the farm has stopped on an error); see the API.</summary>
    internal bool HoldsPlayer => Active && ShouldLockLocal;

    /// <summary>Many Farms' API, when Many Farms is installed and has one.</summary>
    private IManyFarmsApi? farms;
    private bool farmsFailed;
    private bool farmPickerQueued;
    /// <summary>Another mod's menu Shared Day opened from its own (the farm picker): the lock leaves it open, as it leaves Shared Day's.</summary>
    private IClickableMenu? guestMenu;
    internal bool HasFarmPicker => farms is not null && !farmsFailed;

    /// <summary>What the turn menu calls the farm picker right now, or null when it is not on offer.</summary>
    private string? FarmPickerChoice()
    {
        if (!HasFarmPicker || Released || error is not null) return null;
        try { return farms!.CanOpenFarmPicker && farms.FarmPickerLabel is { Length: > 0 } label ? label : null; }
        catch (Exception ex) { FarmsFailed(ex); return null; }
    }

    private void FarmsFailed(Exception ex)
    {
        farmsFailed = true;
        Monitor.Log("Shared Day could not reach Many Farms' farm picker and leaves it out of its menu. " + ex.Message, LogLevel.Warn);
    }

    /// <summary>
    /// "maybe this screen can give you option to add your own farm at any point?" (the owner,
    /// 2026-10-07). Opens Many Farms' picker from the turn menu. A player whose day is done is held
    /// by the lock, which closes every menu but Shared Day's own: the picker is let through as a
    /// guest, and the waiting menu comes back when it closes.
    /// </summary>
    private void OpenFarmPicker()
    {
        if (!HasFarmPicker) return;
        bool opened = false;
        try { opened = farms!.OpenFarmPicker(); }
        catch (Exception ex) { FarmsFailed(ex); }
        if (opened) guestMenu = Game1.activeClickableMenu;
        else if (ShouldLockLocal) waitingMenuQueued = true;
    }

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();
        notifier = new TurnNotifier(Monitor, () => Config.Notifications);
        tracker = new TurnTracker(Monitor, () => Config.TurnTracker, ModManifest.Version.ToString());
        portraits = new FarmerPortraits(Monitor);
        Wallets = new Wallets(helper, Monitor, ModManifest.Version.ToString());
        helper.Events.GameLoop.GameLaunched += OnLaunched;
        helper.Events.GameLoop.SaveLoaded += OnLoaded;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.Saved += OnSaved;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        // The host credits absent farmers' nights inside the one real night, before the game's own night work.
        helper.Events.GameLoop.DayEnding += OnDayEnding;
        helper.Events.GameLoop.ReturnedToTitle += OnTitle;
        // The bed's question, reworded for a turn (see BedQuestion).
        helper.Events.Content.AssetRequested += OnAssetRequested;
        nights = new NightReplay(Monitor, helper, NightsShown);
        helper.Events.GameLoop.UpdateTicking += OnBeforeTick;
        helper.Events.GameLoop.UpdateTicked += OnTick;
        helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;
        helper.Events.Multiplayer.ModMessageReceived += OnMessage;
        helper.Events.Input.ButtonPressed += OnButton;
        // Moving the turn board: the press picks it up, the pointer carries it, letting go saves it.
        helper.Events.Input.ButtonReleased += OnButtonReleased;
        helper.Events.Input.CursorMoved += OnCursorMoved;
        helper.Events.Display.WindowResized += OnWindowResized;
        // The title-screen button and its hub work whether or not Shared Day is enabled: looking
        // at the farm and joining it are harmless (a host running Shared Day still turns away a
        // player whose Shared Day is off).
        Hub = new TitleHub(helper, Monitor, () => Config);
        JoinButton = new TitleJoinButton(helper, Monitor, () => Hub.Link, Hub);
        Hub.Button = JoinButton;
        // The turn board on screen while playing.
        TurnHud = new TurnHud(Monitor, () => Config.Hud, BoardNow, () => Context.IsWorldReady ? ShortDate() : null, NotesUnread);
        helper.Events.Display.RenderedHud += TurnHud.OnRenderedHud;
        helper.Events.Display.RenderedHud += OnSeatHint;
        // Shared Day's own popup (waiting, your turn, skip) wears the farm's faces above it.
        // And the whole screen behind any of them is Shared Day's backdrop, not the world.
        helper.Events.Display.RenderingActiveMenu += (_, e) =>
        {
            if (Active && Game1.activeClickableMenu is { } open && (ReferenceEquals(open, waitingMenu) || ReferenceEquals(open, ownPopup))) TurnHud.DrawBackdrop(e.SpriteBatch);
        };
        helper.Events.Display.RenderedActiveMenu += (_, e) =>
        {
            if (OwnsWaitingMenu && Game1.activeClickableMenu is DialogueBox { isQuestion: true, transitioning: false } popup) TurnHud.DrawOverPopup(e.SpriteBatch, popup);
        };
        helper.Events.GameLoop.UpdateTicked += Hub.OnUpdateTicked;
        helper.Events.GameLoop.UpdateTicked += JoinButton.OnUpdateTicked;
        helper.Events.Display.RenderedActiveMenu += JoinButton.OnRendered;
        helper.Events.Input.ButtonPressed += JoinButton.OnButtonPressed;
        // A dedicated host PC loads its farm by itself (AutoHost in config.json); off unless asked for.
        AutoHost = new TitleAutoHost(helper, Monitor, () => Config.AutoHost);
        helper.Events.GameLoop.UpdateTicked += AutoHost.OnUpdateTicked;
        helper.Events.GameLoop.SaveLoaded += AutoHost.OnSaveLoaded;
        // A farm AutoHost loaded has nobody to press OK on the night's menus; see HostAutopilot.
        autopilot = new HostAutopilot(helper, Monitor, () => Unattended, () => waitingMenu);
        helper.Events.Specialized.UnvalidatedUpdateTicked += autopilot.OnUpdateTicked;
        helper.ConsoleCommands.Add("shared_day", "Shared Day: status | skip <farmer> | reopen <farmer> | unskip <farmer> | fresh_day <farmer> | bar <farmer> | unbar <farmer> | free_cabin <farmer> | mode Relay|Together | checkpoint | retry | restart_day", OnCommand);
    }

    private void OnLaunched(object? sender, GameLaunchedEventArgs e)
    {
        // The board and its page in Mod Options work whether or not the relay itself is enabled.
        ConfigMenu.Register(Helper, ModManifest, () => Config, () => Helper.WriteConfig(Config), Monitor);
        // The join screen is tidied whether or not the relay is enabled, like the Join button: it
        // changes only what this PC draws.
        JoinListTidy.Install(new Harmony(ModManifest.UniqueID), Monitor, () => Config.TidyJoinList);
        // Many Farms is optional and so is its API: one too old to have it leaves this null.
        try { farms = Helper.ModRegistry.GetApi<IManyFarmsApi>("CarbonNode.ManyFarms"); }
        catch (Exception ex) { Monitor.Log("Shared Day could not read Many Farms' API and offers no farm choice in its menu. " + ex.Message, LogLevel.Warn); }
        // The name of a farmer's own farm for the nights shown to them: a second, separate read, so
        // a Many Farms without it costs only the name (see IManyFarmsNightsApi).
        if (farms is not null)
        {
            try { if (Helper.ModRegistry.GetApi<IManyFarmsNightsApi>("CarbonNode.ManyFarms") is { } nightsApi) FarmLabelFor = id => nightsApi.FarmLabelFor(id); }
            catch (Exception ex) { Monitor.Log("Many Farms does not name farms for the nights Shared Day shows. " + ex.Message, LogLevel.Debug); }
        }
        if (!Config.Enabled) return;
        try
        {
            GamePatches.Install(new Harmony(ModManifest.UniqueID), Helper.ModRegistry.IsLoaded("JunimoHost.Server"));
            Monitor.Log("Shared Day guards installed; experimental relay is enabled. Use a disposable test farm.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            patchError = "Shared Day could not install its day guards. " + ex.Message;
            Fail(patchError);
        }
    }

    private IEnumerable<FarmerIdentity> Roster()
    {
        var all = Game1.getAllFarmers().Where(f => f.UniqueMultiplayerID != Game1.MasterPlayer.UniqueMultiplayerID || HostPlays).ToArray();
        // Every cabin holds a placeholder farmer until someone claims it. Nobody plays an
        // empty cabin, so the default roster leaves those out (an explicit list is kept as given).
        if (Config.RequiredPlayerIds.Length == 0) all = all.Where(f => !f.isUnclaimedFarmhand).ToArray();
        if (Config.RequiredPlayerIds.Length > 0)
        {
            var missing = Config.RequiredPlayerIds.Except(all.Select(f => f.UniqueMultiplayerID)).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException("Unknown required farmer IDs: " + string.Join(", ", missing));
            all = all.Where(f => Config.RequiredPlayerIds.Contains(f.UniqueMultiplayerID)).ToArray();
        }
        // A farmer the group removed is on no roster made from now on (see Barring).
        if (barred.Count > 0) all = all.Where(f => !barred.Contains(f.UniqueMultiplayerID)).ToArray();
        return all.Select(f => new FarmerIdentity(f.UniqueMultiplayerID, f.Name));
    }

    private void OnLoaded(object? sender, SaveLoadedEventArgs e)
    {
        // A disabled client still announces itself, so an enabled host can turn it away
        // at once instead of waiting out the hello grace. A vanilla host ignores this.
        if (!Config.Enabled && Context.IsWorldReady && !Context.IsMainPlayer) { SendHello(); return; }
        if (!Active) return;
        error = patchError;
        if (patchError is not null)
        {
            Monitor.Log(patchError + " Return to the title screen; this farm cannot run with missing day guards.", LogLevel.Error);
            Game1.ExitToTitle(null);
            return;
        }
        sleeping = false;
        pendingSleepHome = null;
        status = null;
        coordinator = null;
        calendarClock = null;
        checkpointError = null;
        checkpointFailures = clockFaults = 0;
        checkpointRetryAt = default;
        nights.Reset();
        parkedResumeDone = previousChoosing = false;
        holdActive = holdAnswered = false;
        if (!Context.IsMainPlayer) { SendHello(); return; }
        barred = Barring.Read();
        newFarmersAllowed = Barring.ReadNewFarmers();
        freeCabinFailed.Clear();
        resumedOwnClock.Clear();
        freshDayDue.Clear();
        nightsSentAt.Clear();
        lastSeen.Clear();
        kicksDue.Clear();
        creditedDay = -1;
        heldSave.End();
        lastHeldSaveMinute = -1;
        walletsDue = true;
        walletsDayStarted = false;
        walletsTicks = 0;
        LoadHostDay();
        // The day could not be loaded: nothing was tried, and ModRelay is told so. "Try again"
        // never changes the wallets: only a farm that loaded cleanly does.
        if (error is not null || coordinator is null)
        {
            walletsDue = false;
            Wallets.NotTried(AutoHost.Hosted, Config.IncludeHost, ShortDate(), WalletPlan.HeldFailed);
        }
    }

    /// <summary>The host reads today's turns from the loaded farm. Run again by "Try again" when it failed.</summary>
    private void LoadHostDay()
    {
        try
        {
            // The day's backup is a comfort, not a condition: a full disk must not stop the farm.
            try { WorldCheckpoint.EnsureBackup(Helper.DirectoryPath); }
            catch (Exception ex) { Monitor.Log("Shared Day could not make today's backup copy of the farm and carries on without it. " + ex.Message, LogLevel.Warn); }
            WorldCheckpoint.BindOfflineWallets();
            var saved = WorldCheckpoint.Read();
            if (saved is null)
            {
                coordinator = DayCoordinator.Create(Constants.SaveFolderName!, Game1.Date.TotalDays, Roster(), Config.Mode);
                calendarClock = new CalendarClock(Game1.Date.TotalDays, CalendarClock.ToMinutes(Game1.timeOfDay));
            }
            else
            {
                var recovered = CheckpointRecovery.Restore(Constants.SaveFolderName!, Game1.Date.TotalDays,
                    saved.Journal, saved.Clock, Roster(), Math.Max(0, Config.AwayAfterDays));
                coordinator = recovered.Coordinator;
                calendarClock = recovered.Clock;
            }
            coordinator.AbsenceLimit = Math.Max(0, Config.AwayAfterDays);
            if (saved?.IsMidday == true)
            {
                WorldCheckpoint.RestoreShipping();
                SetClock(saved.TimeOfDay);
            }
            turnStartedAt = DayClock.IsValid(Game1.timeOfDay) ? Game1.timeOfDay : 600;
            if (coordinator.Snapshot.Mode == PlayMode.Relay) NpcMorning(NpcMorningReplay.EnsureMorningBaseline, replay: false);
            // A same-date checkpoint loads part way through the day with the villagers where the save left them, mid-route: bring them up to its clock (0.1.33).
            if (saved?.IsMidday == true && Game1.timeOfDay > 600) CatchUpVillagers(Game1.timeOfDay);
            if (Config.IncludeHost && IsRequired(Game1.player.UniqueMultiplayerID)) Join(Game1.player.UniqueMultiplayerID);
            DedicatedHost.Apply(Unattended, Monitor);
            LoadSkipVotes();
            Broadcast();
        }
        catch (Exception ex) { Fail("Unable to load Shared Day: " + ex.Message); }
    }

    /// <summary>
    /// Every update tick on the host while a load's wallets are still due. The wallet change writes
    /// a full save, and a save raises Saving and Saved for every mod on the farm. Doing that from
    /// inside Shared Day's own SaveLoaded handler would reach mods that have not had their own
    /// SaveLoaded or DayStarted yet, and a mod that keeps its state in memory would then write
    /// nothing into the very save that carries the money. So the wallets wait here: for DayStarted,
    /// for a second of ordinary ticks, and for the screen to be free, exactly where every other
    /// same-date save is made (<see cref="TryCheckpoint"/>). No farmhand is put into the day before
    /// this has run, so the money is still only the save's.
    /// </summary>
    private void WalletsWhenSettled()
    {
        if (!walletsDue) return;
        // Nobody asked: nothing is read, nothing waits.
        if (!Wallets.Listening) { walletsDue = false; Wallets.Reset(); return; }
        if (error is not null || coordinator is null || calendarClock is null)
        {
            walletsDue = false;
            Wallets.NotTried(AutoHost.Hosted, Config.IncludeHost, ShortDate(), WalletPlan.HeldFailed);
            return;
        }
        if (!walletsDayStarted || ++walletsTicks < WalletsSettleTicks) return;
        bool busy = Game1.eventUp || Game1.newDay || savingCheckpoint || Released
            || (Game1.activeClickableMenu is not null && Game1.activeClickableMenu != waitingMenu);
        if (busy)
        {
            if (walletsTicks < WalletsGiveUpTicks) return;
            walletsDue = false;
            Wallets.NotTried(AutoHost.Hosted, Config.IncludeHost, ShortDate(), WalletPlan.HeldBusy);
            return;
        }
        walletsDue = false;
        if (Game1.activeClickableMenu is not null && Wallets.WouldChange)
        {
            // Shared Day's own waiting menu: closed for the save and put back, as a checkpoint does.
            Game1.exitActiveMenu();
            waitingMenu = null;
            waitingMenuQueued = true;
        }
        Wallets.AtLoad(AutoHost.Hosted, Config.IncludeHost, coordinator.Snapshot, ShortDate(), SaveForWallets);
        Broadcast();
    }

    /// <summary>The wallets have just changed: the money and the record of it go to disk together, now, or the change is undone.</summary>
    private void SaveForWallets()
    {
        if (coordinator is null || calendarClock is null) throw new InvalidOperationException("Today's turns are not loaded.");
        savingCheckpoint = true;
        try
        {
            WorldCheckpoint.Save(Helper, Monitor, coordinator.ExportJournal(), calendarClock.ExportState());
            checkpointPending = false;
        }
        finally { savingCheckpoint = false; }
    }

    /// <summary>
    /// Once a second on the host: is somebody playing at this PC, and does today's roster say so?
    /// See <see cref="HostSeat"/>. On a PC whose farmer always plays (IncludeHost) it does nothing.
    /// </summary>
    private void TendHostSeat()
    {
        if (coordinator is null || error is not null || Config.IncludeHost) return;
        long me = Game1.player.UniqueMultiplayerID;
        var day = coordinator.Snapshot;
        var mine = day.Farmers.FirstOrDefault(f => f.Id == me);
        bool present = HostPresent;
        try
        {
            string? message = null;
            switch (HostSeat.Decide(Config.IncludeHost, present, day.Phase == DayPhase.Open, mine))
            {
                case HostSeatAction.AddAndJoin:
                    if (string.IsNullOrWhiteSpace(Game1.player.Name)) return;
                    coordinator.AddFarmer(new FarmerIdentity(me, Game1.player.Name));
                    Join(me);
                    message = $"{Game1.player.Name} is playing at the host PC and takes a turn on {ShortDate()}.";
                    break;
                case HostSeatAction.Join:
                    Join(me);
                    break;
                case HostSeatAction.BringBack:
                    // Join says who is back.
                    Join(me);
                    break;
                case HostSeatAction.StepOut:
                    // Away, not skipped: the others stop waiting, but an empty seat ends no day by itself.
                    coordinator.StepAway(me);
                    message = $"Nobody has been at the host PC for {SeatTime.TotalMinutes:0} minutes, so the others do not wait for {mine!.Name} on {ShortDate()}. They are back in it when they return.";
                    break;
                default:
                    // Present or not is also what the farm's own list of who is on it goes by:
                    // a farmer whose turn is done and whose seat is empty is not on the farm.
                    if (mine is null) return;
                    if (present && !mine.IsOnline && day.Phase == DayPhase.Open) coordinator.Join(me);
                    else if (!present && mine.IsOnline) coordinator.Disconnect(me);
                    else return;
                    break;
            }
            if (message is not null)
            {
                Monitor.Log(message, LogLevel.Info);
                Game1.chatBox?.addInfoMessage(message);
            }
            checkpointPending = true;
            Broadcast();
        }
        catch (DayCoordinationException ex) { Monitor.Log("Could not seat the host's farmer: " + ex.Message, LogLevel.Warn); }
    }

    private bool IsRequired(long id) => coordinator?.Snapshot.Farmers.Any(f => f.Id == id) == true;

    // With the default roster, a farmhand who is not on today's roster can only be someone
    // who claimed a cabin after the day began. They join today's turn instead of being sent away.
    // The group's turn tracker can close the farm to new farmers (0.1.29, see Barring).
    private bool CanJoinAsNewFarmer(long id) => Config.RequiredPlayerIds.Length == 0 && coordinator is not null && newFarmersAllowed
        && coordinator.Snapshot.Phase == DayPhase.Open && id != Game1.MasterPlayer.UniqueMultiplayerID && !barred.Contains(id);

    // A friend in character creation has passed the handshake but is not on the roster yet.
    // The others keep playing meanwhile; the day still cannot end until they have finished.
    private bool IsCreatingFarmer(long id) => !incompatiblePeers.Contains(id) && !IsRequired(id) && CanJoinAsNewFarmer(id)
        && Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id) is { isUnclaimedFarmhand: true };

    private bool LoadingIn => pendingJoins.Any(id => !IsCreatingFarmer(id));

    /// <returns>False while the farmer is still in character creation.</returns>
    private bool TryAddNewFarmer(long id)
    {
        var farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id);
        if (farmer is null || farmer.isUnclaimedFarmhand || string.IsNullOrWhiteSpace(farmer.Name)) return false;
        try
        {
            if (!coordinator!.AddFarmer(new FarmerIdentity(id, farmer.Name))) return true;
            checkpointPending = true;
            string message = $"{farmer.Name} joined the farm and plays {ShortDate()} now.";
            Monitor.Log(message, LogLevel.Info);
            Game1.addHUDMessage(new HUDMessage(message));
            notifier.Joined(FarmName, ShortDate(), farmer.Name);
            return true;
        }
        catch (DayCoordinationException ex)
        {
            Monitor.Log($"Could not add {farmer.Name} to {ShortDate()}: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private void Join(long id)
    {
        if (error is not null || coordinator is null || !IsRequired(id)) return;
        try
        {
            var before = coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == id);
            bool wasAway = before is { Away: true };
            bool wasInTurn = before is { IsInCurrentSession: true };
            var joined = coordinator.Join(id);
            if (wasAway && coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == id) is { Away: false, Status: FarmerStatus.Pending } back)
            {
                string message = back.AbsentDays > 0 ? $"{back.Name} is back after {back.AbsentDays} days away and plays {ShortDate()}."
                    : $"{back.Name} is back and plays {ShortDate()}.";
                Monitor.Log(message, LogLevel.Info);
                Game1.chatBox?.addInfoMessage(message);
            }
            AfterPlacement(id, joined, wasInTurn);
            // What a change of wallets did to this farmer's own money, the first time they are back.
            Wallets.Joined(id, Tell);
            // The nights that ran while they were away, for their own game to show before their turn.
            if (id != Game1.player.UniqueMultiplayerID) SendNights(id);
            else coordinator.AcknowledgeNightsShown(id, coordinator.Snapshot.AbsoluteDay - 1);
            checkpointPending = true;
        }
        catch (Exception ex) { Fail("Unable to start the farmer's session. Reload the last checkpoint. " + ex.Message); }
    }

    /// <summary>
    /// What a farmer's placement means for the clock and the villagers, and the line they read
    /// about it. A new Relay turn starts at morning with the villagers' morning put back; a
    /// resumed turn (0.1.29) goes back to the clock the farmer left it at, with the villagers left
    /// where they are, as for a morning that cannot be replayed exactly.
    /// </summary>
    private void AfterPlacement(long id, JoinResult joined, bool wasInTurn)
    {
        if (coordinator is null) return;
        var mode = coordinator.Snapshot.Mode;
        bool freshStarted = false;
        // Initial load may be a resumed checkpoint. Only subsequent cohorts rewind the clock.
        if (joined.StartsNewSession && joined.SessionNumber > 1 && mode == PlayMode.Relay)
        {
            SetClock(600);
            turnStartedAt = 600;
            NpcMorning(NpcMorningReplay.RestoreMorning, replay: true);
            DedicatedHost.NewTurn();
        }
        else if (joined.ResumesOwnClock && mode == PlayMode.Relay
            && coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == id)?.TimeReached is { } morning && FreshDayMark.IsMorning(morning))
        {
            // 0.1.35: a turn parked at 6:00 AM (a fresh day, or a farmer who left at 6:00) is a
            // new morning, started exactly as a new turn is: catching the villagers up to a clock
            // that went backwards would leave them wherever the last turn had walked them, with
            // their schedule keys already spent.
            SetClock(600);
            turnStartedAt = 600;
            NpcMorning(NpcMorningReplay.RestoreMorning, replay: true);
            DedicatedHost.NewTurn();
            var mine = coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == id);
            if (freshDayDue.Remove(id))
            {
                freshStarted = true;
                Game1.player.modData.TryGetValue(FreshKey, out string had);
                Game1.player.modData[FreshKey] = FreshDayMark.Add(had, Game1.Date.TotalDays, id, coordinator.Snapshot.Revision);
                Monitor.Log($"{mine?.Name ?? id.ToString()}'s fresh day starts: 6:00 AM, turn {joined.SessionNumber}, villagers back at their morning.", LogLevel.Info);
            }
            else Monitor.Log($"{mine?.Name ?? id.ToString()} carries on at 6:00 AM, where they left their turn; the villagers' morning is put back as for a new turn.", LogLevel.Info);
        }
        else if (joined.ResumesOwnClock && mode == PlayMode.Relay)
        {
            var mine = coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == id);
            int time = mine?.TimeReached is { } parked ? parked : Game1.timeOfDay;
            SetClock(time);
            turnStartedAt = time;
            resumedOwnClock.Add(id);
            Monitor.Log($"{mine?.Name ?? id.ToString()} carries on at {DayClock.Words(time)}, where they left their turn. Their morning is not restored for a resumed turn; the villagers are brought up to that clock instead.", LogLevel.Info);
            // 0.1.33: the clock jumped, so every villager goes to where their schedule has them by now (the owner, 2026-10-08: villagers "stuck up there").
            CatchUpVillagers(time);
        }
        else if (joined.Disposition == JoinDisposition.ResumeSession) resumedOwnClock.Add(id);
        // "i think it should warn about a midday join" (the owner, 2026-10-07). The warning is before
        // the join (the title-screen hub and the launcher); this is the line after it, from the
        // host, which alone knows where it put the farmer and what its clock said.
        if (freshStarted) Tell(id, $"Your fresh {ShortDate()} starts now: 6:00 AM, in your own bed. What you did earlier today is still on the farm.");
        else if (MiddayJoin.After(joined.Disposition, wasInTurn, mode, Game1.timeOfDay) is { } line) Tell(id, line);
    }

    /// <summary>
    /// The nights that ran while a farmer was away (see AbsentNights), sent to their own game to
    /// show before their turn starts. With nothing to show, nothing is owed. The clock is held
    /// for them until their game says it has shown them (or for ninety seconds, for a game that
    /// never answers).
    /// </summary>
    private void SendNights(long id)
    {
        // Once per admission: a second hello before the first status reaches them joins them again.
        if (coordinator is null || nightsSentAt.ContainsKey(id)) return;
        try
        {
            var farmer = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id);
            var owed = farmer is null ? new List<NightRecord>() : NightsLedger.Unshown(NightsLedger.Read(), farmer);
            if (owed.Count == 0)
            {
                coordinator.AcknowledgeNightsShown(id, coordinator.Snapshot.AbsoluteDay - 1);
                nightsSentAt.Remove(id);
                return;
            }
            Helper.Multiplayer.SendMessage(new NightsMessage { Day = coordinator.Snapshot.AbsoluteDay, Records = owed }, "nights", new[] { ModManifest.UniqueID }, new[] { id });
            nightsSentAt[id] = DateTime.UtcNow;
            Monitor.Log($"Sent {farmer!.Name} {owed.Count} missed night{(owed.Count == 1 ? "" : "s")} to show before their turn: {string.Join(", ", owed.Select(r => DateWords(r.Day) + "'s"))}.", LogLevel.Info);
        }
        catch (Exception ex) { Monitor.Log("Could not send a farmer the nights they missed: " + ex.Message, LogLevel.Warn); }
    }

    /// <summary>A game that never says it has shown its nights (an older Shared Day) must not hold the clock for ever.</summary>
    private void ExpireNightHolds()
    {
        if (coordinator is null) return;
        foreach (var (id, sent) in nightsSentAt.ToArray())
        {
            if (DateTime.UtcNow - sent < NightsPatience) continue;
            nightsSentAt.Remove(id);
            try
            {
                if (coordinator.AcknowledgeNightsShown(id, coordinator.Snapshot.AbsoluteDay - 1))
                    Monitor.Log($"Farmer {id}'s game never said it had shown them the nights they missed; the clock no longer waits for it.", LogLevel.Warn);
            }
            catch (DayCoordinationException) { }
        }
    }

    /// <summary>This player's game has shown them an old night: written down as theirs, and the host told.</summary>
    private void NightsShown(int day)
    {
        try
        {
            Game1.player.modData[NightsLedger.LastShownKey] = day.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!Context.IsMainPlayer)
                Helper.Multiplayer.SendMessage(new NightsShownMessage { ThroughDay = day }, "nightsshown", new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
            // The replay held this player; the popup it kept shut is not wanted once their turn begins.
            if (!nights.Busy) waitingMenuQueued = false;
        }
        catch (Exception ex) { Monitor.Log("Could not write down the night shown: " + ex.Message, LogLevel.Warn); }
    }

    /// <summary>"Spring 13" for a day counted from Spring 1, Year 1 (0-based), the way ShortDate words today.</summary>
    internal static string DateWords(int absoluteDay)
    {
        int day = Math.Max(0, absoluteDay);
        return $"{Utility.getSeasonNameFromNumber(day / 28 % 4)} {day % 28 + 1}";
    }

    /// <summary>
    /// One line in one player's chat, from the host. The host's own farmer reads it here; anyone
    /// else gets it as a "note" message, which a Shared Day older than 0.1.28 ignores.
    /// </summary>
    internal void Tell(long id, string text)
    {
        if (!Context.IsMainPlayer || string.IsNullOrWhiteSpace(text)) return;
        string who = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id)?.Name is { Length: > 0 } name ? name : id.ToString();
        Monitor.Log($"Told {who}: {text}", LogLevel.Info);
        if (id == Game1.player.UniqueMultiplayerID) Game1.chatBox?.addInfoMessage(text);
        else Helper.Multiplayer.SendMessage(new NoteMessage { Text = text }, "note", new[] { ModManifest.UniqueID }, new[] { id });
    }

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        if (!Active || !Context.IsMainPlayer) return;
        // Installed does not mean enabled or protocol-compatible. Require a live hello.
        incompatiblePeers.Add(e.Peer.PlayerID);
        pendingJoins.Add(e.Peer.PlayerID);
        Broadcast();
    }

    private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        if (!Active || !Context.IsMainPlayer) return;
        pendingJoins.Remove(e.Peer.PlayerID);
        incompatiblePeers.Remove(e.Peer.PlayerID);
        unadmittedSince.Remove(e.Peer.PlayerID);
        rejectReasons.Remove(e.Peer.PlayerID);
        rejectedPeers.Remove(e.Peer.PlayerID);
        if (IsRequired(e.Peer.PlayerID))
        {
            // An unfinished turn is parked at this clock, and the spot they stood on kept with it.
            coordinator!.Disconnect(e.Peer.PlayerID, Game1.timeOfDay);
            ParkOnLeave(e.Peer.PlayerID);
        }
        resumedOwnClock.Remove(e.Peer.PlayerID);
        nightsSentAt.Remove(e.Peer.PlayerID);
        lastSeen.Remove(e.Peer.PlayerID);
        kicksDue.Remove(e.Peer.PlayerID);
        heldSave.Forget(e.Peer.PlayerID);
        emptyTicks = 0;
        checkpointPending = true;
        Broadcast();
    }

    /// <summary>Where a farmer who just left stood, written into their own modData for their return (see Parked).</summary>
    private void ParkOnLeave(long id)
    {
        if (coordinator is null) return;
        try
        {
            var s = coordinator.Snapshot;
            if (s.Farmers.FirstOrDefault(f => f.Id == id) is not { Status: FarmerStatus.Pending, TimeReached: not null } mine) return;
            var farmer = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id);
            if (farmer is null) return;
            // The game has already put the farmhand back in its cabin: the spot is the last one seen.
            var spot = lastSeen.TryGetValue(id, out var seen) ? seen : Parked.Capture(farmer, s.AbsoluteDay, s.SessionNumber);
            if (spot is null) { Monitor.Log($"{mine.Name} left their turn at {DayClock.Words(mine.TimeReached!.Value)}; where they stood is not known on this copy of them, so they come back where the game puts them.", LogLevel.Info); return; }
            spot.Day = s.AbsoluteDay;
            spot.Session = s.SessionNumber;
            if (DayClock.IsValid(Game1.timeOfDay)) spot.TimeOfDay = Game1.timeOfDay;
            int copies = Parked.WriteEverywhere(id, spot);
            Monitor.Log($"{mine.Name} left their turn at {Parked.Words(spot)}; they come back there (written on {copies} cop{(copies == 1 ? "y" : "ies")} of them).", LogLevel.Info);
        }
        catch (Exception ex) { Monitor.Log("Could not keep where a farmer left their turn: " + ex.Message, LogLevel.Warn); }
    }

    private void OnMessage(object? sender, ModMessageReceivedEventArgs e)
    {
        if (!Active || e.FromModID != ModManifest.UniqueID) return;
        if (Context.IsMainPlayer)
        {
            if (e.Type == "held")
            {
                var held = e.ReadAs<HeldMessage>();
                if (held.Token == heldSaveToken && heldSave.Acknowledge(e.FromPlayerID)) Monitor.Log("Every player is held still for the save.", LogLevel.Debug);
                return;
            }
            if (e.Type == "nightsshown")
            {
                var shownThrough = e.ReadAs<NightsShownMessage>();
                nightsSentAt.Remove(e.FromPlayerID);
                string shownWho = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == e.FromPlayerID)?.Name ?? e.FromPlayerID.ToString();
                Monitor.Log($"{shownWho}'s game has shown them the nights they missed through {DateWords(shownThrough.ThroughDay)}; the clock waits for it no longer.", LogLevel.Info);
                try { if (coordinator?.AcknowledgeNightsShown(e.FromPlayerID, shownThrough.ThroughDay) == true) { checkpointPending = true; Broadcast(); } }
                catch (DayCoordinationException) { }
                return;
            }
            if (e.Type == "health")
            {
                // Health is a plain field, so the host's copy of a farmhand only catches up on
                // occasional full syncs. Apply the client's report before any checkpoint uses it.
                var farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == e.FromPlayerID);
                if (farmer is not null && !rejectedPeers.Contains(e.FromPlayerID))
                    farmer.health = Math.Clamp(e.ReadAs<HealthMessage>().Health, 0, farmer.maxHealth);
                return;
            }
            if (e.Type == "hello")
            {
                var hello = e.ReadAs<HelloMessage>();
                if (hello.Protocol != ProtocolVersion || !hello.Enabled)
                {
                    incompatiblePeers.Add(e.FromPlayerID);
                    rejectReasons[e.FromPlayerID] = !hello.Enabled ? "their Shared Day is installed but disabled"
                        : $"they run Shared Day protocol {hello.Protocol}, the host runs {ProtocolVersion}";
                    Broadcast();
                    return;
                }
                if (barred.Contains(e.FromPlayerID))
                {
                    incompatiblePeers.Add(e.FromPlayerID);
                    rejectReasons[e.FromPlayerID] = "they were removed from this farm's group";
                    Broadcast();
                    return;
                }
                if (!IsRequired(e.FromPlayerID) && !CanJoinAsNewFarmer(e.FromPlayerID)) { Broadcast(); return; }
                incompatiblePeers.Remove(e.FromPlayerID);
                pendingJoins.Add(e.FromPlayerID);
                Broadcast();
            }
            if (e.Type == "stickynote")
            {
                // A farmer pins a note to the farm's board, or takes their own down (0.1.33).
                var note = e.ReadAs<StickyNoteRequest>();
                string who = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == e.FromPlayerID)?.Name ?? "A farmer";
                if (NoteBoard.Apply(e.FromPlayerID, who, note, ShortDate(), Monitor) is { } refused)
                    Helper.Multiplayer.SendMessage(new StickyNoteRefused { Reason = refused }, "stickynoterefused", new[] { ModManifest.UniqueID }, new[] { e.FromPlayerID });
                else Broadcast();
                return;
            }
            if (e.Type == "fix")
            {
                var fix = e.ReadAs<FixRequest>();
                // "Try again" is the one fix a paused farm still takes: it is how a pause ends.
                if (fix.Kind == "retry") { if (!incompatiblePeers.Contains(e.FromPlayerID)) RetryAfterHold(e.FromPlayerID); }
                else if (fix.Kind == "bypass")
                {
                    if (incompatiblePeers.Contains(e.FromPlayerID)) return;
                    if (BypassCode.Matches(Config.Bypass.Salt, Config.Bypass.Hash, fix.Secret)) ApplyBypass(e.FromPlayerID);
                    else Monitor.Log("A bypass arrived with the wrong password and was ignored.", LogLevel.Warn);
                }
                else if (fix.Kind == "restart")
                {
                    if (incompatiblePeers.Contains(e.FromPlayerID)) return;
                    if (!BypassCode.Matches(Config.Bypass.Salt, Config.Bypass.Hash, fix.Secret))
                    {
                        Monitor.Log("A restart of the day arrived with the wrong password and was ignored.", LogLevel.Warn);
                        Tell(e.FromPlayerID, "The day was not restarted: that is not the farm's password.");
                    }
                    else if (coordinator is not null && fix.Day != coordinator.Snapshot.AbsoluteDay)
                        Tell(e.FromPlayerID, "The day was not restarted: the farm has moved on to another day since you asked.");
                    else RestartToday(e.FromPlayerID);
                }
                else if (fix.Kind == "freshday")
                {
                    if (incompatiblePeers.Contains(e.FromPlayerID)) return;
                    if (!BypassCode.Matches(Config.Bypass.Salt, Config.Bypass.Hash, fix.Secret))
                    {
                        Monitor.Log("A fresh day for a farmer arrived with the wrong password and was ignored.", LogLevel.Warn);
                        Tell(e.FromPlayerID, "Nobody was given a fresh day: that is not the farm's password.");
                    }
                    else if (coordinator is not null && fix.Day != coordinator.Snapshot.AbsoluteDay)
                        Tell(e.FromPlayerID, "Nobody was given a fresh day: the farm has moved on to another day since you asked.");
                    else GiveFreshDay(e.FromPlayerID, fix.Target);
                }
                else if (error is not null || coordinator is null || incompatiblePeers.Contains(e.FromPlayerID)) return;
                else if (fix.Kind is "joinnow" or "wait") AnswerReturnChoice(e.FromPlayerID, fix.Kind);
                else if (fix.Kind == "savenow") BeginHeldSave(e.FromPlayerID, byRequest: true);
                else ApplyFix(e.FromPlayerID, fix);
                return;
            }
            if (e.Type == "voteskip")
            {
                if (error is null && coordinator is not null && !incompatiblePeers.Contains(e.FromPlayerID))
                    CastSkipVote(e.FromPlayerID, e.ReadAs<SkipVoteRequest>());
                return;
            }
            if (error is not null || e.Type != "finish" || coordinator is null || incompatiblePeers.Contains(e.FromPlayerID)) return;
            var request = e.ReadAs<FinishRequest>();
            if (request.Day != coordinator.Snapshot.AbsoluteDay || request.Session != coordinator.Snapshot.SessionNumber) return;
            // Never trust a claimed farmer ID in the payload: SMAPI supplies the actual sender.
            Finish(e.FromPlayerID);
        }
        else if (e.Type == "status" && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            var incoming = e.ReadAs<StatusMessage>();
            if (incoming.Protocol != ProtocolVersion) { Fail("The host uses a different Shared Day version. Install the same version on every player and the host."); return; }
            if (incoming.Day != Game1.Date.TotalDays) return;
            if (status is not null && incoming.Day == status.Day && incoming.Revision < status.Revision) return;
            status = incoming;
        }
        else if (e.Type == "stickynoterefused" && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            string reason = (e.ReadAs<StickyNoteRefused>().Reason ?? "").Trim();
            if (reason.Length is > 0 and <= 200)
            {
                if (Game1.activeClickableMenu is NotesMenu notes) notes.Refused(reason);
                else Game1.addHUDMessage(new HUDMessage(reason) { noIcon = true });
            }
        }
        else if (e.Type == "note" && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            // Only the host's word, and only as text: it is shown, never acted on.
            string text = (e.ReadAs<NoteMessage>().Text ?? "").Trim();
            if (text.Length is > 0 and <= 600) Game1.chatBox?.addInfoMessage(text);
        }
        else if (e.Type == "hold" && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            // Freeze for a save of the turn; answered from the tick (see TickHold).
            var hold = e.ReadAs<HoldMessage>();
            holdActive = true;
            holdAnswered = false;
            holdToken = hold.Token;
            holdTicks = 0;
            holdSince = DateTime.UtcNow;
        }
        else if (e.Type == "release" && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            holdActive = holdAnswered = false;
        }
        else if (e.Type == "nights" && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            var sent = e.ReadAs<NightsMessage>();
            Monitor.Log($"The host sent {sent.Records.Count} missed night{(sent.Records.Count == 1 ? "" : "s")} to show: {string.Join(", ", sent.Records.Select(r => DateWords(r.Day) + "'s"))}.", LogLevel.Info);
            nights.Queue(sent.Records);
        }
    }

    private void EnforceAdmission()
    {
        var online = Game1.getOnlineFarmers().ToDictionary(f => f.UniqueMultiplayerID);
        foreach (long id in incompatiblePeers.Concat(pendingJoins).Distinct().ToArray())
        {
            if (id == Game1.player.UniqueMultiplayerID || !online.TryGetValue(id, out var farmer) || rejectedPeers.Contains(id)) continue;
            if (!unadmittedSince.TryGetValue(id, out var since)) unadmittedSince[id] = since = DateTime.UtcNow;
            string? reason = rejectReasons.GetValueOrDefault(id);
            if (reason is null && barred.Contains(id)) reason = "they were removed from this farm's group";
            if (reason is null && incompatiblePeers.Contains(id))
            {
                var peer = Helper.Multiplayer.GetConnectedPlayer(id);
                reason = peer is { HasSmapi: false } ? "they are not running SMAPI with Shared Day"
                    : peer is not null && peer.GetMod(ModManifest.UniqueID) is null ? "they do not have Shared Day installed"
                    : !IsRequired(id) && !CanJoinAsNewFarmer(id) ? "they are not on today's roster (RequiredPlayerIds)"
                    : DateTime.UtcNow - since >= HelloGrace ? "their Shared Day never completed its handshake"
                    : null;
            }
            if (reason is null) continue;
            rejectedPeers.Add(id);
            string message = $"Shared Day disconnected {farmer.Name}: {reason}.";
            Monitor.Log(message + " Every player needs the host's Shared Day version installed and enabled.", LogLevel.Warn);
            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.error_type));
            if (barred.Contains(id)) KickLater(id);
            else Game1.server?.kick(id);
        }
        foreach (long id in unadmittedSince.Keys.Where(id => !incompatiblePeers.Contains(id) && !pendingJoins.Contains(id)).ToArray())
            unadmittedSince.Remove(id);
    }

    private void SendHello() => Helper.Multiplayer.SendMessage(new HelloMessage { Protocol = ProtocolVersion, Enabled = Config.Enabled }, "hello", new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });

    internal void RequestFinish()
    {
        if (!Active || Released || LocalFinished || error is not null || status?.Error is not null) return;
        if (Context.IsMainPlayer) Finish(Game1.player.UniqueMultiplayerID);
        else if (status is not null)
        {
            SendHealth(force: true);
            Helper.Multiplayer.SendMessage(new FinishRequest { Day = status.Day, Session = status.Session }, "finish", new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }
    }

    private void SendHealth(bool force)
    {
        if (Context.IsMainPlayer || !Context.IsWorldReady || Game1.player is null) return;
        if (!force && Game1.player.health == lastSentHealth) return;
        lastSentHealth = Game1.player.health;
        Helper.Multiplayer.SendMessage(new HealthMessage { Health = Game1.player.health }, "health",
            new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    private void Finish(long id)
    {
        if (error is not null || coordinator is null || !IsRequired(id)) return;
        try
        {
            if (coordinator.Complete(id))
            {
                checkpointPending = true;
                var day = coordinator.Snapshot;
                notifier.Finished(FarmName, ShortDate(), day.Farmers.First(f => f.Id == id).Name, day.Mode,
                    day.Farmers.Where(f => f.Status == FarmerStatus.Pending).Select(f => f.Name).ToList());
            }
            Broadcast();
        }
        catch (DayCoordinationException ex) { Monitor.Log("Rejected finish request: " + ex.Message, LogLevel.Warn); }
    }

    private void OnBeforeTick(object? sender, UpdateTickingEventArgs e)
    {
        if (!Active) return;
        // Native UpdateGameClock accumulates milliseconds before the guarded clock
        // method runs. Discard held time each frame so admitting a player cannot
        // immediately consume an interval earned while nobody could play.
        if (Context.IsMainPlayer && HoldClock) Game1.gameTimeInterval = 0;
        if (!ShouldLockLocal) return;
        // Close inventory/chest/shop menus before they can handle another action, including
        // menus opened after the lock began. Only our waiting menu remains interactive.
        if (Game1.activeClickableMenu is not null && Game1.activeClickableMenu != waitingMenu && Game1.activeClickableMenu != guestMenu && Game1.activeClickableMenu != nights.OwnMenu)
            Game1.exitActiveMenu();
        if (Game1.activeClickableMenu is not null) return;
        // A button already held when the turn finishes won't raise ButtonPressed again.
        // Suppress it before the game update so closing the waiting menu can't resume a tool.
        Game1.player.CanMove = false;
        wasLocked = true;
        foreach (var button in InputButtons)
            if (button != Config.MenuKey && Helper.Input.IsDown(button)) Helper.Input.Suppress(button);
    }

    private void OnTick(object? sender, UpdateTickedEventArgs e)
    {
        if (!Active) return;
        bool finishedNow = LocalFinished && !LocalAway && !Bypassed;
        if (bypassQueued && Game1.activeClickableMenu is null)
        {
            bypassQueued = false;
            AskBypass();
        }
        if (restartConfirmQueued && Game1.activeClickableMenu is null)
        {
            restartConfirmQueued = false;
            ShowRestartConfirm();
        }
        if (restartPasswordQueued && Game1.activeClickableMenu is null)
        {
            restartPasswordQueued = false;
            AskRestartPassword();
        }
        if (toolsPasswordQueued && Game1.activeClickableMenu is null)
        {
            toolsPasswordQueued = false;
            AskToolsPassword();
        }
        if (toolsQueued && Game1.activeClickableMenu is null)
        {
            toolsQueued = false;
            ShowToolsMenu();
        }
        if (freshConfirmFor is { } freshFor && Game1.activeClickableMenu is null)
        {
            freshConfirmFor = null;
            ShowFreshDayConfirm(freshFor);
        }
        if (finishedNow && !previousLocalFinished) waitingMenuQueued = true;
        previousLocalFinished = finishedNow;
        // The host's question to a farmer back with a parked turn opens by itself.
        bool choosingNow = LocalChoosing;
        if (choosingNow && !previousChoosing) waitingMenuQueued = true;
        previousChoosing = choosingNow;
        // Admission holds are transient. They must not leave a new player stuck in an
        // unsolicited menu once joining succeeds; actual failures still show themselves.
        string? currentError = error ?? (status?.HasFatalError == true ? status.Error : null);
        if (currentError is not null && currentError != previousStatusError && !OwnsWaitingMenu && !Bypassed) waitingMenuQueued = true;
        previousStatusError = currentError;
        if (!Released && skipMenuQueued && Game1.activeClickableMenu is null)
        {
            skipMenuQueued = false;
            ShowSkipMenu();
        }
        if (fixMenuQueued && Game1.activeClickableMenu is null)
        {
            fixMenuQueued = false;
            ShowFixMenu();
        }
        if (helpQueued && Game1.activeClickableMenu is null)
        {
            helpQueued = false;
            ShowHelp();
        }
        if (guestMenu is not null && !ReferenceEquals(Game1.activeClickableMenu, guestMenu))
        {
            // The farm picker closed. A player who is being held gets Shared Day's menu back.
            guestMenu = null;
            if (ShouldLockLocal && Game1.activeClickableMenu is null) waitingMenuQueued = true;
        }
        if (farmPickerQueued && Game1.activeClickableMenu is null)
        {
            farmPickerQueued = false;
            OpenFarmPicker();
        }
        if (finishConfirmQueued && Game1.activeClickableMenu is null)
        {
            finishConfirmQueued = false;
            ShowFinishConfirm();
        }
        if (!Released && waitingMenuQueued && Game1.activeClickableMenu is null && !nights.Busy)
        {
            waitingMenuQueued = false;
            ShowMenu();
        }
        AnnounceTurn();
        if (Context.IsMainPlayer && coordinator is not null)
        {
            CommitUnscheduledMachineTime();
            if (e.IsMultipleOf(60) && calendarClock is not null)
            {
                int furthest = calendarClock.HighWaterMinute;
                // One farmer's face a second, only while a tracker wants them.
                if (tracker.WantsPortraits) portraits.CaptureOne(coordinator.Snapshot.Farmers.Select(f => f.Id));
                tracker.Update(coordinator.Snapshot, coordinator.Snapshot.Mode, furthest / 60 * 100 + furthest % 60, error, portraits.For);
            }
            if (e.IsMultipleOf(60))
            {
                TendHostSeat();
                DedicatedHost.Apply(Unattended, Monitor);
            }
            if (!Released && coordinator.Snapshot.Farmers.Any(f => f.IsOnline && f.Status == FarmerStatus.Pending)) checkpointPending = true;
            EnforceAdmission();
            // Often: a farmer who leaves a second after joining must still be found where they stood.
            if (e.IsMultipleOf(10)) RememberWhereTheyStand();
            TickKicks();
            ApplyTrackerOrders();
            if (e.IsMultipleOf(60)) FreeBarredCabins();
            PromoteWaiters();
            ExpireNightHolds();
            TickHeldSave();
            if (e.IsMultipleOf(60)) TickPeriodicSave();
            WalletsWhenSettled();
            foreach (long id in pendingJoins.ToArray())
            {
                // Nobody is put into the day until the load's wallets have been looked at.
                if (error is not null || walletsDue) break;
                if (!Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == id)) continue;
                if (!IsRequired(id))
                {
                    if (!CanJoinAsNewFarmer(id)) { incompatiblePeers.Add(id); continue; }
                    // Wait for their Shared Day handshake, then for them to finish creating their farmer.
                    if (incompatiblePeers.Contains(id) || !TryAddNewFarmer(id)) continue;
                }
                if (incompatiblePeers.Contains(id)) continue;
                Join(id);
                pendingJoins.Remove(id);
                Broadcast();
            }
            if (error is null && !walletsDue && incompatiblePeers.Count == 0 && pendingJoins.Count == 0
                && coordinator.IsReadyToAdvance && coordinator.Snapshot.Phase == DayPhase.Open)
            {
                coordinator.BeginAdvance();
                // Native overnight must start after the furthest elapsed time today. Otherwise
                // ending a replay early would grant the same machine hours a second time at night.
                if (calendarClock is not null && CalendarClock.ToMinutes(Game1.timeOfDay) < calendarClock.HighWaterMinute)
                    SetClock(calendarClock.HighWaterMinute / 60 * 100 + calendarClock.HighWaterMinute % 60);
                // This payload is saved atomically WITH the farm by the native overnight save.
                WorldCheckpoint.Embed(coordinator.ExportJournal(), calendarClock!.ExportState(), false);
                Broadcast();
            }
            if (!Released && checkpointPending
                && !coordinator.Snapshot.Farmers.Any(f => f.IsOnline && f.Status == FarmerStatus.Pending)
                && !Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID))
            {
                if (++emptyTicks >= 120 && error is null && !walletsDue) TryCheckpoint();
            }
            else emptyTicks = 0;
            if (e.IsMultipleOf(120)) Broadcast();
        }
        if (e.IsMultipleOf(120) && !Context.IsMainPlayer && status is null) SendHello();
        SendHealth(force: false);
        TickBedWording();
        TickHold();
        nights.Tick();
        TickParkedResume();
        TickRestartBed();
        TickFreshBed();
        if (!Released && Game1.timeOfDay >= 2550 && !LocalFinished) RequestFinish();

        bool locked = ShouldLockLocal;
        if (locked)
        {
            if (!wasLocked && Game1.activeClickableMenu is not null && Game1.activeClickableMenu != waitingMenu && Game1.activeClickableMenu != guestMenu && Game1.activeClickableMenu != nights.OwnMenu)
                Game1.exitActiveMenu();
            Game1.player.CanMove = false;
            wasLocked = true;
        }
        else if (wasLocked)
        {
            Game1.player.CanMove = true;
            wasLocked = false;
        }
        // The night needs the screen: a farm picker left open would hold this player's walk to bed.
        if (Released && guestMenu is not null && ReferenceEquals(Game1.activeClickableMenu, guestMenu))
        {
            Game1.exitActiveMenu();
            guestMenu = null;
        }
        if (Released && waitingMenu is not null && Game1.activeClickableMenu == waitingMenu)
        {
            Game1.exitActiveMenu();
            waitingMenu = null;
            waitingMenuQueued = false;
        }
        if (!Released) RefreshWaitingMenu();
        if (pendingSleepHome is not null)
            ContinueNativeSleep();
        else if (Released && !sleeping && !Game1.newDay && Game1.activeClickableMenu is null && !Game1.eventUp)
            StartNativeSleep();
    }

    private void StartNativeSleep()
    {
        sleeping = true;
        int releasedDay = Game1.Date.TotalDays;
        try
        {
            var home = Utility.getHomeOfFarmer(Game1.player);
            void SleepAtHome()
            {
                try
                {
                    if (!Active || !Released || Game1.Date.TotalDays != releasedDay)
                    {
                        sleeping = false;
                        return;
                    }
                    if (Game1.currentLocation is not FarmHouse destination || destination.NameOrUniqueName != home.NameOrUniqueName)
                        throw new InvalidOperationException("The sleep warp did not reach this farmer's home.");
                    // NewDay only starts its fade when the farmer is actually in bed.
                    // Calling a home's Sleep_Yes from the farm can leave newDay=true
                    // without a fade and strand the other peers in their ready check.
                    var bed = destination.GetPlayerBedSpot();
                    Game1.player.Position = new Vector2(bed.X, bed.Y) * 64f;
                    pendingSleepHome = destination;
                    pendingSleepDay = releasedDay;
                    bedWaitTicks = 0;
                }
                catch (Exception ex) { Fail("Unable to enter native sleep. " + ex.Message); }
            }

            if (Game1.currentLocation.NameOrUniqueName == home.NameOrUniqueName)
                SleepAtHome();
            else
            {
                var request = Game1.getLocationRequest(home.NameOrUniqueName);
                request.OnWarp += SleepAtHome;
                Game1.warpFarmer(request, 5, 9, Game1.player.FacingDirection);
            }
        }
        catch (Exception ex) { Fail("Unable to reach this farmer's bed. " + ex.Message); }
    }

    private void ContinueNativeSleep()
    {
        if (Game1.newDay || Game1.Date.TotalDays != pendingSleepDay)
        {
            pendingSleepHome = null;
            return;
        }
        if (!Released || Game1.activeClickableMenu is not null || Game1.eventUp) return;
        if (bedWaitTicks++ == 0) return;
        // Runs after the next normal game update: Farmer.Update, rather than this mod,
        // must confirm the bed tile. This also prevents a last-ready, same-frame race.
        if (!Game1.player.isInBed.Value)
        {
            if (bedWaitTicks >= 120)
            {
                pendingSleepHome = null;
                Fail("A usable bed is required to advance. Reload the last checkpoint and check this farmer's bed.");
            }
            return;
        }
        var home = pendingSleepHome!;
        pendingSleepHome = null;
        try { home.answerDialogueAction("Sleep_Yes", Array.Empty<string>()); }
        catch (Exception ex) { Fail("Unable to enter native sleep. " + ex.Message); }
    }

    private void TryCheckpoint()
    {
        if (error is not null || coordinator is null || calendarClock is null || Released || savingCheckpoint
            || pendingJoins.Count > 0 || incompatiblePeers.Count > 0 || Game1.eventUp) return;
        if (DateTime.UtcNow < checkpointRetryAt) return;
        if (Game1.activeClickableMenu is not null)
        {
            if (Game1.activeClickableMenu != waitingMenu) return;
            Game1.exitActiveMenu();
            waitingMenu = null;
            waitingMenuQueued = true;
        }
        if (Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)) return;
        savingCheckpoint = true;
        try
        {
            WorldCheckpoint.Save(Helper, Monitor, coordinator.ExportJournal(), calendarClock!.ExportState());
            checkpointPending = false;
            checkpointError = null;
            checkpointFailures = 0;
            Monitor.Log("Same-date checkpoint written. " + Describe(), LogLevel.Info);
            Wallets.Report(coordinator.Snapshot, Config.IncludeHost, ShortDate());
        }
        catch (Exception ex)
        {
            // The farm in memory is whole and the last save on disk is untouched, so nothing is
            // gained by stopping everyone. Say it plainly, keep the day open and try again: the
            // night's own save does not go through this path and still writes everything.
            checkpointFailures++;
            checkpointError = ex.Message;
            int wait = Math.Min(300, 30 * checkpointFailures);
            checkpointRetryAt = DateTime.UtcNow.AddSeconds(wait);
            Monitor.Log($"Today's progress could not be saved yet (try {checkpointFailures}): {ex.Message} The farm keeps running; "
                + $"Shared Day tries again in {wait} seconds, and the night's save still writes everything. "
                + "Until one of them succeeds, closing the game loses what was done since the last save.", LogLevel.Error);
            for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
                Monitor.Log("  caused by: " + inner.Message, LogLevel.Error);
            Monitor.Log(ex.ToString(), LogLevel.Trace);
        }
        finally { savingCheckpoint = false; }
        Broadcast();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (!Config.Enabled || !Context.IsMainPlayer || coordinator is null || savingCheckpoint) return;
        WorldCheckpoint.BindOfflineWallets();
        WorldCheckpoint.Embed(coordinator.ExportJournal(), calendarClock!.ExportState(), false);
    }

    private void OnSaved(object? sender, SavedEventArgs e)
    {
        if (!Config.Enabled || !Context.IsMainPlayer || coordinator is null || savingCheckpoint) return;
        if (coordinator.Snapshot.Phase != DayPhase.Advancing)
        {
            Wallets.Report(coordinator.Snapshot, Config.IncludeHost, ShortDate());
            return;
        }
        try
        {
            coordinator.AcknowledgeSavedNextDay(coordinator.Snapshot.AdvanceToken!, Game1.Date.TotalDays, Roster());
            calendarClock!.ConfirmNextDay(Game1.Date.TotalDays);
            checkpointPending = false;
            // The host PC's own farmer is never shown an old night: whatever it is owed is owed to nobody.
            if (IsRequired(Game1.player.UniqueMultiplayerID)) coordinator.AcknowledgeNightsShown(Game1.player.UniqueMultiplayerID, coordinator.Snapshot.AbsoluteDay - 1);
            resumedOwnClock.Clear();
            freshDayDue.Clear();
            nightsSentAt.Clear();
            lastHeldSaveMinute = -1;
            foreach (var away in coordinator.Snapshot.Farmers.Where(f => f.Away))
                Monitor.Log($"{away.Name} has not been on the farm for {away.AbsentDays} days, so {ShortDate()} does not wait for them. They are back in the day as soon as they connect.", LogLevel.Info);
            notifier.NewDay(FarmName, ShortDate(), coordinator.Snapshot.Farmers.Select(f => f.Name).ToList());
        }
        catch (Exception ex) { Fail("The saved next day could not be confirmed. " + ex.Message); }
        // The night's save is on disk: what ModRelay shows while the farm sleeps is this.
        Wallets.Report(coordinator?.Snapshot, Config.IncludeHost, ShortDate());
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Active) return;
        walletsDayStarted = true;
        sleeping = false;
        pendingSleepHome = null;
        waitingMenuQueued = false;
        waitingMenu = null;
        guestMenu = null;
        farmPickerQueued = false;
        waitingMenuSelection = null;
        waitingMenuDescription = previousStatusError = null;
        previousLocalFinished = previousChoosing = false;
        parkedResumeDone = false;
        holdActive = holdAnswered = false;
        if (wasLocked) { Game1.player.CanMove = true; wasLocked = false; }
        if (Context.IsMainPlayer)
        {
            clockFaults = 0;
            turnStartedAt = 600;
            if (error is null && coordinator?.Snapshot.Mode == PlayMode.Relay)
                NpcMorning(NpcMorningReplay.EnsureMorningBaseline, replay: false);
            Broadcast();
        }
        else { status = null; SendHello(); }
    }

    private void OnTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        coordinator = null;
        walletsDue = walletsDayStarted = false;
        walletsTicks = 0;
        Wallets.Reset();
        bypassDay = -1;
        bypassQueued = false;
        restartConfirmQueued = restartPasswordQueued = false;
        toolsQueued = toolsPasswordQueued = false;
        freshConfirmFor = null;
        toolsSecret = null;
        toolsDay = -1;
        freshDayDue.Clear();
        notifier.Reset();
        tracker.Reset();
        portraits.Reset();
        calendarClock = null;
        MachineMinutesThisTick = -1;
        unscheduledTargetMinute = -1;
        error = patchError;
        status = null;
        pendingJoins.Clear();
        incompatiblePeers.Clear();
        unadmittedSince.Clear();
        rejectReasons.Clear();
        rejectedPeers.Clear();
        checkpointPending = sleeping = wasLocked = false;
        pendingSleepHome = null;
        waitingMenuQueued = previousLocalFinished = false;
        waitingMenu = null;
        guestMenu = null;
        farmPickerQueued = false;
        waitingMenuSelection = null;
        waitingMenuDescription = previousStatusError = null;
        emptyTicks = 0;
        announcedTurn = null;
        lastSentHealth = -1;
        skipMenuQueued = finishConfirmQueued = false;
        TurnHud.Reset();
        barred = new HashSet<long>();
        newFarmersAllowed = true;
        freeCabinFailed.Clear();
        resumedOwnClock.Clear();
        freshDayDue.Clear();
        nightsSentAt.Clear();
        lastSeen.Clear();
        kicksDue.Clear();
        creditedDay = -1;
        heldSave.End();
        lastHeldSaveMinute = -1;
        holdActive = holdAnswered = false;
        nights.Reset();
        parkedResumeDone = previousChoosing = false;
        if (bedWording is not null)
        {
            bedWording = null;
            Helper.GameContent.InvalidateCache(BedQuestion.Asset);
        }
    }

    /// <summary>The turn board for this player right now, or null when there is nothing to show (not in a farm, a held farm, no status yet).</summary>
    internal TurnBoardView? BoardNow()
    {
        if (!Active || status is null || error is not null || status.HasFatalError) return null;
        long me = Game1.player.UniqueMultiplayerID;
        return status.Roster.Length > 0
            ? TurnBoardView.Build(status.Roster.Select(r => new RosterFarmer(r.Id, r.Name, r.Status, r.Online, r.InTurn)), me, status.Released)
            : TurnBoardView.FromNames(status.WaitingFor, status.FinishedNames, status.SkippedNames, Game1.player.Name, status.Released);
    }

    private void Broadcast()
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady) return;
        if (coordinator is null)
        {
            if (error is null) return;
            status = new StatusMessage { Day = Game1.Date.TotalDays, Error = error, HasFatalError = true, Fixes = 2,
                BypassSalt = BypassCode.IsSet(Config.Bypass.Hash) ? Config.Bypass.Salt : null, BypassHash = BypassCode.IsSet(Config.Bypass.Hash) ? Config.Bypass.Hash : null };
            Helper.Multiplayer.SendMessage(status, "status", new[] { ModManifest.UniqueID });
            return;
        }
        var s = coordinator.Snapshot;
        status = new StatusMessage
        {
            Day = s.AbsoluteDay, Session = s.SessionNumber, Revision = s.Revision,
            Finished = s.Farmers.Where(f => f.Status != FarmerStatus.Pending).Select(f => f.Id).ToArray(),
            WaitingFor = s.Farmers.Where(f => f.Status == FarmerStatus.Pending || (s.Mode == PlayMode.Together && !f.IsOnline && f.Status != FarmerStatus.Skipped)).Select(f => f.Name).ToArray(),
            Released = s.Phase == DayPhase.Advancing && error is null && incompatiblePeers.Count == 0 && pendingJoins.Count == 0,
            CheckpointPending = checkpointPending,
            HasFatalError = error is not null,
            Error = error ?? (incompatiblePeers.Count > 0 ? "Someone joined without a matching Shared Day. They will be disconnected shortly."
                : LoadingIn ? "A farmer is still loading in." : null),
            Mode = s.Mode,
            FinishedNames = s.Farmers.Where(f => f.Status == FarmerStatus.Completed).Select(f => f.Name).ToArray(),
            SkippedNames = s.Farmers.Where(f => f.Status == FarmerStatus.Skipped && !f.Away).Select(f => f.Name).ToArray(),
            AwayNames = s.Farmers.Where(f => f.Away).Select(f => f.Name).ToArray(),
            Roster = s.Farmers.Select(f => new RosterEntry { Id = f.Id, Name = f.Name, Status = f.Status, Online = f.IsOnline, InTurn = f.IsInCurrentSession }).ToArray(),
            SkipVotes = SkipVoteEntries(s),
            BypassSalt = BypassCode.IsSet(Config.Bypass.Hash) ? Config.Bypass.Salt : null,
            BypassHash = BypassCode.IsSet(Config.Bypass.Hash) ? Config.Bypass.Hash : null,
            ReturnChoices = ReturnChoiceEntries(s),
            ResumedAtOwnClock = resumedOwnClock.ToArray(),
            TurnStartedAt = turnStartedAt,
            Fixes = 3,
            Notes = NoteBoard.Entries(NoteBoard.Read())
        };
        Helper.Multiplayer.SendMessage(status, "status", new[] { ModManifest.UniqueID });
    }

    // ---- Sticky notes (0.1.33) ----

    /// <summary>The farm's board as this player last heard it from the host: the host's own save on the host, the status elsewhere.</summary>
    public IReadOnlyList<StickyNote> NotesNow()
        => Context.IsMainPlayer ? NoteBoard.Read() : NoteBoard.FromStatus(status) ?? new List<StickyNote>();

    /// <summary>How many notes this farmer has not read, or null while there is no board to read (no host, or one too old to keep notes).</summary>
    private int? NotesUnread()
    {
        if (!Active || !Context.IsWorldReady || Game1.player is null) return null;
        if (!Context.IsMainPlayer && status?.Notes is null) return null;
        return NoteBoard.Reader().Unread(NotesNow(), Game1.player.UniqueMultiplayerID);
    }

    public void OpenNotes()
    {
        if (Game1.activeClickableMenu is not null || !Context.IsWorldReady) return;
        Game1.playSound("shwip");
        Game1.activeClickableMenu = new NotesMenu(this, Monitor);
    }

    /// <summary>Pin a note: straight onto the board on the host, through the host from a player.</summary>
    public void PinNote(string text)
    {
        var request = new StickyNoteRequest { Day = Game1.Date.TotalDays, Action = "add", Text = text };
        if (Context.IsMainPlayer)
        {
            if (NoteBoard.Apply(Game1.player.UniqueMultiplayerID, Game1.player.Name, request, ShortDate(), Monitor) is { } refused)
            {
                if (Game1.activeClickableMenu is NotesMenu notes) notes.Refused(refused);
            }
            else Broadcast();
            return;
        }
        Helper.Multiplayer.SendMessage(request, "stickynote", new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    /// <summary>Take one of your own notes down, for everyone.</summary>
    public void TakeDownNote(string id)
    {
        var request = new StickyNoteRequest { Day = Game1.Date.TotalDays, Action = "remove", Id = id };
        if (Context.IsMainPlayer)
        {
            if (NoteBoard.Apply(Game1.player.UniqueMultiplayerID, Game1.player.Name, request, ShortDate(), Monitor) is { } refused)
            {
                if (Game1.activeClickableMenu is NotesMenu notes) notes.Refused(refused);
            }
            else Broadcast();
            return;
        }
        Helper.Multiplayer.SendMessage(request, "stickynote", new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    /// <summary>Every farmer back with a parked turn while somebody else plays, with what their question needs (and whether they chose to wait).</summary>
    private static ReturnChoiceEntry[] ReturnChoiceEntries(DaySnapshot s)
    {
        var playing = s.Farmers.Where(f => f.IsActive).Select(f => f.Name).ToArray();
        return s.Farmers.Where(f => f.IsOnline && f.Status == FarmerStatus.Pending && !f.IsInCurrentSession && f.TimeReached is not null)
            .Select(f => new ReturnChoiceEntry { Id = f.Id, Playing = playing, TheirTime = Game1.timeOfDay, MyTime = f.TimeReached!.Value, Waiting = f.Waiting })
            .ToArray();
    }

    private string Describe()
    {
        const string Paused = "Shared Day has paused the farm to protect your save.\n";
        if (error is not null) return Paused + error;
        if (status is null) return "Connecting to the host's Shared Day...";
        if (status.HasFatalError) return Paused + status.Error;
        string date = ShortDate();
        if (status.Released) return $"Everyone has finished {date}. Good night!";
        if (status.Error is not null) return status.Error;
        string me = Game1.player.Name;
        string others = NameList(status.WaitingFor.Where(n => n != me));
        string played = NameList(status.FinishedNames.Where(n => n != me));
        var lines = new List<string>();
        if (holdActive) lines.Add("Hold still a moment: the farm is being saved.");
        if (MyReturnChoice is { } choice)
        {
            string who = NameList(choice.Playing);
            bool morning = FreshDayMark.IsMorning(choice.MyTime);
            if (choice.Waiting)
                lines.Add(morning
                    ? $"You are waiting for {who} to finish {date}. Then your day starts at 6:00 AM, in your own bed."
                    : $"You are waiting for {who} to finish {date}. Then you carry on at {DayClock.Words(choice.MyTime)}, where you left your turn.");
            else
                lines.Add(morning
                    ? $"{who} {(choice.Playing.Length == 1 ? "is" : "are")} playing {date} now and it is {DayClock.Words(choice.TheirTime)} on the farm. Your day starts at 6:00 AM. Join {who} now, or wait and start yours at 6:00 AM?"
                    : $"{who} {(choice.Playing.Length == 1 ? "is" : "are")} playing {date} now and it is {DayClock.Words(choice.TheirTime)} on the farm. You left your turn at {DayClock.Words(choice.MyTime)}. Join {who} now, or wait and carry on at your own time?");
        }
        else if (LocalFinished && LocalAway)
            lines.Add($"You stepped away from {date}, so nobody is waiting for you. Touch the keyboard and you are back in it.");
        else if (LocalFinished)
        {
            lines.Add($"Your {date} is done.");
            if (status.Mode == PlayMode.Together)
                // Together also waits for finished players who went offline, so do not say "finish".
                lines.Add(others.Length > 0 ? $"Waiting for {others}. In Together mode the day ends once everyone has finished and is online." : "The day is about to end.");
            else
            {
                if (others.Length > 0) lines.Add($"Still to play this day: {others}.");
                lines.Add("You can keep watching or disconnect. Everything you did stays on the farm.");
            }
        }
        else if (CanFinishLocal)
        {
            lines.Add($"{date}: it's your turn.");
            if (played.Length > 0) lines.Add($"Already played this day: {played}.");
            if (others.Length > 0) lines.Add($"Still to play: {others}.");
            if (IsLastToPlay) lines.Add($"You are the last to play {date}. When you finish, the night runs for everyone.");
            lines.Add("Go to bed, or choose Finish my day, when you're done.");
        }
        else lines.Add(others.Length > 0 ? $"Waiting for {others} to play {date}." : $"Waiting for {date} to end.");
        if (status.SkippedNames.Length > 0) lines.Add($"Sitting out today: {NameList(status.SkippedNames)}.");
        if (status.AwayNames is { Length: > 0 } awayNames)
            lines.Add($"Away, and back in the day as soon as they connect: {NameList(awayNames)}.");
        foreach (var vote in status.SkipVotes?.Where(v => v.Votes > 0) ?? Enumerable.Empty<SkipVoteEntry>())
            lines.Add($"Votes to skip {vote.Name} today: {vote.Votes} of {vote.Needed}.");
        if (Context.IsMainPlayer)
            lines.Add(!status.CheckpointPending ? "Everything is saved."
                : checkpointError is null ? "Not saved yet. The farm saves once every other player has left."
                : "Not saved yet: the last try failed and Shared Day is trying again. " + checkpointError);
        return string.Join("\n", lines);
    }

    // "Spring 13". The game's own WorldDate.Localize() reads "Day 13 of Spring, Year 1",
    // which is clumsy inside a sentence.
    // The messages add " Farm" themselves: a farm called "Squeet Farm" is not "Squeet Farm Farm" in them.
    private static string FarmName => Game1.MasterPlayer?.farmName.Value is { Length: > 0 } name ? FarmWords.Bare(name) : "the";

    private static string ShortDate() => $"{Utility.getSeasonNameFromNumber(Game1.seasonIndex)} {Game1.dayOfMonth}";

    private static string NameList(IEnumerable<string> names)
    {
        var list = names.Distinct().ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1]
        };
    }

    private void OnButton(object? sender, ButtonPressedEventArgs e)
    {
        // Only a press made IN the game counts. The game reads the mouse wherever it is, and a host
        // keeps running when its window is not in front, so a click in any other program on that
        // PC used to pass for somebody playing: on 2026-10-07 the farm's own farmer "played"
        // Spring 4 for an hour and a half, and the farm never slept, while the PC was being used
        // for other work.
        if (Context.IsMainPlayer && Game1.game1.IsActive)
        {
            bool wasPresent = HostPresent;
            // A farm that hosts itself: sitting down is one deliberate key (see HostSeatKey).
            var press = SelfHostedFarm && Context.IsWorldReady
                ? HostSeatKey.Decide(wasPresent, e.Button == Config.SeatKey)
                : HostSeatPress.Stay;
            if (press == HostSeatPress.Ignore)
            {
                Helper.Input.Suppress(e.Button);
                return;
            }
            lastHostInput = press == HostSeatPress.Leave ? DateTime.MinValue : DateTime.UtcNow;
            if (press is HostSeatPress.Sit or HostSeatPress.Leave) Helper.Input.Suppress(e.Button);
            // The first press after an empty seat seats them at once, not at the next second.
            if (wasPresent != HostPresent && Active && Context.IsWorldReady)
            {
                TendHostSeat();
                DedicatedHost.Apply(Unattended, Monitor);
                Game1.addHUDMessage(new HUDMessage(press == HostSeatPress.Leave
                    ? $"You got up. The farm runs by itself again. {Config.SeatKey} sits you back down."
                    : $"You are playing as {Game1.player.Name}. {Config.SeatKey} gets you up again.") { noIcon = true });
            }
            if (press == HostSeatPress.Leave) return;
        }
        if (!Active) return;
        // The notes tile beside the board opens the farm's sticky notes (0.1.33).
        if (e.Button == SButton.MouseLeft && Game1.activeClickableMenu is null
            && TurnHud.OwnsNotes(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true)))
        {
            Helper.Input.Suppress(e.Button);
            OpenNotes();
            return;
        }
        // The board is picked up before the lock below, because carrying it is not acting in the
        // world: a player with nothing to do but wait is exactly who wants to move it.
        if (e.Button == SButton.MouseLeft && Game1.activeClickableMenu is null
            && TurnHud.Owns(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true)))
        {
            Helper.Input.Suppress(e.Button);
            // The menu waits for the release: a press that moves is a drag, not a click.
            boardPressed = TurnHud.BeginDrag(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
            return;
        }
        if (ShouldLockLocal && Game1.activeClickableMenu is null && e.Button != Config.MenuKey)
        {
            Helper.Input.Suppress(e.Button);
            return;
        }
        if (e.Button != Config.MenuKey || Game1.activeClickableMenu is not null) return;
        Helper.Input.Suppress(e.Button);
        ShowMenu();
    }

    /// <summary>The pointer moved: if the board was picked up, it is being carried.</summary>
    private void OnCursorMoved(object? sender, CursorMovedEventArgs e)
    {
        if (boardPressed) TurnHud.Drag.MoveTo(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
    }

    /// <summary>
    /// The button came up on the board: a board that moved is saved where it was dropped, and one
    /// that did not opens Shared Day's menu, the way a click on it always has.
    /// </summary>
    private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
    {
        if (!boardPressed || e.Button != SButton.MouseLeft) return;
        boardPressed = false;
        var dropped = TurnHud.Drag.Release();
        if (dropped is null)
        {
            ShowMenu();
            return;
        }
        Config.Hud.Corner = dropped.Corner;
        Config.Hud.OffsetX = dropped.OffsetX;
        Config.Hud.OffsetY = dropped.OffsetY;
        try
        {
            Helper.WriteConfig(Config);
        }
        catch (Exception ex)
        {
            // The board has still moved for this session; only remembering it failed.
            Monitor.Log("Shared Day moved the turn board but could not save where. " + ex.Message, LogLevel.Warn);
        }
    }

    internal void ShowMenu()
    {
        if (Game1.activeClickableMenu is not null) return;
        waitingMenuQueued = false;
        waitingMenuSelection = null;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        var choices = new List<Response>();
        var choice = MyReturnChoice;
        if (choice is { Waiting: false })
        {
            // Back with a parked turn while somebody else plays: their turn at its clock, or wait for it.
            string who = NameList(choice.Playing);
            choices.Add(new Response("joinnow", $"Join {who} now ({DayClock.Words(choice.TheirTime)})"));
            choices.Add(new Response("wait", FreshDayMark.IsMorning(choice.MyTime)
                ? $"Wait for {who}, then start my day at 6:00 AM"
                : $"Wait for {who}, then carry on at {DayClock.Words(choice.MyTime)}"));
            choices.Add(new Response("close", "Decide later"));
        }
        else
        {
            if (waitingMenuCanFinish)
                choices.Add(new Response("finish", "Finish my day"));
            choices.Add(new Response("close", waitingMenuCanFinish ? "Keep playing" : "Keep waiting"));
            if (choice is { Waiting: true })
                choices.Add(new Response("joinnow", $"Join {NameList(choice.Playing)} now instead ({DayClock.Words(choice.TheirTime)})"));
        }
        if (CanSaveTurn) choices.Add(new Response("savenow", "Save my turn"));
        if (Context.IsMainPlayer && !Released && error is null && SkippableFarmers().Any())
            choices.Add(new Response("skip", "Skip someone's turn today"));
        else if (VotableFarmers().Any())
            choices.Add(new Response("skip", "Vote to skip someone today"));
        if (FixChoices().Count > 0) choices.Add(new Response("fix", "Fix a problem"));
        // 0.1.35: the host's repairs in one place, for the host at its keyboard or whoever has
        // the farm's password (the owner, 2026-10-11: "if admin or host ... a troubleshooting option
        // reset someone's day? or reset everyone back to same day?").
        if (CanTroubleshoot) choices.Add(new Response("tools", ToolsUnlocked ? "Troubleshooting" : "Troubleshooting (farm password)"));
        // With Many Farms on this farm: pick a farm or start one, at any point, not only on first
        // joining. Many Farms words it for the player ("Pick or start a farm", "Change my farm").
        if (FarmPickerChoice() is { } farmChoice) choices.Add(new Response("farm", farmChoice));
        // The owner, 2026-10-07: "the options i got at day end were kinda confusing, maybe an I
        // button that you can click to explain". An answer in the list, so it is there on a
        // controller too.
        var offered = choices.Select(c => c.responseKey).Append("leave").ToArray();
        bool voting = !Context.IsMainPlayer;
        choices.Add(new Response("help", "What do these choices do?"));
        choices.Add(new Response("leave", "Disconnect"));
        skipMenuOpen = fixMenuOpen = false;
        AskQuestion(waitingMenuDescription, choices.ToArray(), (_, answer) =>
        {
            if (answer == "finish") finishConfirmQueued = true;
            if (answer is "joinnow" or "wait") SendReturnChoice(answer);
            if (answer == "savenow") RequestSaveTurn();
            if (answer == "skip") skipMenuQueued = true;
            if (answer == "fix") fixMenuQueued = true;
            if (answer == "tools") { if (ToolsUnlocked) toolsQueued = true; else toolsPasswordQueued = true; }
            if (answer == "farm") farmPickerQueued = true;
            if (answer == "help") { helpChoices = offered; helpVoting = voting; helpQueued = true; }
            if (answer == "leave") { SendHealth(force: true); Game1.ExitToTitle(null); }
        });
        waitingMenu = Game1.activeClickableMenu;
    }

    /// <summary>
    /// What the answers on Shared Day's popup do. The game's box grows upwards with its text and
    /// a whole page of explanation ran off the top of a small screen, so it is a list: pick an
    /// answer to read the one or two sentences about it, then Back.
    /// </summary>
    private void ShowHelp()
    {
        if (Game1.activeClickableMenu is not null) return;
        string date = ShortDate();
        string? about = helpAbout;
        helpAbout = null;
        string text;
        var answers = new List<Response>();
        if (about is null)
        {
            text = "Everyone plays the same day, and it moves on once every farmer has finished or is sitting out. Which choice do you want explained?";
            foreach (string key in helpChoices)
            {
                string? label = key switch
                {
                    "finish" => "Finish my day",
                    "joinnow" => "Join now",
                    "wait" => "Wait",
                    "savenow" => "Save my turn",
                    "close" => "Keep playing / Keep waiting",
                    "skip" => helpVoting ? "Vote to skip someone today" : "Skip someone's turn today",
                    "fix" => "Fix a problem",
                    "tools" => "Troubleshooting",
                    "farm" => FarmPickerChoice(),
                    "leave" => "Disconnect",
                    _ => null
                };
                if (label is not null) answers.Add(new Response("about:" + key, label));
            }
            answers.Add(new Response("back", "Back"));
        }
        else
        {
            text = about switch
            {
                "finish" => $"Finish my day ends your turn for {date}, the same as going to bed. Everything you did stays, and you play again when the next day starts.",
                "joinnow" => "Join now puts you into the turn being played, at its clock. The hours between where you left your turn and now are gone for you; your day ends at 2:00 AM with everyone else's.",
                "wait" => "Wait holds you here until the turn being played has ended. Then the clock goes back to where you left your turn and you carry on alone, losing nothing.",
                "savenow" => "Save my turn writes the farm now, with you where you stand and the clock where it is, so a host that goes down loses none of your turn. Everyone on the farm holds still for a moment.",
                "close" => "Keep playing, or Keep waiting, just closes this box. Nothing changes.",
                "skip" when helpVoting => "Vote to skip lets the day end without a farmer who is offline and has not played. It passes when more than half of the other farmers vote for it. They lose nothing and play again tomorrow.",
                "skip" => "Skip lets the day end without a farmer who is offline and has not played. They lose nothing and play again tomorrow.",
                "fix" => "Fix a problem undoes a finish made by mistake, brings a skipped farmer back into today, or gets you unstuck. With the farm's password it can also restart today for everyone, from 6:00 AM in bed. Nothing is taken away.",
                "tools" => "Troubleshooting is for the host, or whoever has the farm's password. It can give one farmer a fresh day (6:00 AM again, in bed, while everyone else carries on) or restart today for everyone. The villagers go back to their morning; nothing anyone owns or did is put back.",
                "farm" => FarmPickerHelp(),
                _ => "Disconnect leaves the farm for the title screen. A turn you have not finished stays open, and the others wait for you."
            };
            answers.Add(new Response("list", "Another choice"));
            answers.Add(new Response("back", "Back"));
        }
        waitingMenuSelection = null;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        skipMenuOpen = fixMenuOpen = false;
        AskQuestion(text, answers.ToArray(), (_, answer) =>
        {
            if (answer.StartsWith("about:", StringComparison.Ordinal)) { helpAbout = answer[6..]; helpQueued = true; }
            else if (answer == "list") helpQueued = true;
            else waitingMenuQueued = true;
        });
        waitingMenu = Game1.activeClickableMenu;
    }

    private string FarmPickerHelp()
    {
        const string plain = "It opens Many Farms' list of farms: join one, or start one of your own. Your cabin stays where it is, and it does not end your turn.";
        if (!HasFarmPicker) return plain;
        try { return farms!.FarmPickerHelp is { Length: > 0 } help ? help : plain; }
        catch (Exception ex) { FarmsFailed(ex); return plain; }
    }

    // Finishing cannot be undone until the next day, and a controller pre-selects the first
    // answer, so ask once more with the harmless answer first. Going to bed already asks.
    private void ShowFinishConfirm()
    {
        if (!CanFinishLocal || Game1.activeClickableMenu is not null) return;
        AskQuestion($"Finish {ShortDate()} now? You can't play again until the next day starts. Everything you did stays on the farm."
            + (IsLastToPlay ? $" You are the last to play {ShortDate()}. When you finish, the night runs for everyone." : ""),
            new[] { new Response("keep", "Keep playing"), new Response("finish", "Finish my day") },
            (_, answer) => { if (answer == "finish") RequestFinish(); });
    }

    private IEnumerable<FarmerSnapshot> SkippableFarmers() => coordinator is null || coordinator.Snapshot.Phase != DayPhase.Open
        ? Enumerable.Empty<FarmerSnapshot>()
        : coordinator.Snapshot.Farmers.Where(f => f.Status == FarmerStatus.Pending && !f.IsOnline);

    private void ShowSkipMenu()
    {
        // The host skips outright. Everyone else votes, and the host counts.
        bool voting = !Context.IsMainPlayer;
        var choices = voting
            ? VotableFarmers().Select(v => new Response("skip:" + v.Id, $"Vote to skip {v.Name} today ({v.Votes} of {v.Needed} so far)")).ToList()
            : SkippableFarmers().Select(f => new Response("skip:" + f.Id, $"Skip {f.Name} today")).ToList();
        if (choices.Count == 0 || Game1.activeClickableMenu is not null) return;
        choices.Add(new Response("back", "Back"));
        waitingMenuSelection = null;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        skipMenuOpen = true;
        fixMenuOpen = false;
        AskQuestion(
            "Skipping lets the day end without that farmer's turn. They keep everything they own and play again tomorrow."
                + (voting ? " It passes when more than half of the other farmers vote for it." : ""),
            choices.ToArray(), (_, answer) =>
            {
                if (answer.StartsWith("skip:", StringComparison.Ordinal) && long.TryParse(answer[5..], out long id))
                {
                    if (!voting) SkipFarmer(id);
                    else if (status is not null)
                        Helper.Multiplayer.SendMessage(new SkipVoteRequest { Day = status.Day, Target = id }, "voteskip",
                            new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
                }
                if (LocalFinished) waitingMenuQueued = true;
            });
        waitingMenu = Game1.activeClickableMenu;
    }

    /// <summary>
    /// What "Fix a problem" can do for this player right now. Each one puts something back the
    /// way it should be and loses nothing, so nobody has to leave the game or reach the host PC
    /// (the owner, 2026-10-06: "make them easily fixable while in game ... a reset day for player, or
    /// some control panel"). Who may do what:
    /// reopening a finished day is the farmer's own (the host at its keyboard may do it for
    /// anyone); bringing a skipped farmer back is anyone's on today's roster, since it only gives
    /// that farmer their turn back; refreshing and unfreezing touch nothing but this player's screen.
    /// </summary>
    private List<Response> FixChoices()
    {
        var choices = new List<Response>();
        // A paused farm used to offer nothing here, exactly when something had gone wrong.
        if (error is not null || status?.HasFatalError == true)
        {
            if (Context.IsMainPlayer || error is not null || status!.Fixes >= 2)
                choices.Add(new Response("fix:retry", "Try again (the farm is paused)"));
            if (BypassCode.IsSet(BypassHash) && !Bypassed) choices.Add(new Response("fix:bypass", "Bypass Shared Day today (farm password)"));
            return choices;
        }
        if (status is null || Released) return choices;
        long me = Game1.player.UniqueMultiplayerID;
        bool host = Context.IsMainPlayer;
        bool hostFixes = host || status.Fixes >= 1;
        bool onRoster = status.Roster.Any(r => r.Id == me);
        if (hostFixes && LocalFinished && status.Roster.Any(r => r.Id == me && r.Status == FarmerStatus.Completed))
            choices.Add(new Response("fix:reopen:" + me, "Reopen my day (I finished by mistake)"));
        if (host)
            foreach (var f in status.Roster.Where(r => r.Id != me && r.Status == FarmerStatus.Completed))
                choices.Add(new Response("fix:reopen:" + f.Id, $"Reopen {f.Name}'s day"));
        if (hostFixes && (host || onRoster))
            foreach (var f in status.Roster.Where(r => r.Status == FarmerStatus.Skipped))
                choices.Add(new Response("fix:unskip:" + f.Id, f.Id == me ? "Bring me back into today" : $"Bring {f.Name} back into today"));
        if (!host && hostFixes) choices.Add(new Response("fix:refresh", "Ask the host for the farm's state again"));
        if (!ShouldLockLocal) choices.Add(new Response("fix:unfreeze", "Unfreeze me (I cannot move)"));
        // 0.1.34: the whole day from the beginning, for everyone. The farm's password guards it,
        // since it gives every farmer who finished their turn back; the host at its own keyboard
        // needs none.
        if (host || BypassCode.IsSet(BypassHash)) choices.Add(new Response("fix:restart", host ? "Restart today for everyone" : "Restart today for everyone (farm password)"));
        if (BypassCode.IsSet(BypassHash) && !Bypassed) choices.Add(new Response("fix:bypass", "Bypass Shared Day today (farm password)"));
        return choices;
    }

    private void ShowFixMenu()
    {
        var choices = FixChoices();
        if (choices.Count == 0 || Game1.activeClickableMenu is not null) return;
        choices.Add(new Response("back", "Back"));
        waitingMenuSelection = null;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        fixMenuOpen = true;
        skipMenuOpen = false;
        AskQuestion(
            "These put a day right without anyone leaving the game. None of them takes anything away: what you own and what you did stay as they are.",
            choices.ToArray(), (_, answer) =>
            {
                var parts = answer.Split(':');
                if (parts.Length >= 2 && parts[0] == "fix")
                {
                    long target = parts.Length == 3 && long.TryParse(parts[2], out long id) ? id : 0;
                    if (parts[1] == "unfreeze") Unfreeze();
                    else if (parts[1] == "bypass") { bypassQueued = true; return; }
                    else if (parts[1] == "restart") { restartConfirmQueued = true; return; }
                    else if (parts[1] == "retry") RequestRetry();
                    else if (Context.IsMainPlayer) ApplyFix(Game1.player.UniqueMultiplayerID, new FixRequest { Day = status?.Day ?? -1, Kind = parts[1], Target = target });
                    else if (status is not null)
                        Helper.Multiplayer.SendMessage(new FixRequest { Day = status.Day, Kind = parts[1], Target = target }, "fix",
                            new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
                }
                if (LocalFinished) waitingMenuQueued = true;
            });
        waitingMenu = Game1.activeClickableMenu;
    }

    /// <summary>
    /// "Try again" from a paused farm. A player's own pause (a version mismatch seen once, say) is
    /// cleared here and the host is greeted afresh; the host's pause is the host's to clear.
    /// </summary>
    private void RequestRetry()
    {
        if (Context.IsMainPlayer) { RetryAfterHold(Game1.player.UniqueMultiplayerID); return; }
        if (error is not null)
        {
            error = patchError;
            status = null;
            SendHello();
        }
        Helper.Multiplayer.SendMessage(new FixRequest { Day = Game1.Date.TotalDays, Kind = "retry" }, "fix",
            new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        Game1.addHUDMessage(new HUDMessage("Asked the farm to try again."));
    }

    /// <summary>
    /// The host lifts its pause and looks again. Nothing is forced through: whatever was wrong
    /// is checked afresh, and a pause that is still deserved comes straight back. Until now the
    /// only way out of a pause was to close the host's game, which nobody playing can do.
    /// </summary>
    private void RetryAfterHold(long asker)
    {
        if (!Context.IsMainPlayer) return;
        if (error is null) { Broadcast(); return; }
        if (DateTime.UtcNow - lastRetry < TimeSpan.FromSeconds(10)) return;
        lastRetry = DateTime.UtcNow;
        string was = error;
        string who = asker == Game1.player.UniqueMultiplayerID ? "The host"
            : Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == asker)?.Name ?? "A farmer";
        error = patchError;
        if (error is not null) { Broadcast(); return; }
        Monitor.Log($"{who} asked Shared Day to try again. It was paused by: {was}", LogLevel.Info);
        sleeping = false;
        pendingSleepHome = null;
        checkpointRetryAt = default;
        // Turns that no longer match the farm's date (a night that saved but could not be
        // confirmed) are read again from the farm itself, as on a fresh load.
        if (coordinator is null || coordinator.Snapshot.AbsoluteDay != Game1.Date.TotalDays)
        {
            coordinator = null;
            calendarClock = null;
            LoadHostDay();
        }
        if (coordinator is not null && error is null)
        {
            // A farmer whose turn could not start when the pause began is admitted again.
            var day = coordinator.Snapshot;
            foreach (var farmer in Game1.getOnlineFarmers())
            {
                long id = farmer.UniqueMultiplayerID;
                if (id == Game1.player.UniqueMultiplayerID) continue;
                if (day.Farmers.FirstOrDefault(f => f.Id == id) is { IsOnline: false, Status: FarmerStatus.Pending }) pendingJoins.Add(id);
            }
            long me = Game1.player.UniqueMultiplayerID;
            if (HostPlays && day.Farmers.FirstOrDefault(f => f.Id == me) is { IsOnline: false, Status: FarmerStatus.Pending }) Join(me);
        }
        if (error is null) Game1.chatBox?.addInfoMessage($"{who} asked the farm to try again, and it is running.");
        Broadcast();
    }

    /// <summary>
    /// Asks for the farm's password in the game's own text box. The owner, 2026-10-07: "maybe a bypass
    /// button (if glitched) requires bypass password". It is the way out when everything else has
    /// failed: with the password, Shared Day stops holding this player for the rest of the day,
    /// whatever state it believes the day is in.
    /// </summary>
    private void AskBypass()
    {
        if (Game1.activeClickableMenu is not null || !BypassCode.IsSet(BypassHash)) return;
        var box = new NamingMenu(TryBypass, "The farm's bypass password", "");
        // The game's "pick a random name" die has no business beside a password.
        if (box.randomButton is not null) box.randomButton.visible = false;
        Game1.activeClickableMenu = box;
        // Shared Day's own, so its lock leaves it open and its backdrop stands behind it.
        waitingMenu = ownPopup = box;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        skipMenuOpen = fixMenuOpen = false;
    }

    /// <summary>What was typed into the password box. Checked here, and again by the host.</summary>
    private void TryBypass(string typed)
    {
        if (ReferenceEquals(Game1.activeClickableMenu, waitingMenu)) Game1.exitActiveMenu();
        waitingMenu = null;
        typed = (typed ?? "").Trim();
        if (!BypassCode.Matches(BypassSalt, BypassHash, typed))
        {
            Monitor.Log("A bypass was asked for with the wrong password.", LogLevel.Warn);
            Game1.addHUDMessage(new HUDMessage("That is not the farm's password.", HUDMessage.error_type));
            if (ShouldLockLocal) waitingMenuQueued = true;
            return;
        }
        bypassDay = Game1.Date.TotalDays;
        waitingMenuQueued = false;
        Game1.player.CanMove = true;
        Game1.freezeControls = false;
        wasLocked = false;
        Monitor.Log($"Shared Day is bypassed for {Game1.player.Name} until the next day (farm password).", LogLevel.Warn);
        Game1.addHUDMessage(new HUDMessage("Shared Day will not hold you again today."));
        if (Context.IsMainPlayer) ApplyBypass(Game1.player.UniqueMultiplayerID);
        else Helper.Multiplayer.SendMessage(new FixRequest { Day = Game1.Date.TotalDays, Kind = "bypass", Secret = typed }, "fix",
            new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    /// <summary>
    /// The host's half of a bypass, once the password has been checked: lift any pause and give
    /// the asker their turn back, so the clock runs for them again. Whatever it cannot do, the
    /// asker is still let go on their own side.
    /// </summary>
    private void ApplyBypass(long asker)
    {
        if (!Context.IsMainPlayer) return;
        string who = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == asker)?.Name ?? "A farmer";
        Monitor.Log($"{who} used the farm's bypass password.", LogLevel.Warn);
        if (error is not null) { lastRetry = default; RetryAfterHold(asker); }
        try
        {
            if (error is null && coordinator is { Snapshot.Phase: DayPhase.Open } day
                && day.Snapshot.Farmers.FirstOrDefault(f => f.Id == asker) is { Status: not FarmerStatus.Pending })
            {
                coordinator.Reinstate(asker);
                checkpointPending = true;
                if (asker != Game1.player.UniqueMultiplayerID || HostPlays) Join(asker);
            }
        }
        catch (DayCoordinationException ex) { Monitor.Log("The bypass could not reopen " + who + "'s day: " + ex.Message, LogLevel.Warn); }
        Game1.chatBox?.addInfoMessage($"{who} used the farm's bypass, and Shared Day will not hold them again today.");
        Broadcast();
    }

    // ---- 0.1.34: Restart today for everyone ----

    /// <summary>"Restart today for everyone?" with what it does and what it leaves alone, before any password.</summary>
    private void ShowRestartConfirm()
    {
        if (Game1.activeClickableMenu is not null || status is null || Released) return;
        waitingMenuSelection = null;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        skipMenuOpen = fixMenuOpen = false;
        AskQuestion($"Restart {ShortDate()} for everyone? The clock goes back to 6:00 AM, every farmer on the farm goes to bed, and everyone plays the day again, including anyone who has finished. What was done today stays: crops, items and money are not put back, and machines do not run the same hours twice.",
            new[]
            {
                new Response("keep", "No, keep the day as it is"),
                new Response("restart", ToolsUnlocked ? "Restart the day" : "Restart the day (farm password)")
            },
            (_, answer) =>
            {
                if (answer == "restart")
                {
                    if (Context.IsMainPlayer) RestartToday(Game1.player.UniqueMultiplayerID);
                    else if (ToolsUnlocked) SendRestart(toolsSecret!);
                    else restartPasswordQueued = true;
                }
                else if (ShouldLockLocal) waitingMenuQueued = true;
            });
        waitingMenu = Game1.activeClickableMenu;
    }

    /// <summary>The farm's password for a restart, in the game's own text box (as for the bypass).</summary>
    private void AskRestartPassword()
    {
        if (Game1.activeClickableMenu is not null || !BypassCode.IsSet(BypassHash)) return;
        var box = new NamingMenu(TryRestart, "The farm's password", "");
        if (box.randomButton is not null) box.randomButton.visible = false;
        Game1.activeClickableMenu = box;
        waitingMenu = ownPopup = box;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        skipMenuOpen = fixMenuOpen = false;
    }

    /// <summary>What was typed for a restart. Checked here so a typo is answered at once, and again by the host.</summary>
    private void TryRestart(string typed)
    {
        if (ReferenceEquals(Game1.activeClickableMenu, waitingMenu)) Game1.exitActiveMenu();
        waitingMenu = null;
        typed = (typed ?? "").Trim();
        if (!BypassCode.Matches(BypassSalt, BypassHash, typed))
        {
            Monitor.Log("A restart of the day was asked for with the wrong password.", LogLevel.Warn);
            Game1.addHUDMessage(new HUDMessage("That is not the farm's password.", HUDMessage.error_type));
            if (ShouldLockLocal) waitingMenuQueued = true;
            return;
        }
        if (Context.IsMainPlayer) { RestartToday(Game1.player.UniqueMultiplayerID); return; }
        SendRestart(typed);
    }

    private void SendRestart(string secret)
    {
        Helper.Multiplayer.SendMessage(new FixRequest { Day = status?.Day ?? Game1.Date.TotalDays, Kind = "restart", Secret = secret }, "fix",
            new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        Game1.addHUDMessage(new HUDMessage("Asked the farm to restart the day."));
    }

    /// <summary>
    /// Why the host will not repair the day now (restart it, or give a farmer a fresh day), or
    /// null when it will. Both move the clock and every villager, so neither runs through a night,
    /// a festival, a cutscene (the host's, or a farmhand's in its "Temp" stage), a pause or a save.
    /// </summary>
    private string? RepairRefusal()
    {
        if (coordinator is null) return "Shared Day is not running on this farm.";
        bool busy = Game1.eventUp || Game1.isFestival()
            || Game1.getOnlineFarmers().Any(f => f.currentLocation?.Name == "Temp");
        string? refusal = DayRestart.Refusal(coordinator.Snapshot.Phase == DayPhase.Open, Released || sleeping || Game1.newDay, busy, error is not null);
        if (refusal is null && (savingCheckpoint || heldSave.Holding)) refusal = "The farm is saving right now. Try again in a moment.";
        return refusal;
    }

    /// <summary>
    /// The host restarts today (the owner, 2026-10-09: "a setting to reset day to beginning and moves
    /// everyone to beds and resets the timer (as a bugfix tool?)"). The turns start again
    /// (DayCoordinator.RestartDay), the clock goes to 6:00 AM, the villagers go back to their
    /// morning, and a mark on the host's farmer sends every farmer on the farm to bed in their
    /// own game (TickRestartBed). The world itself is not put back, and the machine budget keeps
    /// its high-water mark, so no furnace or keg gets today's hours twice.
    /// </summary>
    private bool RestartToday(long asker)
    {
        if (!Context.IsMainPlayer || coordinator is null) return false;
        string who = asker == Game1.player.UniqueMultiplayerID ? "The host"
            : Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == asker)?.Name is { Length: > 0 } name ? name : "A farmer";
        string? refusal = RepairRefusal();
        if (refusal is not null)
        {
            Monitor.Log($"{who} asked to restart the day, and it was not: {refusal}", LogLevel.Info);
            Tell(asker, refusal);
            return false;
        }
        try
        {
            var before = coordinator.Snapshot;
            int highWater = calendarClock?.HighWaterMinute ?? CalendarClock.MorningMinute;
            var playing = coordinator.RestartDay();
            resumedOwnClock.Clear();
            freshDayDue.Clear();
            SetClock(600);
            turnStartedAt = 600;
            NpcMorning(NpcMorningReplay.RestoreMorning, replay: true);
            DedicatedHost.NewTurn();
            // A farmer who is away comes back to the morning, not to a tile they left a turn on.
            long me = Game1.player.UniqueMultiplayerID;
            var online = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();
            foreach (var farmer in before.Farmers.Where(f => !online.Contains(f.Id))) Parked.ClearEverywhere(farmer.Id);
            var onFarm = online.Where(id => id != me || HostPlays).OrderBy(id => id);
            Game1.player.modData[RestartKey] = DayRestart.Write(before.AbsoluteDay, coordinator.Snapshot.Revision, onFarm);
            checkpointPending = true;
            string message = $"{who} restarted {ShortDate()}. Everyone starts again at 6:00 AM in bed; what was done today stays.";
            Monitor.Log($"{message} Turn {coordinator.Snapshot.SessionNumber} has {playing.Count} farmer{(playing.Count == 1 ? "" : "s")} on the farm; "
                + $"the finished farmers before it: {string.Join(", ", before.Farmers.Where(f => f.Status == FarmerStatus.Completed).Select(f => f.Name).DefaultIfEmpty("none"))}. "
                + $"Machines keep today's high-water mark of {DayClock.Words(highWater / 60 * 100 + highWater % 60)}.", LogLevel.Warn);
            Game1.chatBox?.addInfoMessage(message);
            foreach (long id in online.Where(id => id != me))
                Helper.Multiplayer.SendMessage(new NoteMessage { Text = message }, "note", new[] { ModManifest.UniqueID }, new[] { id });
            Broadcast();
            return true;
        }
        catch (DayCoordinationException ex)
        {
            Monitor.Log("Could not restart the day: " + ex.Message, LogLevel.Warn);
            Tell(asker, "The day could not be restarted: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// A restart of the day on each farmer's own game: once per restart, on their first free
    /// tick, they go to bed in their own home. They land on the bed the way the game puts a
    /// farmer who passed out there, so "Go to sleep?" is not asked the moment they arrive. A
    /// farmer who arrives later, into a turn already under way, only notes it.
    /// </summary>
    private void TickRestartBed()
    {
        if (!Context.IsWorldReady || Game1.MasterPlayer is not { } master) return;
        if (Context.IsMainPlayer && !HostPlays) return;
        if (!master.modData.TryGetValue(RestartKey, out string mark)) return;
        string? seen = Game1.player.modData.TryGetValue(RestartSeenKey, out string had) ? had : null;
        var action = DayRestart.For(mark, seen, Game1.Date.TotalDays, Game1.player.UniqueMultiplayerID, Game1.timeOfDay);
        if (action == RestartAction.None) return;
        if (Game1.eventUp || Game1.isFestival() || Game1.isWarping || Game1.newDay || nights.Busy || holdActive) return;
        // Shared Day's own popups give way; anything else the player has open waits until they close it.
        if (Game1.activeClickableMenu is { } open)
        {
            if (!ReferenceEquals(open, waitingMenu) && !ReferenceEquals(open, ownPopup)) return;
            // A question box took the player's controls when it opened; its own close gives them
            // back (lab w17: closed any other way, a farmer whose turn came back could not move).
            if (open is DialogueBox box) box.closeDialogue();
            else Game1.exitActiveMenu();
            waitingMenu = null;
        }
        Game1.player.modData[RestartSeenKey] = mark;
        Parked.Clear(Game1.player);
        if (action == RestartAction.Note) return;
        try { GoToBed("The day was restarted", "The day was restarted. You are back in bed at 6:00 AM."); }
        catch (Exception ex) { Monitor.Log("Could not take you to bed for the restarted day: " + ex.Message, LogLevel.Warn); }
    }

    private void GoToBed(string why, string hud)
    {
        var home = Utility.getHomeOfFarmer(Game1.player);
        var bed = home.GetPlayerBedSpot();
        void InBed()
        {
            Game1.player.Position = new Vector2(bed.X, bed.Y) * 64f;
            // As the game's own pass-out warp does: arriving on the bed is not stepping onto it.
            Game1.player.currentLocation.lastTouchActionLocation = new Vector2(bed.X, bed.Y);
            Game1.player.FacingDirection = 2;
            // A farmer still held here (their finish not yet undone by the host's word) is let go
            // by the hold itself when that word arrives; anyone else gets their controls now.
            if (!ShouldLockLocal)
            {
                Game1.player.CanMove = true;
                Game1.dialogueUp = false;
                Game1.freezeControls = false;
            }
            Monitor.Log($"{why}: in bed at {home.NameOrUniqueName} ({bed.X}, {bed.Y}).", LogLevel.Info);
        }
        if (Game1.currentLocation?.NameOrUniqueName == home.NameOrUniqueName) InBed();
        else
        {
            var request = Game1.getLocationRequest(home.NameOrUniqueName);
            request.OnWarp += InBed;
            Game1.warpFarmer(request, bed.X, bed.Y, 2);
        }
        Game1.addHUDMessage(new HUDMessage(hud, HUDMessage.newQuest_type));
    }

    // ---- 0.1.35: Troubleshooting, and a fresh day for one farmer ----

    /// <summary>
    /// Troubleshooting is the host's: offered at the host's own keyboard, and to a player only on a
    /// farm with a password, which they type before any tool is shown. The host checks it again
    /// with every request, so showing the entry gives nothing away.
    /// </summary>
    private bool CanTroubleshoot => status is not null && !Released && error is null && status.Error is null
        && (Context.IsMainPlayer || BypassCode.IsSet(BypassHash));

    /// <summary>The farm's password, once, before a player sees the tools.</summary>
    private void AskToolsPassword()
    {
        if (Game1.activeClickableMenu is not null || !BypassCode.IsSet(BypassHash)) return;
        var box = new NamingMenu(TryToolsPassword, "The farm's password", "");
        if (box.randomButton is not null) box.randomButton.visible = false;
        Game1.activeClickableMenu = box;
        waitingMenu = ownPopup = box;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        skipMenuOpen = fixMenuOpen = false;
    }

    private void TryToolsPassword(string typed)
    {
        if (ReferenceEquals(Game1.activeClickableMenu, waitingMenu)) Game1.exitActiveMenu();
        waitingMenu = null;
        typed = (typed ?? "").Trim();
        if (!BypassCode.Matches(BypassSalt, BypassHash, typed))
        {
            Monitor.Log("Troubleshooting was asked for with the wrong password.", LogLevel.Warn);
            Game1.addHUDMessage(new HUDMessage("That is not the farm's password.", HUDMessage.error_type));
            if (ShouldLockLocal) waitingMenuQueued = true;
            return;
        }
        toolsSecret = typed;
        toolsDay = Game1.Date.TotalDays;
        toolsQueued = true;
    }

    /// <summary>
    /// The host's repairs: a fresh day for any farmer on today's roster, or today again for
    /// everyone. Each asks once more, naming who it moves, before anything happens.
    /// </summary>
    private void ShowToolsMenu()
    {
        if (Game1.activeClickableMenu is not null || status is null || Released || !ToolsUnlocked) return;
        var choices = status.Roster
            .Select(r => new Response("tools:fresh:" + r.Id, r.Id == Game1.player.UniqueMultiplayerID ? "Give me a fresh day" : $"Give {r.Name} a fresh day"))
            .ToList();
        choices.Add(new Response("tools:restart", "Restart today for everyone"));
        choices.Add(new Response("back", "Back"));
        waitingMenuSelection = null;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        skipMenuOpen = fixMenuOpen = false;
        AskQuestion(
            $"Troubleshooting {ShortDate()}. A fresh day starts one farmer again at 6:00 AM in bed while everyone else carries on; a restart does that for everyone. The villagers go back to their morning either way. Nothing anyone owns or did is put back.",
            choices.ToArray(), (_, answer) =>
            {
                if (answer.StartsWith("tools:fresh:", StringComparison.Ordinal) && long.TryParse(answer[12..], out long id)) freshConfirmFor = id;
                else if (answer == "tools:restart") restartConfirmQueued = true;
                else waitingMenuQueued = true;
            });
        waitingMenu = Game1.activeClickableMenu;
    }

    /// <summary>"Give Alice a fresh day?", naming her and saying what it does and leaves alone.</summary>
    private void ShowFreshDayConfirm(long id)
    {
        if (Game1.activeClickableMenu is not null || status is null || Released || !ToolsUnlocked) return;
        var target = status.Roster.FirstOrDefault(r => r.Id == id);
        if (target is null) return;
        bool me = id == Game1.player.UniqueMultiplayerID;
        string name = me ? "you" : target.Name;
        var playing = status.Roster.Where(r => r.InTurn && r.Online && r.Status == FarmerStatus.Pending && r.Id != id).Select(r => r.Name).ToArray();
        string when = playing.Length > 0
            ? $" {NameList(playing)} {(playing.Length == 1 ? "is" : "are")} playing now, so {name} {(me ? "wait" : "waits")} where {(me ? "you stand" : "they stand")} until that turn ends."
            : target.Online ? "" : $" {target.Name} is away, so it starts when they next join.";
        waitingMenuSelection = null;
        waitingMenuDescription = Describe();
        waitingMenuCanFinish = CanFinishLocal;
        skipMenuOpen = fixMenuOpen = false;
        AskQuestion((me ? $"Take a fresh {ShortDate()} for yourself?" : $"Give {target.Name} a fresh {ShortDate()}?") + $" {(me ? "You start" : target.Name + " starts")} again at 6:00 AM in {(me ? "your" : "their")} own bed, with the villagers back at their morning, and {(me ? "play" : "plays")} the whole day.{when} "
                + $"What was done today stays: crops, items, money, and who {(me ? "you" : "they")} talked to or gave gifts. Nobody else's turn changes.",
            new[]
            {
                new Response("keep", "No, leave it"),
                new Response("fresh", me ? "Give me a fresh day" : $"Give {target.Name} a fresh day")
            },
            (_, answer) =>
            {
                if (answer != "fresh") { if (ShouldLockLocal) waitingMenuQueued = true; return; }
                if (Context.IsMainPlayer) GiveFreshDay(Game1.player.UniqueMultiplayerID, id);
                else if (toolsSecret is not null)
                {
                    Helper.Multiplayer.SendMessage(new FixRequest { Day = status?.Day ?? Game1.Date.TotalDays, Kind = "freshday", Target = id, Secret = toolsSecret }, "fix",
                        new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
                    Game1.addHUDMessage(new HUDMessage($"Asked the farm to give {target.Name} a fresh day."));
                }
            });
        waitingMenu = Game1.activeClickableMenu;
    }

    /// <summary>
    /// The host gives one farmer today again from 6:00 AM (DayCoordinator.RestartFarmer). When
    /// nobody else is playing their morning starts at once: the clock goes to 6:00 AM and the
    /// villagers back to their morning, exactly as for any new turn (AfterPlacement). When
    /// somebody is, they wait, held, until that turn ends (PromoteWaiters starts it then); when
    /// they are away, it starts on their return. Their game puts them in their own bed as it
    /// starts (FreshDayMark, TickFreshBed). The world is not put back, nobody else is moved, and
    /// the machines keep today's high-water mark.
    /// </summary>
    private bool GiveFreshDay(long asker, long target)
    {
        if (!Context.IsMainPlayer || coordinator is null) return false;
        string who = asker == Game1.player.UniqueMultiplayerID ? "The host"
            : Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == asker)?.Name is { Length: > 0 } name ? name : "A farmer";
        var farmer = coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == target);
        string? refusal = farmer is null ? "That farmer is not on today's roster." : RepairRefusal();
        if (refusal is not null)
        {
            Monitor.Log($"{who} asked for a fresh day for {farmer?.Name ?? target.ToString()}, and it was not given: {refusal}", LogLevel.Info);
            Tell(asker, "No fresh day was given: " + refusal);
            return false;
        }
        try
        {
            bool wasInTurn = farmer!.IsInCurrentSession;
            var playing = coordinator.Snapshot.Farmers.Where(f => f.IsActive && f.Id != target).Select(f => f.Name).ToArray();
            var fresh = coordinator.RestartFarmer(target);
            Parked.ClearEverywhere(target);
            resumedOwnClock.Remove(target);
            freshDayDue.Add(target);
            checkpointPending = true;
            string gave = asker == target ? $"{farmer.Name} took a fresh {ShortDate()}" : $"{who} gave {farmer.Name} a fresh {ShortDate()}";
            string message = fresh.Start switch
            {
                FreshDayStart.Now => $"{gave}: they start again at 6:00 AM in bed. What was done today stays.",
                FreshDayStart.AfterTurnInProgress => $"{gave}: they start again at 6:00 AM once {NameList(playing)} {(playing.Length == 1 ? "has" : "have")} finished. What was done today stays.",
                _ => $"{gave}: they start at 6:00 AM when they next join. What was done today stays."
            };
            Monitor.Log($"{message} Before: {fresh.Before}{(farmer.TimeReached is { } t ? ", parked at " + DayClock.Words(t) : "")}.", LogLevel.Warn);
            Game1.chatBox?.addInfoMessage(message);
            foreach (long id in Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).Where(id => id != Game1.player.UniqueMultiplayerID))
                Helper.Multiplayer.SendMessage(new NoteMessage { Text = message }, "note", new[] { ModManifest.UniqueID }, new[] { id });
            if (fresh.Start == FreshDayStart.Now) AfterPlacement(target, fresh.Join!, wasInTurn);
            Broadcast();
            return true;
        }
        catch (DayCoordinationException ex)
        {
            Monitor.Log($"Could not give {farmer?.Name} a fresh day: {ex.Message}", LogLevel.Warn);
            Tell(asker, "No fresh day was given: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// A fresh day on the farmer's own game: once, as their 6:00 AM starts, they go to bed in
    /// their own home, the way a restart of the whole day does. Nobody else on the farm moves.
    /// </summary>
    private void TickFreshBed()
    {
        if (!Context.IsWorldReady || Game1.MasterPlayer is not { } master) return;
        if (Context.IsMainPlayer && !HostPlays) return;
        if (!master.modData.TryGetValue(FreshKey, out string mark)) return;
        string? seen = Game1.player.modData.TryGetValue(FreshSeenKey, out string had) ? had : null;
        if (FreshDayMark.Due(mark, seen, Game1.Date.TotalDays, Game1.player.UniqueMultiplayerID) is not { } due) return;
        if (Game1.eventUp || Game1.isFestival() || Game1.isWarping || Game1.newDay || nights.Busy || holdActive) return;
        // Their own turn has to have started (the host's word that it has): a farmer still waiting
        // for another turn to end stays where they are, held, until then.
        if (LocalWaiting || LocalChoosing || LocalFinished) return;
        if (Game1.activeClickableMenu is { } open)
        {
            if (!ReferenceEquals(open, waitingMenu) && !ReferenceEquals(open, ownPopup)) return;
            if (open is DialogueBox box) box.closeDialogue();
            else Game1.exitActiveMenu();
            waitingMenu = null;
        }
        Game1.player.modData[FreshSeenKey] = due;
        Parked.Clear(Game1.player);
        try { GoToBed("Your fresh day started", $"Your fresh {ShortDate()}: back in bed at 6:00 AM."); }
        catch (Exception ex) { Monitor.Log("Could not take you to bed for your fresh day: " + ex.Message, LogLevel.Warn); }
    }

    /// <summary>A farmer who should be able to move and cannot: give the controls back. Does nothing for one whose day is done.</summary>
    private void Unfreeze()
    {
        if (ShouldLockLocal) { Game1.addHUDMessage(new HUDMessage("You are held because your day is done. Reopen it first.")); return; }
        Game1.player.CanMove = true;
        Game1.freezeControls = false;
        wasLocked = false;
        Game1.addHUDMessage(new HUDMessage("You can move again."));
    }

    /// <summary>
    /// The host carries out one fix. The asker is whoever SMAPI says sent it. A farmer may reopen
    /// only their own day unless the asker is the host itself; anyone on today's roster may bring a
    /// skipped farmer back.
    /// </summary>
    private void ApplyFix(long asker, FixRequest request)
    {
        if (!Context.IsMainPlayer || coordinator is null || error is not null) return;
        var s = coordinator.Snapshot;
        if (request.Kind == "refresh") { Broadcast(); return; }
        if (s.Phase != DayPhase.Open || request.Day != s.AbsoluteDay) return;
        bool byHost = asker == Game1.player.UniqueMultiplayerID;
        if (!byHost && !IsRequired(asker)) return;
        var target = s.Farmers.FirstOrDefault(f => f.Id == request.Target);
        if (target is null) return;
        bool reopen = request.Kind == "reopen" && target.Status == FarmerStatus.Completed && (byHost || asker == target.Id);
        bool unskip = request.Kind == "unskip" && target.Status == FarmerStatus.Skipped;
        if (!reopen && !unskip) return;
        try
        {
            if (coordinator.Reinstate(target.Id) is null) return;
            checkpointPending = true;
            // A farmer who is here is joined afresh, by the same rules as a farmer who reconnects.
            if (target.Id == Game1.player.UniqueMultiplayerID ? HostPlays : Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == target.Id))
                Join(target.Id);
            string who = byHost ? "The host" : s.Farmers.FirstOrDefault(f => f.Id == asker)?.Name ?? "A farmer";
            string message = reopen
                ? (asker == target.Id ? $"{target.Name} reopened their {ShortDate()}." : $"{who} reopened {target.Name}'s {ShortDate()}.")
                : (asker == target.Id ? $"{target.Name} is back in {ShortDate()}." : $"{who} brought {target.Name} back into {ShortDate()}.");
            Monitor.Log(message, LogLevel.Info);
            Game1.chatBox?.addInfoMessage(message);
            Broadcast();
        }
        catch (DayCoordinationException ex) { Monitor.Log("Could not " + request.Kind + " " + target.Name + ": " + ex.Message, LogLevel.Warn); }
    }

    /// <summary>Every absent farmer who can be voted on, with the votes that count right now. Host only.</summary>
    private SkipVoteEntry[] SkipVoteEntries(DaySnapshot s)
    {
        if (s.Phase != DayPhase.Open) return Array.Empty<SkipVoteEntry>();
        skipVotes.Prune(s.AbsoluteDay, s.Farmers);
        return s.Farmers.Where(SkipVoteBook.CanBeSkipped).Select(f =>
        {
            var (votes, needed, voters) = skipVotes.Tally(f.Id, s.Farmers);
            return new SkipVoteEntry { Id = f.Id, Name = f.Name, Votes = votes, Needed = needed, Voters = voters };
        }).ToArray();
    }

    /// <summary>
    /// The host counts one farmer's vote. The voter is whoever SMAPI says sent it, never an ID in
    /// the message. A majority of the other farmers is the same skip the host's own menu gives.
    /// </summary>
    private void CastSkipVote(long voter, SkipVoteRequest request)
    {
        if (!Context.IsMainPlayer || coordinator is null || error is not null) return;
        var s = coordinator.Snapshot;
        if (s.Phase != DayPhase.Open || request.Day != s.AbsoluteDay) return;
        var target = s.Farmers.FirstOrDefault(f => f.Id == request.Target);
        var result = skipVotes.Cast(s.AbsoluteDay, voter, request.Target, s.Farmers);
        if (result == SkipVoteResult.Refused || target is null) return;
        string who = s.Farmers.FirstOrDefault(f => f.Id == voter)?.Name ?? "A farmer";
        var (votes, needed, _) = skipVotes.Tally(target.Id, s.Farmers);
        Monitor.Log($"{who} voted to skip {target.Name} today ({votes} of {needed}).", LogLevel.Info);
        if (result == SkipVoteResult.Passed)
        {
            skipVotes.Forget(target.Id);
            SaveSkipVotes();
            SkipFarmer(target.Id);
            return;
        }
        SaveSkipVotes();
        Game1.chatBox?.addInfoMessage($"{who} voted to skip {target.Name} today: {votes} of {needed}.");
        Broadcast();
    }

    private void SaveSkipVotes()
    {
        try { Helper.Data.WriteJsonFile(SkipVotesFile, skipVotes.Export(Constants.SaveFolderName ?? "")); }
        catch (Exception ex) { Monitor.Log("Shared Day counted a vote but could not write it down. " + ex.Message, LogLevel.Warn); }
    }

    private void LoadSkipVotes()
    {
        try { skipVotes = SkipVoteBook.Restore(Helper.Data.ReadJsonFile<SkipVoteState>(SkipVotesFile), Constants.SaveFolderName ?? "", Game1.Date.TotalDays); }
        catch (Exception ex)
        {
            skipVotes = new SkipVoteBook();
            Monitor.Log("Shared Day could not read today's skip votes and starts without them. " + ex.Message, LogLevel.Warn);
        }
    }

    /// <summary>The absent farmers this player can still vote on: a say on today's roster, and no vote cast yet.</summary>
    private IEnumerable<SkipVoteEntry> VotableFarmers()
    {
        if (Context.IsMainPlayer || Released || error is not null || status?.SkipVotes is not { Length: > 0 } entries) return Enumerable.Empty<SkipVoteEntry>();
        long me = Game1.player.UniqueMultiplayerID;
        var mine = status.Roster.FirstOrDefault(r => r.Id == me);
        if (mine is null || mine.Status == FarmerStatus.Skipped) return Enumerable.Empty<SkipVoteEntry>();
        return entries.Where(v => v.Id != me && !v.Voters.Contains(me));
    }

    private void SkipFarmer(long id)
    {
        if (!Context.IsMainPlayer || coordinator is null || error is not null) return;
        var farmer = coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == id);
        if (farmer is null) return;
        try
        {
            if (!coordinator.Skip(id)) return;
            checkpointPending = true;
            notifier.Skipped(FarmName, ShortDate(), farmer.Name);
            string message = $"{farmer.Name} will sit out {ShortDate()}.";
            Monitor.Log(message, LogLevel.Info);
            Game1.addHUDMessage(new HUDMessage(message));
            Broadcast();
        }
        catch (DayCoordinationException ex) { Monitor.Log("Could not skip " + farmer.Name + ": " + ex.Message, LogLevel.Warn); }
    }

    // A later Relay turn restarts the date at 6 AM in a world others already changed.
    // Tell the player once, so a harvested field or emptied chest is not a surprise.
    private void AnnounceTurn()
    {
        // The nights this player missed are shown first; the toast waits for them.
        if (status is null || Released || status.Error is not null || status.Mode != PlayMode.Relay
            || status.Session < 2 || !CanFinishLocal || nights.Busy) return;
        string key = $"{status.Day}/{status.Session}";
        if (announcedTurn == key) return;
        announcedTurn = key;
        string played = NameList(status.FinishedNames.Where(n => n != Game1.player.Name));
        if (played.Length == 0) return;
        // HUD toasts are narrow and do not wrap; the chat line carries the full sentence.
        Game1.addHUDMessage(new HUDMessage($"{played} already played {ShortDate()}. Your turn!", HUDMessage.newQuest_type));
        Game1.chatBox?.addInfoMessage($"{played} already played {ShortDate()}. It's your turn now, and everything they did is still here.");
    }

    // The game's question dialogue is 1,200 UI pixels wide whatever the screen, so it runs off
    // a small window or a high UI scale. Shared Day's popups take the screen's width instead.
    private static void AskQuestion(string text, Response[] choices, GameLocation.afterQuestionBehavior after)
    {
        var location = Game1.currentLocation;
        location.lastQuestionKey = null;
        location.afterQuestion = after;
        Game1.drawObjectQuestionDialogue(text, choices, Math.Clamp(Game1.uiViewport.Width - 64, 400, 1200));
        ownPopup = Game1.activeClickableMenu;
    }

    // Whichever of Shared Day's popups was opened last, so the backdrop knows its own.
    private static IClickableMenu? ownPopup;

    // On a resize the game puts an open dialogue back to 1,200 pixels without laying its
    // answers out again; open ours afresh at the new width, keeping the selected answer.
    private void OnWindowResized(object? sender, WindowResizedEventArgs e)
    {
        if (!Active || !OwnsWaitingMenu) return;
        if (skipMenuOpen || fixMenuOpen)
        {
            bool fix = fixMenuOpen;
            Game1.exitActiveMenu();
            waitingMenu = null;
            if (fix) ShowFixMenu(); else ShowSkipMenu();
            if (Game1.activeClickableMenu is null) ShowMenu();
            return;
        }
        waitingMenuDescription = null;
        RefreshWaitingMenu();
    }

    private void RefreshWaitingMenu()
    {
        if (!OwnsWaitingMenu) { waitingMenuSelection = null; return; }
        RestoreWaitingMenuSelection();
        if (waitingMenuDescription == Describe() && waitingMenuCanFinish == CanFinishLocal) return;
        // Rebuild only when the visible state changes. Repeated heartbeat messages must
        // not restart the dialogue or steal a keyboard/controller selection.
        // If another status arrives before the previous rebuild has drawn, retain its
        // pending selection rather than reading uninitialized native components.
        string? selectedKey = waitingMenuSelection?.Key;
        bool snapped = waitingMenuSelection?.Snap ?? false;
        if (selectedKey is null && waitingMenu is DialogueBox previous)
        {
            int selected = previous.selectedResponse;
            if (previous.currentlySnappedComponent is { } component && previous.responseCC is { } responses)
            {
                int snappedIndex = responses.IndexOf(component);
                if (snappedIndex >= 0) { snapped = true; selected = snappedIndex; }
            }
            if (selected >= 0 && selected < previous.responses.Length)
                selectedKey = previous.responses[selected].responseKey;
        }
        Game1.exitActiveMenu();
        waitingMenu = null;
        ShowMenu();
        if (selectedKey is null) return;
        waitingMenuSelection = (selectedKey, snapped);
        RestoreWaitingMenuSelection();
    }

    private void RestoreWaitingMenuSelection()
    {
        if (waitingMenu is not DialogueBox current || waitingMenuSelection is not { } selection) return;
        int next = Array.FindIndex(current.responses, r => r.responseKey == selection.Key);
        if (next < 0) next = Array.FindIndex(current.responses, r => r.responseKey == "close");
        current.selectedResponse = next;
        if (selection.Snap && next >= 0)
        {
            // DialogueBox creates response components after its text/transition. Wait for
            // those native components before restoring controller/keyboard focus.
            if (current.responseCC is null || next >= current.responseCC.Count) return;
            current.currentlySnappedComponent = current.responseCC[next];
            current.snapCursorToCurrentSnappedComponent();
        }
        waitingMenuSelection = null;
    }

    private void OnCommand(string command, string[] args)
    {
        if (!Active || !Context.IsMainPlayer || coordinator is null) { Monitor.Log("Load an enabled host farm first.", LogLevel.Warn); return; }
        try
        {
            if (error is not null && args.Length > 0 && args[0] is not ("status" or "retry"))
                throw new InvalidOperationException("Shared Day is held after an error. Reload a complete matching checkpoint before changing the day.");
            if (args.Length >= 2 && args[0] == "skip")
            {
                string who = string.Join(" ", args.Skip(1));
                var match = coordinator.Snapshot.Farmers.Where(f => f.Id.ToString() == who
                    || string.Equals(f.Name, who, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (match.Length != 1) throw new InvalidOperationException($"No single farmer named or numbered '{who}' is on today's roster.");
                SkipFarmer(match[0].Id);
            }
            else if (args.Length == 2 && args[0] == "mode" && Enum.TryParse<PlayMode>(args[1], true, out var mode))
            {
                if (mode == PlayMode.Relay) NpcMorning(NpcMorningReplay.EnsureMorningBaseline, replay: false);
                coordinator.SetMode(mode);
                checkpointPending = true;
            }
            else if (args.Length >= 2 && args[0] is "reopen" or "unskip")
            {
                string who = string.Join(" ", args.Skip(1));
                var match = coordinator.Snapshot.Farmers.Where(f => f.Id.ToString() == who
                    || string.Equals(f.Name, who, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (match.Length != 1) throw new InvalidOperationException($"No single farmer named or numbered '{who}' is on today's roster.");
                ApplyFix(Game1.player.UniqueMultiplayerID, new FixRequest { Day = coordinator.Snapshot.AbsoluteDay, Kind = args[0], Target = match[0].Id });
            }
            else if (args.Length >= 2 && args[0] is "bar" or "unbar" or "free_cabin")
            {
                string who = string.Join(" ", args.Skip(1));
                var match = Game1.getAllFarmers().Where(f => !f.isUnclaimedFarmhand && (f.UniqueMultiplayerID.ToString() == who
                    || string.Equals(f.Name, who, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (match.Length != 1) throw new InvalidOperationException($"No single farmer named or numbered '{who}' is on this farm.");
                if (args[0] == "bar") Bar(match[0].UniqueMultiplayerID, "the host's console");
                else if (args[0] == "unbar") Unbar(match[0].UniqueMultiplayerID);
                else if (Barring.FreeCabin(match[0], Monitor) is { } why) throw new InvalidOperationException($"Could not free {match[0].Name}'s cabin: {why}.");
                else checkpointPending = true;
            }
            else if (args.Length == 1 && args[0] == "checkpoint") { checkpointRetryAt = default; TryCheckpoint(); }
            else if (args.Length == 1 && args[0] == "restart_day") RestartToday(Game1.player.UniqueMultiplayerID);
            else if (args.Length >= 2 && args[0] == "fresh_day")
            {
                string who = string.Join(" ", args.Skip(1));
                var match = coordinator.Snapshot.Farmers.Where(f => f.Id.ToString() == who
                    || string.Equals(f.Name, who, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (match.Length != 1) throw new InvalidOperationException($"No single farmer named or numbered '{who}' is on today's roster.");
                GiveFreshDay(Game1.player.UniqueMultiplayerID, match[0].Id);
            }
            else if (args.Length == 1 && args[0] == "retry") RetryAfterHold(Game1.player.UniqueMultiplayerID);
            Broadcast();
            Monitor.Log(Describe() + "\n" + string.Join("\n", coordinator.Snapshot.Farmers.Select(f => $"{f.Id}: {f.Name}, {f.Status}, online={f.IsOnline}")), LogLevel.Info);
        }
        catch (Exception ex) { Monitor.Log(ex.Message, LogLevel.Error); }
    }

    private static void SetClock(int time)
    {
        Game1.timeOfDay = time;
        Game1.gameTimeInterval = 0;
        Game1.netWorldState.Value.UpdateFromGame1();
    }

    // ---- 0.1.29: the return choice ----

    /// <summary>A player's answer to "join now or wait", sent to the host as a fix request (the host's own farmer is never asked).</summary>
    private void SendReturnChoice(string kind)
    {
        if (Context.IsMainPlayer || status is null) return;
        Helper.Multiplayer.SendMessage(new FixRequest { Day = status.Day, Kind = kind, Target = Game1.player.UniqueMultiplayerID }, "fix",
            new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    /// <summary>The host carries out a farmer's answer. The asker is whoever SMAPI says sent it.</summary>
    private void AnswerReturnChoice(long asker, string kind)
    {
        if (!Context.IsMainPlayer || coordinator is null || error is not null) return;
        try
        {
            var before = coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == asker);
            if (before is null || !before.IsOnline || before.Status != FarmerStatus.Pending || before.IsInCurrentSession) return;
            if (kind == "wait")
            {
                if (!coordinator.Wait(asker)) return;
                Monitor.Log($"{before.Name} waits for the turn in progress to end, then carries on at {DayClock.Words(before.TimeReached ?? 600)}.", LogLevel.Info);
            }
            else if (coordinator.JoinNow(asker) is { } joined)
            {
                Monitor.Log($"{before.Name} joins the turn in progress at {DayClock.Words(Game1.timeOfDay)} instead of waiting for their own clock ({DayClock.Words(before.TimeReached ?? 600)}).", LogLevel.Info);
                AfterPlacement(asker, joined, false);
            }
            else return;
            checkpointPending = true;
            Broadcast();
        }
        catch (DayCoordinationException ex) { Monitor.Log($"Could not take a farmer's answer ({kind}): {ex.Message}", LogLevel.Warn); }
    }

    /// <summary>Once nobody is playing, a farmer who chose to wait gets their own clock back (see DayCoordinator.PromoteWaiter).</summary>
    private void PromoteWaiters()
    {
        if (coordinator is null || error is not null || Released) return;
        try
        {
            if (coordinator.PromoteWaiter() is not { } promoted) return;
            AfterPlacement(promoted.Id, promoted.Join, true);
            checkpointPending = true;
            Broadcast();
        }
        catch (DayCoordinationException ex) { Monitor.Log("Could not give a waiting farmer their turn back: " + ex.Message, LogLevel.Warn); }
    }

    /// <summary>
    /// A player back on their own clock is put back on the tile they left (see Parked): on their
    /// first free tick, once the nights they missed have been shown, never during a cutscene or a
    /// festival. Once per admission; the spot is removed once used or stale.
    /// </summary>
    private void TickParkedResume()
    {
        if (parkedResumeDone || Context.IsMainPlayer || !Context.IsWorldReady || status is null || status.Day != Game1.Date.TotalDays) return;
        if (status.ResumedAtOwnClock?.Contains(Game1.player.UniqueMultiplayerID) != true) return;
        if (Game1.eventUp || Game1.isFestival() || Game1.activeClickableMenu is not null || Game1.isWarping || nights.Busy || holdActive) return;
        parkedResumeDone = true;
        var spot = Parked.Of(Game1.player);
        if (spot is null) { Monitor.Log("Back in your turn. No spot was kept, so you stay where the game put you.", LogLevel.Debug); return; }
        Parked.Clear(Game1.player);
        if (spot.Day != Game1.Date.TotalDays) { Monitor.Log($"The spot kept for you ({Parked.Words(spot)}) is from another day and is not used.", LogLevel.Info); return; }
        try
        {
            Game1.warpFarmer(spot.Location, spot.TileX, spot.TileY, spot.Facing);
            Monitor.Log($"Back where you left your turn: {Parked.Words(spot)}.", LogLevel.Info);
        }
        catch (Exception ex) { Monitor.Log($"Could not take you back to {Parked.Words(spot)}: {ex.Message}", LogLevel.Warn); }
    }

    // ---- 0.1.29: Save my turn ----

    private void RequestSaveTurn()
    {
        if (!CanSaveTurn || status is null) return;
        Helper.Multiplayer.SendMessage(new FixRequest { Day = status.Day, Kind = "savenow", Target = Game1.player.UniqueMultiplayerID }, "fix",
            new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        Game1.addHUDMessage(new HUDMessage("Asked the farm to save your turn.") { noIcon = true });
    }

    /// <summary>
    /// The host asks every connected player to freeze for a save of the turn (see HeldSave).
    /// <paramref name="byRequest"/>: a player pressed Save my turn, who is told how it went;
    /// otherwise the periodic save (SaveTurnEveryMinutes).
    /// </summary>
    private void BeginHeldSave(long asker, bool byRequest)
    {
        if (!Context.IsMainPlayer || coordinator is null || error is not null || savingCheckpoint || heldSave.Holding || Released) return;
        var s = coordinator.Snapshot;
        if (s.Phase != DayPhase.Open) return;
        if (byRequest && s.Farmers.FirstOrDefault(f => f.Id == asker) is not { IsOnline: true, Status: FarmerStatus.Pending }) return;
        long me = Game1.player.UniqueMultiplayerID;
        var remotes = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).Where(id => id != me).Distinct().ToArray();
        if (!heldSave.Begin(asker, remotes, DateTime.UtcNow)) return;
        heldSaveToken++;
        heldSaveByRequest = byRequest;
        Helper.Multiplayer.SendMessage(new HoldMessage { Day = s.AbsoluteDay, Token = heldSaveToken }, "hold", new[] { ModManifest.UniqueID }, remotes);
        string who = s.Farmers.FirstOrDefault(f => f.Id == asker)?.Name ?? asker.ToString();
        Monitor.Log($"Holding {remotes.Length} player{(remotes.Length == 1 ? "" : "s")} still for a save of the turn ({(byRequest ? who + " asked" : "SaveTurnEveryMinutes")}).", LogLevel.Info);
    }

    private void TickHeldSave()
    {
        var step = heldSave.Tick(DateTime.UtcNow);
        if (step is HeldSaveStep.Nothing or HeldSaveStep.Wait) return;
        long asker = heldSave.Requester;
        if (step == HeldSaveStep.GiveUp)
        {
            Monitor.Log("Not every player answered the hold in time; nothing was saved.", LogLevel.Warn);
            if (heldSaveByRequest) Tell(asker, "Shared Day could not save now: not every player answered in time. Nothing was saved.");
            if (DayClock.IsValid(Game1.timeOfDay)) lastHeldSaveMinute = CalendarClock.ToMinutes(Game1.timeOfDay);
        }
        else SaveHeldTurn(asker);
        heldSave.End();
        long me = Game1.player.UniqueMultiplayerID;
        var remotes = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).Where(id => id != me).Distinct().ToArray();
        if (remotes.Length > 0)
            Helper.Multiplayer.SendMessage(new HoldMessage { Day = Game1.Date.TotalDays, Token = heldSaveToken }, "release", new[] { ModManifest.UniqueID }, remotes);
        Broadcast();
    }

    /// <summary>Every player is held still: the farm is written now, with each playing farmer's clock parked at this time.</summary>
    private void SaveHeldTurn(long asker)
    {
        if (coordinator is null || calendarClock is null) return;
        if (Game1.activeClickableMenu is not null && Game1.activeClickableMenu == waitingMenu)
        {
            Game1.exitActiveMenu();
            waitingMenu = null;
            waitingMenuQueued = true;
        }
        if (Game1.activeClickableMenu is not null || Game1.eventUp)
        {
            Monitor.Log("The held save was skipped: the host's screen is busy.", LogLevel.Warn);
            if (heldSaveByRequest) Tell(asker, "Shared Day could not save now: the farm is busy. Nothing was saved.");
            return;
        }
        long me = Game1.player.UniqueMultiplayerID;
        if (DayClock.IsValid(Game1.timeOfDay))
            foreach (var playing in coordinator.Snapshot.Farmers.Where(f => f.IsActive && f.Id != me).ToArray())
                coordinator.Park(playing.Id, Game1.timeOfDay);
        savingCheckpoint = true;
        try
        {
            WorldCheckpoint.Save(Helper, Monitor, coordinator.ExportJournal(), calendarClock.ExportState(), id => heldSave.IsHeld(id));
            checkpointPending = false;
            checkpointError = null;
            checkpointFailures = 0;
            if (DayClock.IsValid(Game1.timeOfDay)) lastHeldSaveMinute = CalendarClock.ToMinutes(Game1.timeOfDay);
            Monitor.Log($"Saved the turn at {DayClock.Words(Game1.timeOfDay)} with every player held still. " + Describe(), LogLevel.Info);
            if (heldSaveByRequest) Tell(asker, $"Your turn is saved ({ShortDate()}, {DayClock.Words(Game1.timeOfDay)}).");
        }
        catch (Exception ex)
        {
            Monitor.Log("The held save failed: " + ex.Message + " The farm keeps running; the night's save still writes everything.", LogLevel.Error);
            Monitor.Log(ex.ToString(), LogLevel.Trace);
            if (heldSaveByRequest) Tell(asker, "Shared Day could not save now: " + ex.Message);
        }
        finally { savingCheckpoint = false; }
    }

    /// <summary>SaveTurnEveryMinutes: the same held save, by itself, while exactly one remote farmer plays.</summary>
    private void TickPeriodicSave()
    {
        if (Config.SaveTurnEveryMinutes <= 0 || heldSave.Holding || savingCheckpoint || Released || coordinator is null || error is not null) return;
        long me = Game1.player.UniqueMultiplayerID;
        var remotes = Game1.getOnlineFarmers().Where(f => f.UniqueMultiplayerID != me).ToArray();
        if (remotes.Length != 1) return;
        var only = coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == remotes[0].UniqueMultiplayerID);
        if (only is not { IsActive: true } || !DayClock.IsValid(Game1.timeOfDay)) return;
        int now = CalendarClock.ToMinutes(Game1.timeOfDay);
        int last = lastHeldSaveMinute < 0 ? CalendarClock.MorningMinute : lastHeldSaveMinute;
        if (!HeldSave.PeriodicDue(Config.SaveTurnEveryMinutes, now, last)) return;
        BeginHeldSave(only.Id, byRequest: false);
    }

    /// <summary>
    /// A player's side of a hold: freeze (the lock does it), keep where they stand for their
    /// return (see Parked), send the health the host does not get by itself, then answer. A host
    /// that never lets go is waited for ten seconds, no more.
    /// </summary>
    private void TickHold()
    {
        if (!holdActive || Context.IsMainPlayer) return;
        if (DateTime.UtcNow - holdSince > HoldPatience)
        {
            holdActive = holdAnswered = false;
            Monitor.Log("The host did not let this player go after holding them for a save; carrying on.", LogLevel.Warn);
            return;
        }
        if (holdAnswered) return;
        // Menus close on the lock's tick; a menu the lock leaves (the farm picker) is given half a second.
        if (Game1.activeClickableMenu is not null && ++holdTicks < 30) return;
        if (Context.IsWorldReady && status is not null && Parked.Capture(Game1.player, status.Day, status.Session) is { } spot) Parked.Write(Game1.player, spot);
        SendHealth(force: true);
        Helper.Multiplayer.SendMessage(new HeldMessage { Day = Game1.Date.TotalDays, Token = holdToken }, "held", new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        holdAnswered = true;
    }

    // ---- 0.1.29: the bed's question ----

    /// <summary>The bed's question as it should read for this player now (see BedQuestion), or null for the game's own.</summary>
    private string? BedWording()
    {
        if (!Active || !Context.IsWorldReady || status is null || Released || status.HasFatalError || status.Error is not null) return null;
        long me = Game1.player.UniqueMultiplayerID;
        bool othersUp = status.Mode == PlayMode.Together && status.Roster.Any(r => r.Id != me && r.Status == FarmerStatus.Pending);
        return BedQuestion.Wording(CanFinishLocal, status.Mode, IsLastToPlay, othersUp, ShortDate());
    }

    private void TickBedWording()
    {
        string? wording = BedWording();
        if (wording == bedWording) return;
        bedWording = wording;
        Helper.GameContent.InvalidateCache(BedQuestion.Asset);
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (bedWording is not { } text || !e.NameWithoutLocale.IsEquivalentTo(BedQuestion.Asset)) return;
        e.Edit(asset => asset.AsDictionary<string, string>().Data[BedQuestion.Key] = text);
    }

    // ---- 0.1.29: absent farmers' nights ----

    /// <summary>
    /// The host, as the farm's one real night begins: every roster farmer who is not connected
    /// gets their night credited and recorded (see AbsentNights). Before the game's own night
    /// work and its save, so it is saved with that night: once, or not at all.
    /// </summary>
    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        if (!Active || !Context.IsMainPlayer || coordinator is null || error is not null) return;
        try
        {
            var s = coordinator.Snapshot;
            if (s.Phase != DayPhase.Advancing || creditedDay == s.AbsoluteDay) return;
            creditedDay = s.AbsoluteDay;
            int wentToSleep = DayClock.IsValid(Game1.timeOfDay) ? Game1.timeOfDay : DayClock.Closes;
            int credited = AbsentNights.Credit(s.Farmers.Select(f => f.Id), HostPlays, s.AbsoluteDay, wentToSleep, FarmLabelFor, Monitor);
            if (credited > 0) Monitor.Log($"Credited the night of {ShortDate()} to {credited} absent farmer{(credited == 1 ? "" : "s")}.", LogLevel.Info);
        }
        catch (Exception ex) { Monitor.Log("Could not credit absent farmers' nights: " + ex.Message, LogLevel.Warn); }
    }

    // ---- 0.1.29: removal orders ----

    /// <summary>What the turn tracker answered to the host's reports (see Barring), applied on the game thread.</summary>
    private void ApplyTrackerOrders()
    {
        foreach (var orders in tracker.TakeOrders())
        {
            foreach (long id in orders.Barred) Bar(id, "the turn tracker");
            if (orders.NewFarmers is { } allowed && allowed != newFarmersAllowed)
            {
                newFarmersAllowed = allowed;
                Barring.WriteNewFarmers(allowed);
                checkpointPending = true;
                Monitor.Log(allowed ? "New farmers may claim a cabin again: the turn tracker says so."
                    : "No new farmer may claim a cabin: the turn tracker says the group takes nobody new.", LogLevel.Info);
            }
        }
    }

    /// <summary>Removes a farmer from the farm's group: today skipped, every later roster without them, disconnected on sight, their cabin freed once they are offline.</summary>
    private void Bar(long id, string by)
    {
        if (!Context.IsMainPlayer || id == Game1.player.UniqueMultiplayerID || !barred.Add(id)) return;
        Barring.Write(barred);
        freeCabinFailed.Remove(id);
        checkpointPending = true;
        string name = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id)?.Name ?? id.ToString();
        Monitor.Log($"{name} was removed from this farm's group by {by}: they sit today out, are on no later roster, and their cabin is freed once they are offline.", LogLevel.Warn);
        if (coordinator is not null && error is null && coordinator.Snapshot.Farmers.FirstOrDefault(f => f.Id == id) is { Status: FarmerStatus.Pending }) SkipFarmer(id);
        if (Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == id)) KickLater(id);
        Broadcast();
    }

    /// <summary>
    /// Tells a removed farmer why and disconnects them a moment later, so the line reaches their
    /// chat before the connection goes (a kick in the same tick dropped it, seen in the lab).
    /// </summary>
    private void KickLater(long id)
    {
        rejectedPeers.Add(id);
        if (kicksDue.ContainsKey(id)) return;
        Tell(id, Barring.Reason);
        kicksDue[id] = 90;
    }

    private void TickKicks()
    {
        if (kicksDue.Count == 0) return;
        foreach (var (id, left) in kicksDue.ToArray())
        {
            if (left > 0) { kicksDue[id] = left - 1; continue; }
            kicksDue.Remove(id);
            if (Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == id)) Game1.server?.kick(id);
        }
    }

    /// <summary>Once a second: where every connected remote farmer stands, for the spot kept when they leave (see ParkOnLeave).</summary>
    private void RememberWhereTheyStand()
    {
        if (coordinator is null) return;
        var s = coordinator.Snapshot;
        long me = Game1.player.UniqueMultiplayerID;
        foreach (var farmer in Game1.getOnlineFarmers())
        {
            if (farmer.UniqueMultiplayerID == me) continue;
            try { if (Parked.Capture(farmer, s.AbsoluteDay, s.SessionNumber) is { } spot) lastSeen[farmer.UniqueMultiplayerID] = spot; }
            catch (Exception) { }
        }
    }

    private void Unbar(long id)
    {
        if (!barred.Remove(id)) return;
        Barring.Write(barred);
        freeCabinFailed.Remove(id);
        checkpointPending = true;
        Monitor.Log($"Farmer {id} may join this farm again from the next day's roster.", LogLevel.Info);
    }

    /// <summary>A barred farmer who is offline loses their farmhand (see Barring.FreeCabin); a failure is logged once.</summary>
    private void FreeBarredCabins()
    {
        if (barred.Count == 0 || error is not null) return;
        foreach (long id in barred.ToArray())
        {
            if (freeCabinFailed.Contains(id)) continue;
            var farmer = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id);
            if (farmer is null || farmer.isUnclaimedFarmhand || Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == id)) continue;
            string? why = Barring.FreeCabin(farmer, Monitor);
            if (why is null) { checkpointPending = true; continue; }
            freeCabinFailed.Add(id);
            Monitor.Log($"Could not free {farmer.Name}'s cabin: {why}. Use 'shared_day free_cabin {farmer.Name}' to try again.", LogLevel.Warn);
        }
    }

    /// <summary>
    /// Budget for machine time handed out by something other than the ten-minute clock: a
    /// festival ending, a cutscene, another mod. Such a grant is taken to cover now to now
    /// plus its minutes, and only the part past the day's furthest time is real; a later
    /// Relay turn replaying the same hours gets nothing. Every machine in one sweep gets the
    /// same budget; the furthest time moves on before the next clock tick.
    /// </summary>
    internal int UnscheduledMachineBudget(int minutes)
    {
        if (!Active || Released || !Context.IsMainPlayer || calendarClock is null) return -1;
        int now = CalendarClock.ToMinutes(Game1.timeOfDay);
        int target = Math.Min(now + minutes, CalendarClock.EndOfDayMinute);
        unscheduledTargetMinute = Math.Max(unscheduledTargetMinute, target);
        return Math.Max(0, target - Math.Max(now, calendarClock.HighWaterMinute));
    }

    private void CommitUnscheduledMachineTime()
    {
        if (unscheduledTargetMinute < 0) return;
        if (calendarClock is not null && !Released) calendarClock.AdvanceToMinute(unscheduledTargetMinute);
        unscheduledTargetMinute = -1;
    }

    internal void BeginMachineTick(int targetTime)
    {
        if (!Context.IsMainPlayer || calendarClock is null || Released) return;
        CommitUnscheduledMachineTime();
        machineTickTarget = targetTime;
        MachineMinutesThisTick = Math.Max(0, CalendarClock.ToMinutes(targetTime) - calendarClock.HighWaterMinute);
    }

    internal void EndMachineTick(bool succeeded)
    {
        if (MachineMinutesThisTick >= 0 && calendarClock is not null)
        {
            if (succeeded) calendarClock.AdvanceToTimeOfDay(machineTickTarget);
            else
            {
                // Something inside the game's ten-minute update threw (with a big pack, usually
                // another mod). The game carries on from here and SMAPI logs the error, so the
                // day does too: count the time the clock really reached, and hold nobody.
                clockFaults++;
                if (clockFaults <= 3 || clockFaults % 100 == 0)
                    Monitor.Log($"The game's clock update hit an error ({clockFaults} today); the error itself is in this log. Shared Day carries on.", LogLevel.Warn);
                try { if (Game1.timeOfDay >= 600) calendarClock.AdvanceToTimeOfDay(Math.Min(Game1.timeOfDay, 2600)); }
                catch (ArgumentOutOfRangeException) { }
            }
        }
        MachineMinutesThisTick = -1;
    }

    // Villagers' mornings are a nicety of Relay; the farm's day is not. Whatever stops a morning
    // being stored or replayed (a cutscene at 6 AM, a pack's villager Shared Day cannot read,
    // damaged stored data) is logged, and the turn goes on with the villagers where they are.
    /// <summary>Every villager to where their schedule has them at <paramref name="time"/> (NpcCatchUp). Never fatal.</summary>
    private void CatchUpVillagers(int time)
    {
        try { NpcCatchUp.ToClock(time, Monitor); }
        catch (Exception ex) { Monitor.Log("The villagers were left where they are: " + ex.Message, LogLevel.Warn); }
    }

    private void NpcMorning(Func<IReadOnlyList<string>> step, bool replay)
    {
        try { LogInexactVillagers(step(), replay); }
        catch (Exception ex)
        {
            Monitor.Log((replay
                ? "Villagers were left where they are for this turn instead of going back to their morning: "
                : "This morning's villager positions were not stored, so a later turn today starts with villagers where they are: ")
                + ex.Message, LogLevel.Warn);
        }
    }

    // A villager whose 6 AM state cannot be replayed exactly (an animation under way, a
    // modded villager's own behavior) is reset to their morning spot and schedule instead.
    private void LogInexactVillagers(IReadOnlyList<string> names, bool replay)
    {
        if (names.Count == 0) return;
        Monitor.Log((replay
            ? "Started at their morning spot without replaying an exact 6 AM animation: "
            : "Could not store the exact 6 AM state of: ") + string.Join(", ", names.Distinct())
            + ". They follow their schedule from there; nothing else is affected.", LogLevel.Info);
    }

    private void Fail(string message)
    {
        error = message;
        Monitor.Log(message, LogLevel.Error);
        if (Context.IsMainPlayer) notifier.Paused(FarmName, message);
        Broadcast();
    }
}

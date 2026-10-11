using HarmonyLib;
using ManyFarms.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Buildings;
using StardewValley.GameData.WorldMaps;
using StardewValley.Locations;
using StardewValley.Menus;

namespace ManyFarms.Mod;

/// <summary>
/// Many Farms: several farms in one game. The host decides who farms where and keeps the farms;
/// each player picks a farm when they join, and a totem set down in the world takes anyone to any of them.
/// </summary>
public sealed class ModEntry : StardewModdingAPI.Mod
{
    internal const int Protocol = 1;
    internal static ModEntry Instance = null!;
    internal ModConfig Config = new();

    private RulesMessage rules = new();
    private bool rulesKnown;
    /// <summary>Why the mod cannot run in this session, when it cannot.</summary>
    private string? broken;
    private bool asked;
    /// <summary>On the host: this farm has the plots (the host asked for them, or its save already has extra farms).</summary>
    private bool hostActive;
    /// <summary>On the host: the houses were taken out by hand in this session and are not stood again in it.</summary>
    private bool housesRemoved;
    internal bool HousesHeldBack => housesRemoved;

    /// <summary>Takes every house out and stands none again in this session (the host's "many_farms houses remove").</summary>
    internal int RemoveHouses()
    {
        housesRemoved = true;
        return Houses.RemoveAll();
    }
    private int ticksInWorld;
    private Multiplayer? multiplayer;
    private readonly Dictionary<long, string> unwelcome = new();
    /// <summary>On a player: why the host turned this game away, until it has been shown on the title screen.</summary>
    private string? turnedAway;
    private int titleTicks;
    /// <summary>This session's player has been told once what walking off a plot means for them (per screen: split-screen has two players).</summary>
    private readonly PerScreen<bool> toldAboutRoads = new();
    /// <summary>They have been told once that a road brought them to their own farm.</summary>
    private readonly PerScreen<bool> toldRoadHome = new();
    /// <summary>
    /// The farm this player last walked off straight onto a road, and which road (the Bus Stop, the
    /// forest or the backwoods), until they reach anywhere else: turning round leads back in.
    /// </summary>
    private readonly PerScreen<(string Farm, string Road)?> walkedOff = new();
    /// <summary>The rooms this player entered by a house's door and where that door is, until they leave those rooms.</summary>
    private readonly PerScreen<Houses.Entered?> enteredBy = new();
    /// <summary>
    /// This player walked into the rooms they are in by those rooms' own door on the game's own farm.
    /// False when they woke up, joined, passed out or were put to bed there, or came in by a plot's house.
    /// </summary>
    private readonly PerScreen<bool> cameInByOwnDoor = new();
    /// <summary>How long this player's own farm has had no house, in ticks; they are told once why.</summary>
    private readonly PerScreen<int> noHouseTicks = new();
    private readonly PerScreen<bool> toldNoHouse = new();
    /// <summary>This player's house upgrade when last looked at (nights left, level): a change is told once to a farmer with a house on a farm of their own.</summary>
    private readonly PerScreen<(int DaysLeft, int Level)?> upgradeSeen = new();
    /// <summary>Shared Day's API, when Shared Day is installed and has one. It knows whether anybody plays the host's farmer.</summary>
    private ISharedDayApi? sharedDay;
    private bool sharedDayFailed;
    /// <summary>Shared Day's second API (0.1.29): whether it counts an absent farmer's nights for them. Null for an older Shared Day, or none.</summary>
    private ISharedDayNightsApi? sharedDayNights;
    private bool sharedDayNightsFailed;
    /// <summary>The picker is open for a player with no farm: closing it without picking is answered with how to get it back.</summary>
    private bool pickerOpenUnpicked;
    /// <summary>
    /// The farm the host just said is this player's, until the game's own syncing shows it here. The
    /// host's word arrives a moment before the farm's list of farmers does, and a menu worded in that
    /// moment (Shared Day's popup comes back at once for a held player) must not say they have no farm.
    /// </summary>
    private string? pickedFarm;
    private int pickedTicks;

    /// <summary>This player has a farm of their own, counting one the host has just granted.</summary>
    private bool HasFarmNow => Farms.ChosenBy(Me) is not null || pickedFarm is not null;

    private long Me => Game1.player.UniqueMultiplayerID;
    private Multiplayer Net => multiplayer ??= Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();

    /// <summary>The farm this game is in has room for more farms: its host has Many Farms switched on.</summary>
    internal bool Active => broken is null && rulesKnown && rules.Enabled;

    /// <summary>Somebody plays the host's farmer (the host's own setting; a player learns it with the rules).</summary>
    internal bool HostPlays => Context.IsMainPlayer ? HostVisibility.Shown(Config.HostPlays, SharedDaySaysHostPlays()) : !rulesKnown || rules.HostPlays;

    /// <summary>
    /// On the host: what Shared Day says about the host's own farmer, or null when Shared Day is
    /// absent, switched off or too old to say. A host PC that only hosts (Shared Day's IncludeHost
    /// false with nobody sitting at it) is nobody's farmer, whatever this mod's own switch says.
    /// </summary>
    private bool? SharedDaySaysHostPlays()
    {
        if (sharedDay is null || sharedDayFailed) return null;
        try { return sharedDay.IsActive ? sharedDay.HostFarmerPlays : null; }
        catch (Exception ex)
        {
            sharedDayFailed = true;
            Monitor.Log("Many Farms could not ask Shared Day who plays the host's farmer and goes by its own HostPlays setting. " + ex.Message, LogLevel.Warn);
            return null;
        }
    }

    /// <summary>
    /// Shared Day (0.1.29 or later) is running this farm and counts an absent farmer's nights for
    /// them: a house upgrade goes on being built while its farmer is out of the game, so Robin stays
    /// at their house (<see cref="Builder.Upgraders"/>) and the farmer is not told to stay the night.
    /// False without Shared Day, with an older one, or when it is not running the farm.
    /// </summary>
    internal bool AbsentNightsCredited
    {
        get
        {
            if (sharedDayNights is null || sharedDayNightsFailed || sharedDay is null || sharedDayFailed) return false;
            try { return sharedDay.IsActive && sharedDayNights.CreditsAbsentNights; }
            catch (Exception ex)
            {
                sharedDayNightsFailed = true;
                Monitor.Log("Many Farms could not ask Shared Day whether it counts an absent farmer's nights, and goes by the game's own count. " + ex.Message, LogLevel.Warn);
                return false;
            }
        }
    }

    /// <summary>
    /// For other mods (<see cref="IManyFarmsNightsApi"/>): the name of the farm this farmer has of
    /// their own ("Alice's farm"), or null when they have none, so a night played for them in their
    /// absence can be titled with it.
    /// </summary>
    internal string? FarmLabelFor(long farmerId)
    {
        try { return Context.IsWorldReady && Active && Patches.OwnPlot(farmerId) is { } own ? Farms.Label(own) : null; }
        catch (Exception ex)
        {
            Patches.Warn("naming a farmer's farm for another mod", ex);
            return null;
        }
    }

    /// <summary>The key of Shared Day's menu when that menu offers the picker, else null.</summary>
    private string? TurnMenuKey()
    {
        if (sharedDay is null || sharedDayFailed) return null;
        try { return sharedDay.IsActive && sharedDay.OffersFarmPicker && sharedDay.MenuKeyName is { Length: > 0 } key ? key : null; }
        catch (Exception) { return null; }
    }

    /// <summary>The player may pick a farm now: they have none yet, or the host lets players change.</summary>
    internal bool CanChoose => Active && (!HasFarmNow || rules.AllowSwitching);
    internal bool CanStartFarm => Active && rules.AllowNewFarms && Farms.FreeCount() > 0 && AllowedKinds().Count > 0;

    /// <summary>The host lets players start farms at all (whether or not a plot is left).</summary>
    internal bool HostAllowsNewFarms => rules.AllowNewFarms;

    // ---- for other mods (see Api.cs) ----

    public override object? GetApi() => new ManyFarmsApi(this);

    internal bool CanOpenPicker => broken is null && Context.IsWorldReady && CanChoose && Game1.player.isCustomized.Value
        && Farms.Main is not null && Farms.Pool().Any() && PickerRules.CanOpenNow(Game1.eventUp, Fading, Travel.InProgress);

    /// <summary>The screen is fading or the player is between two maps.</summary>
    private static bool Fading => Game1.fadeToBlack || Game1.globalFade || Game1.isWarping;

    /// <summary>Shared Day holds this player where they stand until the next day (their day is done).</summary>
    private bool HeldByTurnMod()
    {
        if (sharedDay is null || sharedDayFailed) return false;
        try { return sharedDay.IsActive && sharedDay.HoldsPlayer; }
        catch (Exception) { return false; }
    }
    internal string PickerLabel => PickerWords.OpenChoice(Context.IsWorldReady && HasFarmNow, Context.IsWorldReady && CanStartFarm);
    internal string PickerHelp => PickerWords.OpenChoiceHelp(Context.IsWorldReady && HasFarmNow, Context.IsWorldReady && CanStartFarm);

    internal bool OpenPicker()
    {
        if (!CanOpenPicker || Game1.activeClickableMenu is not null) return false;
        OpenMenu(MenuMode.Choose);
        return Game1.activeClickableMenu is FarmMenu;
    }

    internal List<FarmKind> AllowedKinds() =>
        FarmKinds.All().Where(kind => rules.FarmTypes.Length == 0 || rules.FarmTypes.Contains(kind.Id)).ToList();

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();
        broken = Patches.Install(new Harmony(ModManifest.UniqueID), Monitor);
        if (broken is not null)
            Monitor.Log("Many Farms could not hook into this version of the game and is switched off: " + broken, LogLevel.Error);

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.DayEnding += (_, _) => Builder.Night(running: true);
        helper.Events.GameLoop.Saving += (_, _) => { try { Builder.BeforeSave(Monitor); } catch (Exception ex) { Patches.Warn("taking Robin home before a save", ex); } };
        helper.Events.GameLoop.UpdateTicked += OnTick;
        helper.Events.GameLoop.ReturnedToTitle += OnTitle;
        helper.Events.Player.Warped += OnWarped;
        helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        helper.Events.Multiplayer.PeerDisconnected += (_, e) => unwelcome.Remove(e.Peer.PlayerID);
        helper.Events.Multiplayer.ModMessageReceived += OnMessage;
        helper.Events.Input.ButtonPressed += OnButton;
        helper.ConsoleCommands.Add("many_farms", "Many Farms: list | kinds | start <kind> [farmer] | assign <farmer> <main or plot 1-8> | totem | place [main or plot 1-8] | houses [remove] | menu", OnCommand);
    }

    // ---- what the game is told exists ----

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        // With its hooks missing the plots would be bare farms with a second farmhouse each; leave the game as it was.
        if (broken is not null) return;
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Locations"))
        {
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, LocationData>().Data;
                foreach (int slot in FarmSlots.All)
                {
                    // Every plot is the game's own Farm class, on the standard map until someone starts it.
                    data[FarmSlots.FarmName(slot)] = new LocationData
                    {
                        CreateOnLoad = new CreateLocationData { MapPath = "Maps\\Farm", Type = "StardewValley.Farm", AlwaysActive = true },
                    };
                    data[FarmSlots.CaveName(slot)] = new LocationData
                    {
                        CreateOnLoad = new CreateLocationData { MapPath = "Maps\\FarmCave", Type = "StardewValley.Locations.FarmCave" },
                    };
                }
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
        {
            // The Farmstead Totem is a thing that stands in the world, like a scarecrow: set it down, walk up, use it.
            e.Edit(asset => asset.AsDictionary<string, BigCraftableData>().Data[Keys.TotemId] = new BigCraftableData
            {
                Name = "Farmstead Totem",
                DisplayName = "Farmstead Totem",
                Description = "Set it down anywhere. Anyone who uses it can go to any farm in the valley.",
                Price = 0,
                CanBePlacedOutdoors = true,
                CanBePlacedIndoors = true,
                Texture = Keys.TotemTexture,
                SpriteIndex = 0,
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
        {
            // 15 wood and 10 fiber for another totem. Not known from the start: a farmer learns it on a farm that has extra farms.
            e.Edit(asset => asset.AsDictionary<string, string>().Data[Keys.TotemId] = $"388 15 771 10/Home/{Keys.TotemId}/true/null");
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Buildings"))
        {
            // The house on a plot: the main farmhouse's picture and shape (0.5.0; the cabin's when the game has no usable
            // Farmhouse entry), with no rooms of its own. On every game, like the totem.
            // Late, so a mod that reskins or reshapes the farmhouse or the cabin has done so first.
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, BuildingData>().Data;
                var farmhouse = data.TryGetValue(Houses.FarmhouseEntry, out var fh) ? fh : null;
                var cabin = data.TryGetValue(Houses.CabinEntry, out var cb) ? cb : null;
                if (Houses.DataFrom(farmhouse, cabin, out string? madeFrom) is { } house)
                {
                    data[Houses.TypeId] = house;
                    if (madeFrom != Houses.FarmhouseEntry)
                        Monitor.LogOnce("Many Farms: the game has no usable Farmhouse entry in Data/Buildings, so the house on a farm is made from the Cabin entry and looks like a cabin.", LogLevel.Warn);
                }
                else Monitor.LogOnce("Many Farms: the game has no usable Farmhouse or Cabin entry in Data/Buildings, so new farms get no house (their land, totem and roads work).", LogLevel.Warn);
            }, AssetEditPriority.Late);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/WorldMap"))
        {
            // A player standing on a plot is on the world map, where the farm is. Late, so a mod that redraws the map has done so first.
            e.Edit(asset => WorldMapPlots.AddTo(asset.AsDictionary<string, WorldMapRegionData>().Data), AssetEditPriority.Late);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo(Keys.TotemTexture))
        {
            e.LoadFromModFile<Texture2D>("assets/totem.png", AssetLoadPriority.Exclusive);
        }
    }

    private void OnLaunched(object? sender, GameLaunchedEventArgs e)
    {
        if (broken is not null) return;
        ConfigMenu.Register(Helper, ModManifest, () => Config, () => Helper.WriteConfig(Config), Monitor, MainFarmName);
        // The door of a plot's house. Registered by name, so it needs no patch on the game's buildings.
        GameLocation.RegisterTileAction(Houses.DoorAction, Houses.UseDoor);
        // Shared Day is optional and so is its API: one too old to have it leaves this null.
        try { sharedDay = Helper.ModRegistry.GetApi<ISharedDayApi>("CarbonNode.SharedDay"); }
        catch (Exception ex) { Monitor.Log("Many Farms could not read Shared Day's API and goes by its own settings. " + ex.Message, LogLevel.Warn); }
        // Its second API (0.1.29), read on its own: a Shared Day without it leaves this null and an absent farmer's upgrade waits for them, as the game has it.
        try { sharedDayNights = Helper.ModRegistry.GetApi<ISharedDayNightsApi>("CarbonNode.SharedDay"); }
        catch (Exception ex) { Monitor.Log("Many Farms could not read Shared Day's nights API (an older Shared Day), so an absent farmer's house upgrade waits for them, as the game has it. " + ex.Message, LogLevel.Info); }
        // The game may have read its locations before this mod could add to them.
        if (Game1.locationData is null || !Game1.locationData.ContainsKey(FarmSlots.FarmName(1)))
        {
            Helper.GameContent.InvalidateCache("Data/Locations");
            Game1.locationData = DataLoader.Locations(Game1.content);
        }
        if (!Game1.locationData.ContainsKey(FarmSlots.FarmName(1)))
        {
            broken = "the game's list of locations does not take this mod's farm plots";
            Monitor.Log("Many Farms is switched off: " + broken + ".", LogLevel.Error);
        }
    }

    // ---- a save, a day, a tick ----

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        ticksInWorld = 0;
        asked = false;
        rulesKnown = false;
        pickerOpenUnpicked = false;
        pickedFarm = null;
        ForgetWays();
        unwelcome.Clear();
        FarmKinds.Forget();
        Houses.ForgetLevels();
        Houses.ForgetRestood();
        Builder.Forget();
        if (broken is not null)
        {
            if (Context.IsMainPlayer && Config.Enabled)
            {
                // A save with extra farms must not be saved by a game that cannot see them: the game drops what it does not know.
                Monitor.Log("Many Farms cannot run (" + broken + "). Returning to the title so a farm with extra farms is not saved without them.", LogLevel.Error);
                Game1.ExitToTitle(null);
            }
            return;
        }
        if (Context.IsMainPlayer)
        {
            // On for a host who asked for it, and for any save that already has extra farms.
            bool hasFarms = Farms.Pool().Any(Farms.IsStarted);
            hostActive = Config.Enabled || hasFarms;
            if (hasFarms && !Config.Enabled)
                Monitor.Log("This save has extra farms, so Many Farms stays on for it although Enabled is false in its config.json.", LogLevel.Info);
            if (hostActive)
            {
                try { Founder.Prepare(Monitor); }
                catch (Exception ex) { Monitor.Log("Many Farms could not prepare its farm plots: " + ex, LogLevel.Error); }
                // A farm started before 0.3.0 gets its house here, in place; a house stood before 0.5.0 (a cabin's shape) is stood again here, once, in the farmhouse's.
                Founder.EnsureHouses(Monitor);
            }
            else RemovePlots();
            rules = HostRules();
            rulesKnown = true;
        }
        GiveTotem();
    }

    /// <summary>
    /// A host with Many Farms off: the plots the game made while loading are taken out again before
    /// anything is saved or sent, so the farm and its save are exactly the game's own.
    /// </summary>
    private void RemovePlots()
    {
        foreach (int slot in FarmSlots.All)
        {
            foreach (string name in new[] { FarmSlots.FarmName(slot), FarmSlots.CaveName(slot) })
            {
                if (Game1.getLocationFromNameInLocationsList(name) is not { } plot) continue;
                Game1.locations.Remove(plot);
                Game1.removeLocationFromLocationLookup(plot);
            }
        }
    }

    /// <summary>A host whose save has gained an extra farm uses Many Farms from now on, and its config says so.</summary>
    internal void RememberEnabled()
    {
        if (Config.Enabled) return;
        Config.Enabled = true;
        try { Helper.WriteConfig(Config); }
        catch (Exception ex) { Monitor.Log("Many Farms could not save its config.json. " + ex.Message, LogLevel.Warn); }
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (broken is not null) return;
        // The night is over and saved: Robin may be looked for again.
        Builder.Night(running: false);
        // Everybody woke up in their own rooms: nobody "came in" by any door today yet.
        enteredBy.Value = null;
        cameInByOwnDoor.Value = false;
        if (Context.IsMainPlayer && hostActive)
        {
            foreach (int slot in FarmSlots.All)
                if (Farms.Slot(slot) is { } farm && Farms.IsStarted(farm)) Founder.PrepareCave(slot);
            // A house that could not be stood (something was in the way) is tried again every morning.
            Founder.EnsureHouses(Monitor);
            // Weeds that spread in the night overgrow what stands among them, a totem as much as a scarecrow (the
            // game's own rule, five times over on the first of a month), and a new farm is all weeds. On a farm
            // with extra farms every farm wakes up with a totem by its arrival point, in front of the house:
            // if none stands within reach of it, one is stood there again. Totems elsewhere are left alone.
            var started = Farms.Started();
            if (started.Count > 1)
            {
                foreach (var farm in started)
                    foreach (var (door, at) in Founder.StandTotems(farm))
                        Monitor.Log(at is { } tile ? $"Many Farms: no totem stood by the door at {door.X}, {door.Y} on {Farms.Label(farm)} ({farm.Name}) this morning, so one stands at {tile.X}, {tile.Y} now."
                            : $"Many Farms: no totem stands by the door at {door.X}, {door.Y} on {Farms.Label(farm)} ({farm.Name}) and there is no free tile near it. Place one by hand.", at is null ? LogLevel.Warn : LogLevel.Info);
            }
        }
        GiveTotem();
    }

    private void OnTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        rulesKnown = false;
        hostActive = false;
        housesRemoved = false;
        asked = false;
        ticksInWorld = 0;
        titleTicks = 0;
        toldAboutRoads.Value = false;
        toldRoadHome.Value = false;
        toldNoHouse.Value = false;
        ForgetWays();
        pickerOpenUnpicked = false;
        pickedFarm = null;
        unwelcome.Clear();
        FarmKinds.Forget();
        Houses.ForgetLevels();
        Builder.Forget();
    }

    private void ForgetWays()
    {
        upgradeSeen.Value = null;
        walkedOff.Value = null;
        enteredBy.Value = null;
        cameInByOwnDoor.Value = false;
        noHouseTicks.Value = 0;
    }

    internal string MainFarmName() => Context.IsWorldReady && Farms.Main is { } main ? Farms.Label(main) : "the main farm";

    private static bool IsRoad(string? name) => FarmEntry.RoadSentinel(name) is not null;

    /// <summary>The farm this player walked off straight onto this road, having been nowhere else since; null otherwise.</summary>
    internal string? TurnedRoundFrom(string road) => walkedOff.Value is { } left && left.Road == road ? left.Farm : null;

    /// <summary>A house's door was used: stepping out of those rooms sets the player down in front of it.</summary>
    internal void EnteredByHouse(Houses.Entered entered) => enteredBy.Value = entered;

    /// <summary>
    /// Where the door of the rooms a player stands in sets them down, when that is not where the
    /// game would: in front of the house they came in by; or, when they woke up, joined or were put
    /// to bed in their own rooms and came in by no door, in front of the house on their own farm.
    /// Null leaves the game's own way out (their cabin's door on the game's own farm): they came in
    /// by that door, they have no farm of their own, or it has no house.
    /// </summary>
    internal (string Location, Point Step)? DoorOut(FarmHouse rooms, Farm? own)
    {
        string name = rooms.NameOrUniqueName;
        if (enteredBy.Value is { } entered && entered.Home == name) return (entered.Location, entered.Step);
        if (cameInByOwnDoor.Value) return null;
        if (own is null || Game1.player.homeLocation.Value != name || Houses.For(own) is not { } house) return null;
        return (house.In.NameOrUniqueName, Houses.DoorStep(house.House));
    }

    /// <summary>A road, a Warp Totem: Farm or the Return Scepter has just brought this player to their own farm: said once per session.</summary>
    internal void CameHomeByRoad(Farm plot)
    {
        if (toldRoadHome.Value) return;
        toldRoadHome.Value = true;
        Game1.addHUDMessage(new HUDMessage(RoadWords.RoadHomeShort, HUDMessage.newQuest_type));
        Game1.chatBox?.addInfoMessage(RoadWords.RoadHome(MainFarmName()));
    }

    /// <summary>
    /// Keeps track of how this player moves between a farm, its roads and a farmer's rooms, and
    /// tells them once per session what walking off a plot means for them.
    /// </summary>
    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (broken is not null || !e.IsLocalPlayer) return;
        try
        {
            var to = e.NewLocation;
            var from = e.OldLocation;
            bool onFoot = !Travel.InProgress;

            // Into a farmer's rooms: remember by which way (the cellar below is part of the same rooms).
            if (to is FarmHouse)
            {
                if (from is not Cellar) cameInByOwnDoor.Value = from is Farm outside && !Farms.IsPool(outside) && enteredBy.Value is null;
            }
            else if (to is not Cellar) enteredBy.Value = null;

            // Off a farm straight onto a road: turning round leads back in. Anywhere else forgets it.
            if (from is Farm left && Farms.IsStarted(left) && IsRoad(to?.Name) && onFoot) walkedOff.Value = (left.Name, to!.Name);
            else if (walkedOff.Value is { } was && to?.Name != was.Road) walkedOff.Value = null;

            if (toldAboutRoads.Value || !Active || !onFoot) return;
            // Only a walk onto one of the three roads: "turn straight round" is untrue after a warp totem, a minecart
            // or a door (the plot's cave, its buildings, a farmer's rooms), and saying it there would use the one telling up.
            if (from is not Farm plot || !Farms.IsPool(plot) || !Farms.IsStarted(plot) || to is null || !IsRoad(to.Name)) return;
            var own = Farms.ChosenBy(Me);
            bool ownPlot = own is not null && Farms.IsPool(own);
            string leftName = Farms.Farmers(plot).Count == 0 ? "a farm nobody farms yet" : Farms.Label(plot);
            string? line = own == plot
                // Their roads lead home, so there is nothing to warn about.
                ? (Config.RoadsLeadHome ? null : RoadWords.WalkedOffOwn(MainFarmName()))
                : RoadWords.VisitorLeft(leftName, ownPlot && Config.RoadsLeadHome ? "your own farm" : MainFarmName());
            if (line is null) return;
            toldAboutRoads.Value = true;
            // The game's message box is one line as wide as its words, so it gets the short form and the chat the whole of it.
            Game1.addHUDMessage(new HUDMessage(own == plot ? RoadWords.WalkedOffOwnShort : RoadWords.VisitorLeftShort(leftName), HUDMessage.newQuest_type));
            Game1.chatBox?.addInfoMessage(line);
        }
        catch (Exception ex) { Patches.Warn("following a farmer between farms", ex); }
    }

    /// <summary>
    /// A farmer whose farm has a house is told so once (a farm started before 0.3.0 had none), and
    /// one whose house could not be stood is told why once per session, after the host has had time to stand it.
    /// </summary>
    private void TellAboutHouse()
    {
        if (!Active || Game1.player is not { } who || !who.isCustomized.Value || Game1.eventUp) return;
        if (Farms.ChosenBy(Me) is not { } own || !Farms.IsPool(own)) { noHouseTicks.Value = 0; return; }
        if (Houses.For(own) is not null)
        {
            noHouseTicks.Value = 0;
            if (who.modData.ContainsKey(Keys.ToldHouse) || !Context.IsPlayerFree) return;
            who.modData[Keys.ToldHouse] = "1";
            Game1.chatBox?.addInfoMessage(RoadWords.NewHouse(MainFarmName()));
            return;
        }
        noHouseTicks.Value += 30;
        // Twenty seconds: a house stood with the farm reaches a player's game a moment after the host's word does.
        if (noHouseTicks.Value < 1200 || toldNoHouse.Value || !Context.IsPlayerFree) return;
        toldNoHouse.Value = true;
        Game1.chatBox?.addInfoMessage(RoadWords.HouseBlocked);
    }

    /// <summary>
    /// A farmer with a house on a farm of their own who has just paid Robin for a house upgrade is
    /// told where she builds it and when it is ready, and told again the morning it is done. An
    /// ordinary game says neither, and there the cabin is the only house a farmer has. What they
    /// last saw is kept on the farmer (0.5.0), so an upgrade finished while they were away (Shared
    /// Day counting their nights for them) is told on their first look back, not kept quiet as
    /// something that was already so.
    /// </summary>
    private void TellAboutUpgrade()
    {
        if (Game1.player is not { } who || !who.isCustomized.Value) return;
        int daysLeft = who.daysUntilHouseUpgrade.Value, level = who.HouseUpgradeLevel;
        bool house = Patches.OwnPlot(Me) is { } own && Houses.For(own) is not null;
        // The first look of a session starts from the farmer's own memory of it; a farmer who never looked has none, and is told nothing of what is already so.
        var before = upgradeSeen.Value ?? UpgradeMemory.Read(who.modData.TryGetValue(Keys.UpgradeSeen, out string kept) ? kept : null);
        string? line = HouseWords.About(before, daysLeft, level, house, MainFarmName(), farmhouse: Context.IsMainPlayer, nightsCountedWhileAway: AbsentNightsCredited);
        if (line is not null)
        {
            // Robin is still talking, or the night is still passing: said when the farmer can read it.
            if (!Context.IsPlayerFree || Game1.eventUp) return;
            if (level == before!.Value.Level) Game1.addHUDMessage(new HUDMessage(HouseWords.OrderedShort, HUDMessage.newQuest_type));
            Game1.chatBox?.addInfoMessage(line);
        }
        upgradeSeen.Value = (daysLeft, level);
        // Written only when it changes: the farmer's modData is synced and saved, and this is looked at twice a second.
        string now = UpgradeMemory.Keep(daysLeft, level);
        if (!who.modData.TryGetValue(Keys.UpgradeSeen, out string had) || had != now) who.modData[Keys.UpgradeSeen] = now;
    }

    private void OnTick(object? sender, UpdateTickedEventArgs e)
    {
        if (broken is not null) return;
        // Between two ticks no call of the game's is under way, so no farm scope may be open.
        FarmScope.CheckIdle(Monitor);
        if (!Context.IsWorldReady)
        {
            ShowWhyTurnedAway();
            return;
        }
        TellWayBack();
        // The farm's own list of farmers has caught up with the host's word (or ten seconds have gone by).
        if (pickedFarm is not null && (Farms.ChosenBy(Me)?.Name == pickedFarm || ++pickedTicks > 600)) pickedFarm = null;
        if (!e.IsMultipleOf(30)) return;
        ticksInWorld += 30;
        if (ticksInWorld >= 240) TellAboutHouse();
        if (Active)
        {
            Houses.RefreshLevels();
            TellAboutUpgrade();
        }
        if (Context.IsMainPlayer)
        {
            if (!hostActive) return;
            // Robin works at the house of a farmer whose upgrade she is building (see Builder).
            if (ticksInWorld % 60 == 0)
            {
                try { Builder.Tend(Monitor); }
                catch (Exception ex) { Patches.Warn("sending Robin to a house upgrade", ex); }
            }
            // Whether anybody plays the host's farmer can change while the farm is up (somebody sits down
            // at the host PC, or gets up): the players' menus go by what the host last told them.
            if (HostPlays != rules.HostPlays)
            {
                rules = HostRules();
                Helper.Multiplayer.SendMessage(rules, "rules", new[] { ModManifest.UniqueID });
            }
            if (ticksInWorld % 600 == 0) GiveTotem();
            TurnAway();
            // The community centre's pantry repairs every farm's greenhouse, as it repairs the one the game has.
            if (Farms.Main is { } main && main.greenhouseUnlocked.Value)
            {
                foreach (var farm in Farms.Pool())
                    if (Farms.IsStarted(farm) && !farm.greenhouseUnlocked.Value) farm.greenhouseUnlocked.Value = true;
            }
            return;
        }
        if (!rulesKnown)
        {
            // The host's rules are sent on connecting; ask again if they have not arrived.
            if (ticksInWorld % 300 == 0)
                Helper.Multiplayer.SendMessage("hello", "hello", new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
            return;
        }
        if (!rules.Enabled) return;
        // The recipe for a totem of their own (a no-op once they know it).
        GiveTotem();
        // A player with no farm yet is asked once, ever, when they are standing free in the world (0.5.2:
        // "doesnt need to popup everytime i join"; it used to be once per session). Not in the middle of a
        // festival: the question waits until it is over. The farmer remembers being asked, so the
        // picker then only opens from the menu or the key.
        if (JoinAsk.ShouldOpen(asked, Config.AskOnJoin, ticksInWorld >= 240 && Context.IsPlayerFree && !Game1.eventUp && Game1.player.isCustomized.Value,
            Farms.Pool().Any(), Farms.ChosenBy(Me) is not null, Game1.player.modData.ContainsKey(Keys.AskedOnJoin)))
        {
            asked = true;
            Game1.player.modData[Keys.AskedOnJoin] = "1";
            OpenMenu(MenuMode.Choose);
        }
    }

    /// <summary>
    /// A player the host turned away is back at the title with the game's own line, "the server has closed
    /// the connection". The reason the host sent is put in its place, once the game's own box is up (or
    /// after a few seconds if none comes), so they know what to do about it.
    /// </summary>
    private void ShowWhyTurnedAway()
    {
        if (turnedAway is null || Game1.gameMode != Game1.titleScreenGameMode || Game1.activeClickableMenu is not TitleMenu) return;
        titleTicks++;
        if (TitleMenu.subMenu is not ConfirmationDialog && titleTicks < 300) return;
        string reason = turnedAway;
        turnedAway = null;
        // The game's box sizes itself to the message as one line; a long one has to be wrapped first or the box stands off the screen.
        string wrapped = Game1.parseText(reason, Game1.dialogueFont, Math.Min(960, Game1.uiViewport.Width - 160));
        TitleMenu.subMenu = new ConfirmationDialog(wrapped, _ => TitleMenu.subMenu = null);
    }

    /// <summary>
    /// "ohp i closed the new farm popup and i have no way to get it back" (the owner, 2026-10-07). A
    /// player who closes the picker without picking is told at once where it is now, on screen and in
    /// chat, naming only the ways that exist for them.
    /// </summary>
    private void TellWayBack()
    {
        if (!pickerOpenUnpicked || Game1.activeClickableMenu is FarmMenu) return;
        pickerOpenUnpicked = false;
        if (!Active || Farms.ChosenBy(Me) is not null || Farms.Main is null) return;
        string? turnKey = TurnMenuKey();
        string? menuKey = Config.MenuKey != SButton.None ? Config.MenuKey.ToString() : null;
        Game1.addHUDMessage(new HUDMessage(PickerWords.WayBackShort(turnKey, menuKey), HUDMessage.newQuest_type));
        Game1.chatBox?.addInfoMessage(WayBack());
    }

    /// <summary>Where the picker is once it has been closed, for this player now.</summary>
    internal string WayBack() => Farms.Main is not { } main ? "" : PickerWords.WayBack(Farms.Label(main), TurnMenuKey(),
        Config.MenuKey != SButton.None ? Config.MenuKey.ToString() : null, Farms.Totems(main).Count > 0, CanStartFarm, Config.AskOnJoin);

    private void OnButton(object? sender, ButtonPressedEventArgs e)
    {
        if (broken is null && Config.MenuKey != SButton.None && e.Button == Config.MenuKey && Context.IsPlayerFree) OpenMenu(MenuMode.Travel);
    }

    // ---- the menu, the trip, the choice ----

    internal void OpenMenu(MenuMode mode)
    {
        if (broken is not null || !Context.IsWorldReady || Game1.activeClickableMenu is not null || Farms.Main is null) return;
        // A festival or a cutscene has the player: a trip from here would leave it running with nobody in it.
        if (Game1.eventUp) return;
        if (!Active)
        {
            if (rulesKnown) Game1.addHUDMessage(new HUDMessage("This farm keeps to one farm.", HUDMessage.error_type));
            return;
        }
        if (mode == MenuMode.Choose && !CanChoose) mode = MenuMode.Travel;
        Game1.playSound("bigSelect");
        Game1.activeClickableMenu = new FarmMenu(this, mode);
        pickerOpenUnpicked = mode == MenuMode.Choose && Farms.ChosenBy(Me) is null;
    }

    internal void TravelTo(Farm farm) => Travel.Start(farm, Net);

    /// <summary>Asks for a farm. The host answers itself; a player asks the host.</summary>
    internal void Choose(ChoiceRequest request)
    {
        if (Context.IsMainPlayer) OnChoiceResult(hostActive ? Founder.Choose(Me, request, Config, Monitor) : new ChoiceResult { Error = "This farm keeps to one farm." });
        else Helper.Multiplayer.SendMessage(request, "choose", new[] { ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }

    private void OnChoiceResult(ChoiceResult result)
    {
        bool hadMenu = Game1.activeClickableMenu is FarmMenu;
        if (Game1.activeClickableMenu is FarmMenu menu) menu.OnResult(result);
        if (!result.Ok)
        {
            if (!hadMenu) Game1.addHUDMessage(new HUDMessage(result.Error ?? "The host said no.", HUDMessage.error_type));
            return;
        }
        // They picked: no "how to get it back" when the menu closes.
        pickerOpenUnpicked = false;
        pickedFarm = result.Farm;
        pickedTicks = 0;
        if (hadMenu) Game1.exitActiveMenu();
        if (result.GaveUp)
        {
            // 0.5.1: their farm is a plot nobody farms now. A farmer standing on it (or in its cave) is set
            // down on the main farm; their cabin, bed and chests are where they always were.
            Game1.player.modData.Remove(Keys.ToldHouse);
            toldNoHouse.Value = false;
            Game1.addHUDMessage(new HUDMessage("Your farm is given up.", HUDMessage.newQuest_type));
            Game1.chatBox?.addInfoMessage(PickerWords.AfterGiveUp(MainFarmName()));
            var here = Game1.currentLocation;
            bool onIt = here is Farm plot ? Farms.IsPool(plot) && !Farms.IsStarted(plot)
                : here is StardewValley.Locations.FarmCave && FarmSlots.All.Any(slot => Farms.Cave(slot) == here && Farms.Slot(slot) is { } p && !Farms.IsStarted(p));
            if (onIt && Farms.Main is { } main)
            {
                var landing = Farms.DefaultArrival(main);
                Game1.warpFarmer(main.Name, landing.X, landing.Y, flip: false);
            }
            return;
        }
        GiveTotem();
        Game1.addHUDMessage(new HUDMessage(result.Started ? "Your new farm is ready." : "That farm is yours now.", HUDMessage.newQuest_type));
        bool mainFarm = result.Farm == FarmSlots.MainFarm;
        string totems = PickerWords.AfterPick(result.Started, mainFarm, MainFarmName(), result.House);
        // Once that line has really been said, a farmer has been told about the house (or that it could
        // not be built) and is not told again as news. One who is not shown the line is told the next morning.
        void SayWhatTheyPicked()
        {
            Game1.chatBox?.addInfoMessage(totems);
            if (mainFarm) return;
            if (result.House) Game1.player.modData[Keys.ToldHouse] = "1";
            else toldNoHouse.Value = true;
        }
        if (Game1.getLocationFromName(result.Farm) is not Farm farm || Game1.currentLocation == farm)
        {
            SayWhatTheyPicked();
            return;
        }
        // Go and see it, when they are free to. A player whose day is done is held where they stand
        // (Shared Day's popup comes straight back), and a trip under that popup left its answers dead
        // and the screen black: they are told how to get there instead, in one line, since the chat
        // lies over the foot of that popup while it shows.
        bool held = HeldByTurnMod();
        if (PickerRules.GoesThereNow(Context.IsPlayerFree, Game1.eventUp, Travel.InProgress, held))
        {
            SayWhatTheyPicked();
            TravelTo(farm);
        }
        else
        {
            if (!held) SayWhatTheyPicked();
            Game1.chatBox?.addInfoMessage(PickerRules.GoLater(held));
        }
    }

    /// <summary>
    /// The host is handed the farm's first totem, once, to set down where they like; anyone can
    /// then use it. Every farmer on a farm with extra farms knows how to make another.
    /// </summary>
    private void GiveTotem()
    {
        if (!Active || !Context.IsWorldReady || Game1.player is not { } who || !who.isCustomized.Value) return;
        if (!who.craftingRecipes.ContainsKey(Keys.TotemId)) who.craftingRecipes.Add(Keys.TotemId, 0);
        // A host nobody plays has nowhere to set one down; the farms get theirs stood by their doors.
        if (!Context.IsMainPlayer || !HostPlays || who.modData.ContainsKey(Keys.GotTotem)) return;
        // With a full pack it waits for room; this is tried again every few seconds.
        if (who.Items.ContainsId(Keys.TotemQualifiedId) || who.addItemToInventoryBool(ItemRegistry.Create(Keys.TotemQualifiedId)))
        {
            who.modData[Keys.GotTotem] = "1";
            Game1.chatBox?.addInfoMessage("You have a Farmstead Totem in your pack. Set it down where everyone can reach it: anyone who uses it can go to any farm.");
        }
    }

    // ---- the host and its players ----

    private RulesMessage HostRules() => new()
    {
        Enabled = hostActive,
        AllowNewFarms = Config.AllowNewFarms,
        AllowSwitching = Config.AllowSwitching,
        // What is true now, not only the switch: a host PC Shared Day runs with nobody at it is nobody's farmer.
        HostPlays = HostPlays,
        FarmTypes = Config.FarmTypes ?? Array.Empty<string>(),
    };

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        if (broken is not null || !Context.IsMainPlayer) return;
        if (!hostActive)
        {
            // A farm without extra farms asks nothing of its players; one who has the mod is told it is off here.
            if (e.Peer.HasSmapi && e.Peer.GetMod(ModManifest.UniqueID) is not null)
                Helper.Multiplayer.SendMessage(HostRules(), "rules", new[] { ModManifest.UniqueID }, new[] { e.Peer.PlayerID });
            return;
        }
        // A player without the same Many Farms has no plots for the host's farms to arrive in.
        string? why = !e.Peer.HasSmapi ? "they are not running SMAPI with Many Farms"
            : e.Peer.GetMod(ModManifest.UniqueID) is not { } theirs ? "they do not have Many Farms installed"
            : !theirs.Version.Equals(ModManifest.Version) ? $"they run Many Farms {theirs.Version} and the host runs {ModManifest.Version}"
            : null;
        if (why is not null)
        {
            unwelcome[e.Peer.PlayerID] = why;
            // Told before the disconnect, on the same ordered connection, so a player whose Many Farms understands
            // it can say why on their own screen. The game's own disconnect says only that the server closed the connection.
            if (e.Peer.GetMod(ModManifest.UniqueID) is { } theirMod)
                Helper.Multiplayer.SendMessage(new GoodbyeMessage { Reason = $"you run Many Farms {theirMod.Version} and this farm's host runs {ModManifest.Version}", HostVersion = ModManifest.Version.ToString() }, "goodbye", new[] { ModManifest.UniqueID }, new[] { e.Peer.PlayerID });
        }
        else Helper.Multiplayer.SendMessage(HostRules(), "rules", new[] { ModManifest.UniqueID }, new[] { e.Peer.PlayerID });
    }

    private void TurnAway()
    {
        foreach (var (id, why) in unwelcome.ToArray())
        {
            unwelcome.Remove(id);
            string name = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id)?.Name is { Length: > 0 } known ? known : "A player";
            string message = $"Many Farms disconnected {name}: {why}.";
            Monitor.Log(message + " Every player needs the host's Many Farms version.", LogLevel.Warn);
            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.error_type));
            Game1.server?.kick(id);
        }
    }

    private void OnMessage(object? sender, ModMessageReceivedEventArgs e)
    {
        if (broken is not null || e.FromModID != ModManifest.UniqueID) return;
        if (Context.IsMainPlayer)
        {
            if (unwelcome.ContainsKey(e.FromPlayerID)) return;
            if (e.Type == "hello")
                Helper.Multiplayer.SendMessage(HostRules(), "rules", new[] { ModManifest.UniqueID }, new[] { e.FromPlayerID });
            else if (e.Type == "choose")
            {
                // SMAPI says who sent it; the request only says which farm.
                var result = hostActive ? Founder.Choose(e.FromPlayerID, e.ReadAs<ChoiceRequest>(), Config, Monitor)
                    : new ChoiceResult { Error = "This farm keeps to one farm." };
                Helper.Multiplayer.SendMessage(result, "chosen", new[] { ModManifest.UniqueID }, new[] { e.FromPlayerID });
            }
        }
        else if (e.Type == "goodbye")
        {
            // Sent while this game may still be joining, before it knows who the host is.
            var goodbye = e.ReadAs<GoodbyeMessage>();
            turnedAway = $"Many Farms: you were disconnected because {goodbye.Reason}. This farm has extra farms, and every player on it needs the host's version: update the pack (Play through ModRelay) and join again.";
            titleTicks = 0;
            Monitor.Log(turnedAway, LogLevel.Warn);
        }
        else if (e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            if (e.Type == "rules")
            {
                var incoming = e.ReadAs<RulesMessage>();
                if (incoming.Protocol != Protocol)
                {
                    Monitor.Log("The host runs a different Many Farms. Install the host's version.", LogLevel.Warn);
                    return;
                }
                rules = incoming;
                rulesKnown = true;
            }
            else if (e.Type == "chosen" && Context.IsWorldReady) OnChoiceResult(e.ReadAs<ChoiceResult>());
        }
    }

    // ---- the console ----

    private void OnCommand(string command, string[] args)
    {
        if (broken is not null) { Monitor.Log("Many Farms is switched off: " + broken, LogLevel.Error); return; }
        if (!Context.IsWorldReady) { Monitor.Log("Load a farm first.", LogLevel.Warn); return; }
        try
        {
            string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            switch (verb)
            {
                case "list":
                    foreach (var farm in Farms.Started())
                    {
                        string names = string.Join(", ", Farms.Farmers(farm).Select(f => f.Name));
                        Monitor.Log($"{farm.Name}: {Farms.Label(farm)}, {Farms.KindLabel(farm)}, farmers: {(names.Length > 0 ? names : "none")}, totems: {Farms.Totems(farm).Count}", LogLevel.Info);
                    }
                    Monitor.Log($"{Farms.FreeCount()} of {FarmSlots.Count} spare plots left.", LogLevel.Info);
                    break;
                case "kinds":
                    foreach (var kind in FarmKinds.All()) Monitor.Log($"{kind.Id}: {kind.Label}", LogLevel.Info);
                    break;
                case "totem":
                    Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create(Keys.TotemQualifiedId));
                    break;
                case "place":
                    // For a host with nobody at the keyboard: stand a totem beside a farm's arrival point.
                    if (!Context.IsMainPlayer) throw new InvalidOperationException("Only the host can do that.");
                    if (!hostActive) throw new InvalidOperationException("Many Farms is off on this PC. Set Enabled to true in its config.json and load the farm again.");
                    string where = args.Length >= 2 ? args[1] : "main";
                    var on = where.Equals("main", StringComparison.OrdinalIgnoreCase) ? Farms.Main : Farms.Slot(int.Parse(where));
                    if (on is null || !Farms.IsStarted(on)) throw new InvalidOperationException($"There is no farm '{where}'. Use main or a started plot, 1 to {FarmSlots.Count}.");
                    var stoodAll = Founder.StandTotems(on);
                    if (stoodAll.Count == 0) Monitor.Log($"A totem already stands by every door of {Farms.Label(on)}.", LogLevel.Info);
                    foreach (var (door, stood) in stoodAll)
                        Monitor.Log(stood is { } at ? $"A Farmstead Totem stands on {Farms.Label(on)} at {at.X}, {at.Y}, by the door at {door.X}, {door.Y}." : $"No free tile near the door at {door.X}, {door.Y} on {Farms.Label(on)}.", stood is null ? LogLevel.Warn : LogLevel.Info);
                    break;
                case "houses":
                    if (args.Length >= 2 && args[1].Equals("remove", StringComparison.OrdinalIgnoreCase))
                    {
                        // Before going back to a version that has no houses: its game does not know the building.
                        if (!Context.IsMainPlayer) throw new InvalidOperationException("Only the host can do that.");
                        Monitor.Log($"Took {RemoveHouses()} house(s) out. They are not stood again in this session; this version stands them again the next time the farm is loaded.", LogLevel.Info);
                        break;
                    }
                    var houses = Houses.All();
                    foreach (var (house, at) in houses)
                    {
                        var step = Houses.DoorStep(house);
                        var mail = Houses.Mailbox(house);
                        Monitor.Log($"House of {Houses.PlotOf(house) ?? "no plot"}: in {at.NameOrUniqueName} at {house.tileX.Value}, {house.tileY.Value} ({house.tilesWide.Value} by {house.tilesHigh.Value}), doorstep {step.X}, {step.Y}, mailbox {mail.X}, {mail.Y}, skin {house.skinId.Value ?? "default"}, shape {(house.modData.TryGetValue(Keys.HouseShape, out string shape) ? shape : "none (stood before 0.5.0)")}.", LogLevel.Info);
                    }
                    foreach (var plot in Farms.Pool().Where(Farms.IsStarted))
                        if (Houses.For(plot) is null) Monitor.Log($"{Farms.Label(plot)} ({plot.Name}) has no house.", LogLevel.Warn);
                    Monitor.Log($"{houses.Count} house(s).", LogLevel.Info);
                    break;
                case "menu":
                    OpenMenu(MenuMode.Travel);
                    break;
                case "start" when args.Length >= 2:
                case "assign" when args.Length >= 3:
                    if (!Context.IsMainPlayer) throw new InvalidOperationException("Only the host can do that.");
                    if (!hostActive) throw new InvalidOperationException("Many Farms is off on this PC. Set Enabled to true in its config.json and load the farm again.");
                    bool start = verb == "start";
                    string who = start ? (args.Length >= 3 ? string.Join(" ", args.Skip(2)) : Game1.player.Name) : string.Join(" ", args.Skip(1).Take(args.Length - 2));
                    var match = Farms.AllFarmers().Where(f => string.Equals(f.Name, who, StringComparison.OrdinalIgnoreCase) || f.UniqueMultiplayerID.ToString() == who).ToList();
                    if (match.Count != 1) throw new InvalidOperationException($"No single farmer is named or numbered '{who}'.");
                    var request = start ? new ChoiceRequest { NewKind = args[1] }
                        : new ChoiceRequest { Farm = args[^1].Equals("main", StringComparison.OrdinalIgnoreCase) ? FarmSlots.MainFarm : FarmSlots.FarmName(int.Parse(args[^1])) };
                    // The host's own say overrides the rules it set for players.
                    var result = Founder.Choose(match[0].UniqueMultiplayerID, request, new ModConfig(), Monitor);
                    Monitor.Log(result.Ok ? $"{match[0].Name} farms {result.Farm} now." : result.Error ?? "It could not be done.", result.Ok ? LogLevel.Info : LogLevel.Warn);
                    break;
                default:
                    Monitor.Log("many_farms list | kinds | start <kind> [farmer] | assign <farmer> <main or plot 1-8> | totem | place [main or plot 1-8] | houses [remove] | menu", LogLevel.Info);
                    break;
            }
        }
        catch (Exception ex) { Monitor.Log(ex.Message, LogLevel.Error); }
    }
}

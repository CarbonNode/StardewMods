using System.Reflection;
using HarmonyLib;
using ManyFarms.Core;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Locations;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.WorldMaps;
using SObject = StardewValley.Object;

namespace ManyFarms.Mod;

/// <summary>
/// What the game needs told so that a second farm is a farm. A spare plot is the game's own Farm
/// class, so crops, crows, weeds, grass, animals, buildings and the shrine all come with it; these
/// patches cover the places where the game assumes there is one farm, of one kind.
/// </summary>
internal static class Patches
{
    private static IMonitor monitor = null!;
    private static readonly HashSet<string> warned = new();

    /// <summary>True while the game is asking which location to build or buy animals in.</summary>
    [ThreadStatic] private static bool pickingLocation;

    /// <returns>What could not be patched, or null when everything was.</returns>
    public static string? Install(Harmony harmony, IMonitor log)
    {
        monitor = log;
        var failed = new List<string>();
        void Patch(MethodBase? target, string what, string? prefix = null, string? postfix = null, string? finalizer = null)
        {
            try
            {
                if (target is null) throw new MissingMethodException(what);
                harmony.Patch(target,
                    prefix: prefix is null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix: postfix is null ? null : new HarmonyMethod(typeof(Patches), postfix),
                    finalizer: finalizer is null ? null : new HarmonyMethod(typeof(Patches), finalizer));
            }
            catch (Exception ex)
            {
                failed.Add($"{what} ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // A farm of another kind borrows the game's farm type while it does its own work.
        Patch(AccessTools.DeclaredMethod(typeof(Farm), nameof(Farm.DayUpdate)), "Farm.DayUpdate", prefix: nameof(DayUpdate_Prefix), finalizer: nameof(Kind_Finalizer));
        foreach (string name in new[] { nameof(Farm.performTenMinuteUpdate), nameof(Farm.onNewGame), nameof(Farm.GetGreenhouseStartLocation), nameof(Farm.ClearGreenhouseGrassTiles), nameof(Farm.MakeMapModifications) })
            Patch(AccessTools.DeclaredMethod(typeof(Farm), name), "Farm." + name, prefix: nameof(Kind_Prefix), finalizer: nameof(Kind_Finalizer));
        Patch(AccessTools.DeclaredMethod(typeof(GameLocation), nameof(GameLocation.GetData), new[] { typeof(string) }), "GameLocation.GetData", postfix: nameof(GetData_Postfix));
        // Which of a farm's bushes a farmer may clear is decided by the farm type when the bush is hit, not in a Farm method.
        Patch(AccessTools.DeclaredMethod(typeof(Bush), nameof(Bush.isDestroyable)), "Bush.isDestroyable", prefix: nameof(BushKind_Prefix), finalizer: nameof(Kind_Finalizer));
        // The cave of a plot nobody farms has no day either: the fruit bats leave nothing in it.
        Patch(AccessTools.DeclaredMethod(typeof(FarmCave), nameof(FarmCave.DayUpdate)), "FarmCave.DayUpdate", prefix: nameof(CaveDayUpdate_Prefix));

        // What a plot starts with, and what it is called.
        Patch(AccessTools.DeclaredMethod(typeof(Farm), nameof(Farm.AddDefaultBuildings)), "Farm.AddDefaultBuildings", prefix: nameof(AddDefaultBuildings_Prefix));
        Patch(AccessTools.DeclaredMethod(typeof(Farm), nameof(Farm.IsBuildableLocation)), "Farm.IsBuildableLocation", postfix: nameof(IsBuildableLocation_Postfix));
        Patch(AccessTools.DeclaredMethod(typeof(Farm), nameof(Farm.GetDisplayName)), "Farm.GetDisplayName", postfix: nameof(GetDisplayName_Postfix));
        Patch(AccessTools.DeclaredMethod(typeof(Farm), nameof(Farm.UpdatePatio)), "Farm.UpdatePatio", prefix: nameof(UpdatePatio_Prefix));
        Patch(AccessTools.DeclaredMethod(typeof(Farmer), nameof(Farmer.getMailboxPosition)), "Farmer.getMailboxPosition", postfix: nameof(GetMailboxPosition_Postfix));

        // Robin and Marnie work on your farm, not on everyone's.
        Patch(AccessTools.DeclaredMethod(typeof(GameLocation), nameof(GameLocation.ShowConstructOptions)), "GameLocation.ShowConstructOptions", prefix: nameof(Picking_Prefix), finalizer: nameof(Picking_Finalizer));
        Patch(AccessTools.DeclaredMethod(typeof(GameLocation), nameof(GameLocation.ShowAnimalShopMenu)), "GameLocation.ShowAnimalShopMenu", prefix: nameof(Picking_Prefix), finalizer: nameof(Picking_Finalizer));
        Patch(AccessTools.DeclaredMethod(typeof(GameLocation), nameof(GameLocation.ShowPagedResponses),
            new[] { typeof(string), typeof(List<KeyValuePair<string, string>>), typeof(Action<string>), typeof(bool), typeof(bool), typeof(int) }),
            "GameLocation.ShowPagedResponses", prefix: nameof(ShowPagedResponses_Prefix));

        // A storm is over every farm.
        Patch(AccessTools.DeclaredMethod(typeof(Utility), nameof(Utility.performLightningUpdate)), "Utility.performLightningUpdate", postfix: nameof(PerformLightningUpdate_Postfix));
        Patch(AccessTools.DeclaredMethod(typeof(Farm), "doLightningStrike"), "Farm.doLightningStrike", postfix: nameof(DoLightningStrike_Postfix));

        // Each plot has its own cave, and a totem set down in the world opens the farm menu for anyone.
        Patch(AccessTools.DeclaredMethod(typeof(Game1), nameof(Game1.warpFarmer), new[] { typeof(LocationRequest), typeof(int), typeof(int), typeof(int) }), "Game1.warpFarmer", prefix: nameof(WarpFarmer_Prefix));
        Patch(AccessTools.DeclaredMethod(typeof(SObject), nameof(SObject.checkForAction)), "Object.checkForAction", prefix: nameof(CheckForAction_Prefix));

        // Home by magic: a Warp Totem: Farm and the Return Scepter set a farmer with a farm of their own down on it.
        // These two only mark the warp they make; without them those two lead to the game's own farm, as they always
        // did, so a game that has changed them costs that and nothing else (the mod stays on).
        Optional(harmony, AccessTools.DeclaredMethod(typeof(SObject), "totemWarpForReal"), "Object.totemWarpForReal");
        Optional(harmony, AccessTools.DeclaredMethod(typeof(StardewValley.Tools.Wand), "wandWarpForReal"), "Wand.wandWarpForReal");

        InstallLayer(harmony);

        return failed.Count == 0 ? null : string.Join("; ", failed);
    }

    /// <summary>
    /// The small things a farm does that the game does for the location named "Farm" only, and the
    /// house's look. Each is optional like the two hooks above: one the game no longer has costs
    /// that one thing on a plot (the log says which) and nothing else, and the mod stays on.
    /// </summary>
    private static void InstallLayer(Harmony harmony)
    {
        void Scoped(MethodBase? target, string prefix) =>
            harmony.Patch(target ?? throw new MissingMethodException(), prefix: new HarmonyMethod(typeof(Patches), prefix), finalizer: new HarmonyMethod(typeof(Patches), nameof(Scope_Finalizer)));
        void Found(int rewrites, string what)
        {
            if (rewrites == 0) throw new InvalidOperationException($"the game's {what} no longer reads as it did");
        }

        // The house on a plot shows its farmers' house upgrade, as a cabin shows its farmer's.
        Layer("Building.getSourceRect", "The house on a farm keeps its first look whatever its farmers have built.", () =>
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Building), nameof(Building.getSourceRect)) ?? throw new MissingMethodException(), postfix: new HarmonyMethod(typeof(Patches), nameof(GetSourceRect_Postfix))));

        // Weeds seed grass on a plot, and blue grass on a Meadowlands plot.
        Layer("GameLocation.spawnWeeds", "Weeds on a farm other than the main farm seed no grass.", () =>
        {
            var spawnWeeds = AccessTools.DeclaredMethod(typeof(GameLocation), nameof(GameLocation.spawnWeeds)) ?? throw new MissingMethodException();
            Found(Redirect.FarmNameTests(harmony, spawnWeeds), "test for the farm's name in spawnWeeds");
            Scoped(spawnWeeds, nameof(LocationScope_Prefix));
        });

        // What a crate in the mines holds follows the kind of farm of the farmer who breaks it.
        Layer("BreakableContainer.releaseContents", "A crate broken in the mines holds what the main farm's kind of land gives, whoever breaks it.", () =>
            Scoped(AccessTools.DeclaredMethod(typeof(BreakableContainer), nameof(BreakableContainer.releaseContents)), nameof(FinderScope_Prefix)));

        // Moving an animal to another building through its own menu shows the farm it lives on.
        Layer("AnimalQueryMenu", "Moving an animal to another building through its menu shows the main farm, also for an animal that lives on another farm.", () =>
        {
            var methods = new[] { nameof(AnimalQueryMenu.prepareForAnimalPlacement), nameof(AnimalQueryMenu.receiveLeftClick), nameof(AnimalQueryMenu.performHoverAction) }
                .Select(name => AccessTools.DeclaredMethod(typeof(AnimalQueryMenu), name) ?? throw new MissingMethodException(name)).ToArray();
            // All three or none: a view of one farm that places on another would be worse than the game's own.
            foreach (var method in methods)
                Found(LexicalRewrite.Count(PatchProcessor.GetOriginalInstructions(method), code => code.Calls(AccessTools.DeclaredMethod(typeof(Game1), nameof(Game1.getFarm)))), $"AnimalQueryMenu.{method.Name}");
            try
            {
                foreach (var method in methods)
                {
                    Redirect.FarmCalls(harmony, method);
                    Scoped(method, nameof(AnimalScope_Prefix));
                }
                harmony.Patch(methods[0], postfix: new HarmonyMethod(typeof(Patches), nameof(AnimalPlacement_Postfix)));
            }
            catch
            {
                // Half of it (a view of the plot that places on the main farm) is taken out again before the layer is given up.
                foreach (var method in methods) harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
                throw;
            }
        });

        // Robin's counter answers on a day she is out at a farm's house.
        Layer("GameLocation.carpenters", "Robin's counter says and offers nothing on a day she builds a house upgrade at a farm's house.", () =>
            harmony.Patch(AccessTools.DeclaredMethod(typeof(GameLocation), nameof(GameLocation.carpenters)) ?? throw new MissingMethodException(), postfix: new HarmonyMethod(typeof(Patches), nameof(Carpenters_Postfix))));

        // The world map names the farm a player stands on.
        Layer("MapAreaPosition.GetScrollText", "The world map calls every farm by the main farm's name.", () =>
            harmony.Patch(AccessTools.DeclaredMethod(typeof(MapAreaPosition), nameof(MapAreaPosition.GetScrollText)) ?? throw new MissingMethodException(), postfix: new HarmonyMethod(typeof(Patches), nameof(ScrollText_Postfix))));
    }

    /// <summary>The lab makes one layer hook fail on purpose (its name in this variable), to see the mod carry on without it.</summary>
    private static readonly string? FailLayerForTest = Environment.GetEnvironmentVariable("MANYFARMS_LAB_NOLAYER");

    private static void Layer(string what, string cost, Action patch)
    {
        try
        {
            if (FailLayerForTest == what || FailLayerForTest == "all") throw new MissingMethodException(what);
            patch();
        }
        catch (Exception ex)
        {
            skipped.Add(what);
            monitor.Log($"Many Farms could not hook {what} ({ex.GetType().Name}: {ex.Message}). {cost} Everything else works.", LogLevel.Warn);
        }
    }

    /// <summary>The optional patches that could not be applied in this session (the lab reads it).</summary>
    internal static IReadOnlyCollection<string> Skipped => skipped;
    private static readonly List<string> skipped = new();

    /// <summary>The lab makes these fail on purpose (it sets this variable for its own game), to see the mod carry on without them.</summary>
    internal static bool FailOptionalForTest = Environment.GetEnvironmentVariable("MANYFARMS_LAB_NOMAGIC") == "1";

    private static void Optional(Harmony harmony, MethodBase? target, string what)
    {
        try
        {
            if (target is null || FailOptionalForTest) throw new MissingMethodException(what);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(Patches), nameof(HomeMagic_Prefix)), finalizer: new HarmonyMethod(typeof(Patches), nameof(HomeMagic_Finalizer)));
        }
        catch (Exception ex)
        {
            skipped.Add(what);
            monitor.Log($"Many Farms could not hook {what} ({ex.GetType().Name}: {ex.Message}). A Warp Totem: Farm or the Return Scepter leads to the main farm, as in an ordinary game; everything else works.", LogLevel.Warn);
        }
    }

    /// <summary>True while the game's own Warp Totem or Return Scepter makes its warp.</summary>
    [ThreadStatic] private static bool homeMagic;

    private static void HomeMagic_Prefix() => homeMagic = true;

    private static void HomeMagic_Finalizer() => homeMagic = false;

    internal static void Warn(string where, Exception ex) => WarnOnce(where, ex);

    /// <summary>What has gone wrong inside a patch in this session, by what it was doing. Empty when nothing has.</summary>
    internal static IReadOnlyCollection<string> Failures => warned;

    private static void WarnOnce(string where, Exception ex)
    {
        if (warned.Add(where)) monitor.Log($"Many Farms: {where} failed and was skipped. {ex}", LogLevel.Error);
    }

    // ---- one farm type per farm ----

    private static bool DayUpdate_Prefix(Farm __instance, out FarmScope.Handle __state)
    {
        __state = default;
        // A plot nobody farms has no day: no weeds, no debris, nothing to grow.
        if (Farms.IsPool(__instance) && !Farms.IsStarted(__instance)) return false;
        __state = FarmScope.Enter(__instance);
        return true;
    }

    private static void Kind_Prefix(Farm __instance, out FarmScope.Handle __state) => __state = FarmScope.Enter(__instance);

    /// <summary>A bush on a plot is cleared or kept by the rules of that plot's kind of farm (the Forest farm lets its farmer clear a whole stretch; the Standard farm keeps every bush).</summary>
    private static void BushKind_Prefix(Bush __instance, out FarmScope.Handle __state) => __state = FarmScope.Enter(__instance.Location as Farm);

    private static bool CaveDayUpdate_Prefix(FarmCave __instance) =>
        !FarmSlots.TryCaveSlot(__instance.Name, out int slot) || (Farms.Slot(slot) is { } farm && Farms.IsStarted(farm));

    private static void Kind_Finalizer(FarmScope.Handle __state) => __state.Exit();

    // ---- the small things a farm does ----

    private static void Scope_Finalizer(FarmScope.Handle __state) => __state.Exit();

    /// <summary>Weeds spread on a plot by that plot's kind of land (blue grass on a Meadowlands plot), whoever asks for them.</summary>
    private static void LocationScope_Prefix(GameLocation __instance, out FarmScope.Handle __state) => __state = FarmScope.Enter(__instance as Farm);

    /// <summary>
    /// The one thing a broken crate holds by the kind of farm (deluxe retaining soil for a Beach
    /// farmer, an omni geode for anyone else) follows the farm of the farmer who broke it: their own
    /// started plot, else the main farm as in an ordinary game.
    /// </summary>
    private static void FinderScope_Prefix(Farmer who, out FarmScope.Handle __state)
    {
        __state = default;
        try
        {
            if (who is null || !ModEntry.Instance.Active) return;
            __state = FarmScope.Enter(Game1.getLocationFromName(FarmerFinds.FarmFor(OwnPlot(who.UniqueMultiplayerID)?.Name)) as Farm);
        }
        catch (Exception ex) { WarnOnce("finding whose farm a crate is broken for", ex); }
    }

    /// <summary>The started plot that is this farmer's own farm, or null.</summary>
    internal static Farm? OwnPlot(long farmerId) => Farms.ChosenBy(farmerId) is { } chosen && Farms.IsPool(chosen) && Farms.IsStarted(chosen) ? chosen : null;

    /// <summary>The farm an animal lives on: where the building it calls home stands.</summary>
    private static void AnimalScope_Prefix(AnimalQueryMenu __instance, out FarmScope.Handle __state)
    {
        __state = default;
        try { __state = FarmScope.Enter(__instance.animal?.home?.GetParentLocation() as Farm); }
        catch (Exception ex) { WarnOnce("finding the farm an animal lives on", ex); }
    }

    /// <summary>The game opens that view on a fixed corner of its own farm. On a plot it opens on the animal's own building.</summary>
    private static void AnimalPlacement_Postfix(AnimalQueryMenu __instance)
    {
        try
        {
            if (__instance.animal?.home is not { } home || home.GetParentLocation() is not Farm farm || !Farms.IsPool(farm) || farm.Map is null) return;
            var layer = farm.Map.Layers[0];
            var (x, y) = AnimalView.Origin(new TilePoint(home.tileX.Value, home.tileY.Value), new TilePoint(home.tilesWide.Value, home.tilesHigh.Value),
                Game1.viewport.Width, Game1.viewport.Height, layer.LayerWidth, layer.LayerHeight);
            Game1.viewport.Location = new xTile.Dimensions.Location(x, y);
        }
        catch (Exception ex) { WarnOnce("showing the farm an animal lives on", ex); }
    }

    /// <summary>
    /// A house on a plot is drawn in the look of its farmers' house upgrade. The game does this for
    /// the farmhouse and for a cabin by the upgrade level of the rooms behind them, moving the
    /// source rectangle one look per level, two at most: a cabin's picture holds its three looks
    /// side by side (80 wide in 240), the farmhouse's Buildings\houses one above the other (160 by
    /// 144 in 432 tall: the 0.5.0 lab saw the as-built look at level 2 when this moved only to the
    /// right). The house has no rooms, so the game's own answer is the as-built look, and this
    /// moves it the way the picture is laid out (<see cref="HouseLook.Shift"/>). The -Upgrade lab
    /// compares the house with the farmhouse's own rectangle at every level, and passed on 1.6.15.
    /// </summary>
    private static void GetSourceRect_Postfix(Building __instance, ref Rectangle __result)
    {
        if (__instance.buildingType.Value != Houses.TypeId) return;
        try
        {
            int level = Houses.LevelOf(__instance);
            if (level <= 0 || __instance.GetData() is not { } data || data.SourceRect == Rectangle.Empty || __instance.texture?.Value is not { } picture) return;
            var (dx, dy) = HouseLook.Shift(level, data.SourceRect.Width, data.SourceRect.Height, __result.X, __result.Y, picture.Width, picture.Height);
            __result.X += dx;
            __result.Y += dy;
        }
        catch (Exception ex) { WarnOnce("drawing a house's upgrade", ex); }
    }

    /// <summary>The game's counter found no Robin behind it and answered nothing: on a day this mod has her out at a house, it answers for her.</summary>
    private static void Carpenters_Postfix(GameLocation __instance, ref bool __result)
    {
        if (__result) return;
        try
        {
            if (Builder.AnswerCounter(__instance)) __result = true;
        }
        catch (Exception ex) { WarnOnce("answering Robin's counter while she is out", ex); }
    }

    /// <summary>On the world map a plot is named for its farmers, not for the main farm.</summary>
    private static void ScrollText_Postfix(MapAreaPosition __instance, ref string __result)
    {
        try
        {
            if (WorldMapIds.TryPlot(__instance.Data?.Id, out int slot) && Farms.Slot(slot) is { } farm && Farms.IsStarted(farm)) __result = Farms.Label(farm);
        }
        catch (Exception ex) { WarnOnce("naming a farm on the world map", ex); }
    }

    /// <summary>A plot's fish, forage and weeds are those of its kind; its cave's are the cave's.</summary>
    private static void GetData_Postfix(string name, ref LocationData __result)
    {
        try
        {
            if (name is null || !name.StartsWith(FarmSlots.Prefix, StringComparison.Ordinal)) return;
            if (FarmSlots.TryCaveSlot(name, out _))
            {
                if (Game1.locationData.TryGetValue("FarmCave", out var cave)) __result = cave;
                return;
            }
            if (!FarmSlots.TryFarmSlot(name, out int slot)) return;
            string key = Farms.Slot(slot) is { } farm && Farms.KindOf(farm) is { } kind ? kind.DataKey : "Farm_Standard";
            if (Game1.locationData.TryGetValue(key, out var data) || Game1.locationData.TryGetValue("Farm_Standard", out data)) __result = data;
        }
        catch (Exception ex) { WarnOnce("reading a farm's data", ex); }
    }

    // ---- what a plot starts with ----

    /// <summary>
    /// The game gives every Farm a farmhouse, a greenhouse, a bin, a pet bowl and the starting
    /// cabins. The farmhouse and the cabins belong to the game's own farm and stay there; a
    /// started plot gets the rest, and a plot nobody farms gets nothing.
    /// </summary>
    private static bool AddDefaultBuildings_Prefix(Farm __instance, bool load)
    {
        if (!Farms.IsPool(__instance)) return true;
        try
        {
            if (!Game1.IsMasterGame || !Farms.IsStarted(__instance)) return false;
            Founder.EnsureMap(__instance);
            var scope = KindScope.Enter(__instance);
            try
            {
                // A second greenhouse building gets an interior of its own from the game.
                __instance.AddDefaultBuilding("Greenhouse", __instance.GetGreenhouseStartLocation(), load);
                __instance.AddDefaultBuilding("Shipping Bin", __instance.GetStarterShippingBinLocation(), load);
                __instance.AddDefaultBuilding("Pet Bowl", __instance.GetStarterPetBowlLocation(), load);
            }
            finally
            {
                scope.Exit();
            }
        }
        catch (Exception ex) { WarnOnce("giving a farm its first buildings", ex); }
        return false;
    }

    private static void IsBuildableLocation_Postfix(Farm __instance, ref bool __result)
    {
        if (__result && Farms.IsPool(__instance) && !Farms.IsStarted(__instance)) __result = false;
    }

    private static void GetDisplayName_Postfix(Farm __instance, ref string __result)
    {
        try
        {
            if (Farms.IsPool(__instance)) __result = Farms.Label(__instance);
        }
        catch (Exception ex) { WarnOnce("naming a farm", ex); }
    }

    /// <summary>The host's spouse has one patio, on the farm with the farmhouse.</summary>
    private static bool UpdatePatio_Prefix(Farm __instance) => !Farms.IsPool(__instance);

    /// <summary>
    /// A farm draws "you have mail" over the player's mailbox. On a plot that is the mailbox of the
    /// plot's house, for a farmer of that plot; for anyone else there is none of theirs to draw it over.
    /// </summary>
    private static void GetMailboxPosition_Postfix(Farmer __instance, ref Point __result)
    {
        try
        {
            if (__instance != Game1.player || Game1.currentLocation is not Farm plot || !Farms.IsPool(plot)) return;
            __result = Houses.On(plot) is { } house && Farms.MemberIds(plot).Contains(__instance.UniqueMultiplayerID) ? Houses.Mailbox(house) : new Point(-1000, -1000);
        }
        catch (Exception ex) { WarnOnce("finding a farm's mailbox", ex); }
    }

    // ---- Robin and Marnie ----

    private static void Picking_Prefix() => pickingLocation = true;

    private static void Picking_Finalizer() => pickingLocation = false;

    /// <summary>
    /// The game lists every location that can be built in. Of the farms, a player is offered their
    /// own and the game's own farm (shared, as it always was); other players' farms are theirs.
    /// </summary>
    private static void ShowPagedResponses_Prefix(ref List<KeyValuePair<string, string>> responses)
    {
        if (!pickingLocation || responses is null) return;
        try
        {
            // On a farm without extra farms the game's own list is right as it is.
            if (!responses.Any(r => FarmSlots.TryFarmSlot(r.Key, out _))) return;
            var mine = Farms.FarmOf(Game1.player.UniqueMultiplayerID);
            var kept = responses.Where(r => Game1.getLocationFromName(r.Key) is not Farm farm || !Farms.IsPool(farm) || farm == mine).ToList();
            if (kept.Count == 0 && mine is not null) kept.Add(new KeyValuePair<string, string>(mine.NameOrUniqueName, mine.DisplayName));
            if (mine is not null)
            {
                // Your own farm first, so the list opens on it.
                int at = kept.FindIndex(r => r.Key == mine.NameOrUniqueName);
                if (at > 0) { var own = kept[at]; kept.RemoveAt(at); kept.Insert(0, own); }
            }
            responses = kept;
        }
        catch (Exception ex) { WarnOnce("listing where to build", ex); }
    }

    // ---- the weather ----

    private static void PerformLightningUpdate_Postfix(int time_of_day)
    {
        try { EachFarm.Plots("lightning over the other farms", (farm, slot) => Lightning.Strike(farm, time_of_day, slot)); }
        catch (Exception ex) { WarnOnce("lightning over the other farms", ex); }
    }

    private static void DoLightningStrike_Postfix(Farm __instance, Farm.LightningStrikeEvent lightning)
    {
        try
        {
            if (Farms.IsPool(__instance)) Lightning.ShowBolt(__instance, lightning);
        }
        catch (Exception ex) { WarnOnce("drawing a lightning bolt", ex); }
    }

    // ---- the caves, and the totems ----

    /// <summary>
    /// Every farm map's cave door leads to the one cave the game has, and that cave leads back to
    /// the game's own farm. A plot's door leads to the plot's cave, and back.
    /// </summary>
    private static void WarpFarmer_Prefix(LocationRequest locationRequest, ref int tileX, ref int tileY)
    {
        try
        {
            var here = Game1.currentLocation;
            if (locationRequest is null || here is null) return;
            if (locationRequest.Name == "FarmCave" && here is Farm from && FarmSlots.TryFarmSlot(from.Name, out int slot) && Farms.IsStarted(from))
            {
                PointAt(locationRequest, FarmSlots.CaveName(slot));
            }
            else if (locationRequest.Name == FarmSlots.MainFarm && FarmSlots.TryCaveSlot(here.Name, out int caveSlot) && Farms.Slot(caveSlot) is { } farm)
            {
                TilePoint? mapSays = farm.TryGetMapPropertyAs("FarmCaveEntry", out Point parsed, required: false) ? new TilePoint(parsed.X, parsed.Y) : null;
                var tile = FarmEntry.FromCave(Farms.KindOf(farm)?.WhichFarm ?? 0, new TilePoint(tileX, tileY), mapSays);
                if (PointAt(locationRequest, farm.Name))
                {
                    tileX = tile.X;
                    tileY = tile.Y;
                }
            }
            else if (locationRequest.Name == FarmSlots.MainFarm) HomeWarp(locationRequest, here, ref tileX, ref tileY);
        }
        catch (Exception ex) { WarnOnce("finding a farm's own cave", ex); }
    }

    /// <summary>
    /// A warp to the game's own farm that is really a way home: out of the farmer's own rooms, by
    /// Warp Totem: Farm, Return Scepter or the island's farm obelisk, or along a road from the Bus
    /// Stop, the forest or the backwoods (the road's own way in, or another gap in the same border
    /// of the map). A farmer with a farm of their own is set down on it.
    /// Every other warp to "Farm" (the cave, the greenhouse, a building's door, a festival's end,
    /// a wedding, a shortcut of the main farm's map, the minecart) is left exactly as the game made it.
    /// Decided on the player's own PC; nothing is written to the save.
    /// </summary>
    private static void HomeWarp(LocationRequest request, GameLocation here, ref int tileX, ref int tileY)
    {
        try
        {
            var mod = ModEntry.Instance;
            // A festival or a cutscene moves farmers about by its own script (a cutscene another player started pulls
            // everyone to its map before the event is up: the game marks that warp), and a trip by Farmstead Totem has its own end.
            if (Game1.eventUp || Game1.warpingForForcedRemoteEvent || Travel.InProgress || !mod.Active) return;
            var who = Game1.player;
            var own = OwnPlot(who.UniqueMultiplayerID);

            // A Warp Totem: Farm, the Return Scepter, the farm obelisk on Ginger Island (wherever they are used from, indoors too).
            if (homeMagic || here.Name == "IslandWest")
            {
                if (RoadRule.DecideMagic(own?.Name, mod.Config.RoadsLeadHome) is null || own is null) return;
                var at = Farms.ArrivalTile(own, who);
                if (PointAt(request, own.Name)) { tileX = at.X; tileY = at.Y; mod.CameHomeByRoad(own); }
                return;
            }

            // Out of the door of the rooms they are in.
            if (here is FarmHouse rooms)
            {
                // A cabin that really stands on a plot (another mod moved it) already opens onto that plot.
                if (rooms.ParentBuilding?.GetParentLocation() is Farm stands && Farms.IsPool(stands)) return;
                if (mod.DoorOut(rooms, own) is not { } door || Game1.getLocationFromName(door.Location) is not { } outside) return;
                var step = SafeTile(outside, door.Step, 0, 0, who) ?? (outside is Farm f ? Farms.ArrivalTile(f, who) : door.Step);
                if (PointAt(request, outside.NameOrUniqueName)) { tileX = step.X; tileY = step.Y; }
                return;
            }

            // A road: told by where they stand and the tile asked for, so a modded map's own way of sending walkers home counts too.
            if (FarmEntry.RoadProperty(here.Name) is not { } property || Farms.Main is not { } main) return;
            TilePoint? mainEntry = main.TryGetMapPropertyAs(property, out Point named, required: false) ? new TilePoint(named.X, named.Y) : null;
            var asked = new TilePoint(tileX, tileY);
            string? target = RoadRule.Decide(mod.TurnedRoundFrom(here.Name), own?.Name, mod.Config.RoadsLeadHome);
            if (target is null || Game1.getLocationFromName(target) is not Farm plot || !Farms.IsPool(plot) || !Farms.IsStarted(plot) || plot.Map is null) return;

            TilePoint? plotSays = plot.TryGetMapPropertyAs(property, out Point there, required: false) ? new TilePoint(there.X, there.Y) : null;
            var layer = plot.Map.Layers[0];
            var edge = FarmEntry.FromRoad(here.Name, Farms.KindOf(plot)?.WhichFarm ?? 0, plotSays, layer.LayerWidth, layer.LayerHeight);
            // The road's own way in, or a second gap in the same border that this farm's map has that road on too
            // (the Frontier Farm's western entrance from the forest). Anything else is a door of the main farm.
            if (FarmEntry.Road(here.Name, asked, mainEntry) is null)
            {
                if (here.Map is null || main.Map is null) return;
                var hereSize = here.Map.Layers[0];
                var mainSize = main.Map.Layers[0];
                if (FarmEntry.EdgeRoad(here.Name, new TilePoint(who.TilePoint.X, who.TilePoint.Y), hereSize.LayerWidth, hereSize.LayerHeight,
                        asked, mainSize.LayerWidth, mainSize.LayerHeight, edge, layer.LayerWidth, layer.LayerHeight) is null) return;
            }
            // A rider is set down one tile further on (the game's own rule, read by Game1.warpFarmer after this).
            var tile = SafeTile(plot, new Point(edge.X, edge.Y), who.isRidingHorse() ? Game1.nextFarmerWarpOffsetX : 0, who.isRidingHorse() ? Game1.nextFarmerWarpOffsetY : 0, who);
            bool atEdge = tile is not null;
            tile ??= Farms.ArrivalTile(plot, who);
            if (!PointAt(request, plot.Name)) return;
            if (!atEdge) { Game1.nextFarmerWarpOffsetX = 0; Game1.nextFarmerWarpOffsetY = 0; }
            tileX = tile.Value.X;
            tileY = tile.Value.Y;
            if (plot == own) mod.CameHomeByRoad(plot);
        }
        catch (Exception ex) { WarnOnce("leading a farmer home to their own farm", ex); }
    }

    /// <summary>
    /// A tile a farmer can be set down on, at or near the one wanted: on the map, open ground, dry,
    /// outside every building and with nothing standing on it; for a rider, the tile the game will
    /// shift them to as well. The wanted tile first, then the nearest open one within 8; null when there is none.
    /// </summary>
    private static Point? SafeTile(GameLocation location, Point wanted, int offsetX, int offsetY, Farmer who)
    {
        bool Open(Point p)
        {
            var tile = new Vector2(p.X, p.Y);
            // A building's own open tiles (a porch, a doorstep) are ground to stand on; the rest of it is not.
            return location.isTileOnMap(tile) && location.isTilePassable(tile) && !location.isWaterTile(p.X, p.Y)
                && (location.getBuildingAt(tile) is not { } building || building.isTilePassable(tile))
                // Grass, a path or tilled soil is ground to stand on; a chest, a stone, a tree or an animal is not.
                && !location.IsTileOccupiedBy(tile, ~(CollisionMask.Farmers | CollisionMask.Buildings), CollisionMask.All);
        }
        bool Good(Point p) => Open(p) && (offsetX == 0 && offsetY == 0 || Open(new Point(p.X + offsetX, p.Y + offsetY)));
        if (Good(wanted)) return wanted;
        for (int ring = 1; ring <= 8; ring++)
        {
            Point? best = null;
            int bestFar = int.MaxValue;
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                    var p = new Point(wanted.X + dx, wanted.Y + dy);
                    int far = dx * dx + dy * dy;
                    if (far < bestFar && Good(p)) { best = p; bestFar = far; }
                }
            if (best is not null) return best;
        }
        return null;
    }

    // The request keeps whatever its caller hung on it (what to do on arrival); only where it goes changes.
    private static bool PointAt(LocationRequest request, string name)
    {
        var location = Game1.getLocationFromName(name);
        if (location is null) return false;
        request.Name = name;
        request.Location = location;
        request.IsStructure = false;
        return true;
    }

    /// <summary>
    /// A Farmstead Totem standing in the world: anyone who walks up and uses it gets the farm menu.
    /// It belongs to nobody, so whoever set it down, every player can travel by it.
    /// </summary>
    private static bool CheckForAction_Prefix(SObject __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
    {
        if (!Farms.IsTotem(__instance)) return true;
        __result = true;
        if (justCheckingForActivity) return false;
        try
        {
            bool free = who == Game1.player && Game1.player.canMove && !Game1.eventUp && !Game1.isFestival() && !Game1.fadeToBlack
                && Game1.activeClickableMenu is null;
            if (free) ModEntry.Instance.OpenMenu(MenuMode.Travel);
        }
        catch (Exception ex) { WarnOnce("opening the farm menu from a totem", ex); }
        return false;
    }
}

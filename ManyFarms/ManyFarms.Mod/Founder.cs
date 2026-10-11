using ManyFarms.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace ManyFarms.Mod;

/// <summary>
/// The host's side of the farms: turning a spare plot into a farm, and keeping the plots right
/// across a save and a load. Only the host ever changes a farm; the players see it through the
/// game's own syncing.
/// </summary>
internal static class Founder
{
    /// <summary>
    /// After a save is loaded or made. A plot is created with the standard farm's map and that
    /// map's trees and stones, because the game builds every location before it reads the save.
    /// A plot nobody farms is emptied (so it costs nothing in the save or on the wire); a started
    /// one gets its own kind's map back, which the save does not keep.
    /// </summary>
    public static void Prepare(IMonitor monitor)
    {
        foreach (int slot in FarmSlots.All)
        {
            var farm = Farms.Slot(slot);
            if (farm is null)
            {
                monitor.Log($"Many Farms: the game did not create farm plot {slot}. Its extra farms are unavailable in this session.", LogLevel.Error);
                continue;
            }
            if (!Farms.IsStarted(farm))
            {
                Empty(farm);
                continue;
            }
            EnsureMap(farm);
            PrepareCave(slot);
        }
    }

    /// <summary>Starts a farm of this kind on the first spare plot.</summary>
    /// <param name="founder">The farmer it is started for, when there is one: its house takes the look of their cabin.</param>
    public static Farm Start(FarmKind kind, IMonitor monitor, Farmer? founder = null)
    {
        var farm = Farms.FirstFree() ?? throw new InvalidOperationException($"All {FarmSlots.Count} spare farm plots are in use.");
        FarmSlots.TryFarmSlot(farm.Name, out int slot);
        try
        {
            Empty(farm);
            farm.modData[Keys.Kind] = kind.Id;
            var scope = KindScope.Enter(farm);
            try
            {
                EnsureMap(farm);
                // What a new game does for its farm: the map's own trees, stones, weeds and grass,
                // the buildings every farm starts with, then that kind's extras (ore, a coop).
                farm.loadObjects();
                farm.AddDefaultBuildings();
                farm.onNewGame();
            }
            finally
            {
                scope.Exit();
            }
            PrepareCave(slot);
            // Its house first, so the totem is stood beside it and not where it goes.
            EnsureHouse(farm, monitor, founder);
            // A farm nobody can leave by totem is half a farm: stand one where travellers arrive.
            if (Farms.TotemsNearArrival(farm).Count == 0 && StandTotem(farm) is null) monitor.Log($"Many Farms: no free tile for a totem near the arrival point of plot {slot}. Place one by hand.", LogLevel.Warn);
            monitor.Log($"Many Farms: started a {kind.Label} on plot {slot} ({farm.objects.Length} objects, {farm.terrainFeatures.Length} terrain features, {farm.buildings.Count} buildings).", LogLevel.Info);
            return farm;
        }
        catch
        {
            // Half a farm is worse than none: give the plot back.
            farm.modData.Remove(Keys.Kind);
            farm.modData.Remove(Keys.Members);
            Empty(farm);
            throw;
        }
    }

    /// <summary>A house on every started plot that has none (see <see cref="Houses.Ensure"/>). Host only; safe to call again.</summary>
    public static void EnsureHouses(IMonitor monitor)
    {
        if (!Game1.IsMasterGame || ModEntry.Instance.HousesHeldBack) return;
        foreach (var plot in Farms.Pool().Where(Farms.IsStarted).ToList()) EnsureHouse(plot, monitor);
    }

    /// <summary>Stands this plot's house when it has none, and says in the host's log what was done or what is in the way.</summary>
    public static void EnsureHouse(Farm plot, IMonitor monitor, Farmer? founder = null)
    {
        if (ModEntry.Instance.HousesHeldBack) return;
        try
        {
            var result = Houses.Ensure(plot, founder);
            if (result.New && result.Stood is { } house)
            {
                var step = Houses.DoorStep(house);
                monitor.Log($"Many Farms: stood a house on {Farms.Label(plot)} ({plot.Name}) at {house.tileX.Value}, {house.tileY.Value}, its doorstep at {step.X}, {step.Y}; cleared {result.Cleared} weeds, stones or grass"
                    + (result.TotemMovedTo is { } totem ? $"; a totem stands at {totem.X}, {totem.Y} beside it." : "."), LogLevel.Info);
            }
            else if (result.Restood && result.Stood is { } moved)
            {
                // 0.5.0: a house stood by an older version in the cabin's shape now has the farmhouse's, its doorstep where it was.
                var step = Houses.DoorStep(moved);
                var stood = Houses.Restood.LastOrDefault(r => r.Plot == plot.Name);
                monitor.Log($"Many Farms: the house on {Farms.Label(plot)} ({plot.Name}) was stood in an older shape and is stood again as a {Houses.ShapeName?.ToLowerInvariant() ?? "house"}: from {stood.From.X}, {stood.From.Y} to {moved.tileX.Value}, {moved.tileY.Value} ({moved.tilesWide.Value} by {moved.tilesHigh.Value}), its doorstep still at {step.X}, {step.Y}; cleared {result.Cleared} weeds, stones or grass"
                    + (result.TotemMovedTo is { } totem ? $"; its totem stands at {totem.X}, {totem.Y} beside it." : "."), LogLevel.Info);
            }
            else if (result.Stood is { } kept && result.Why is not null)
            {
                monitor.Log($"Many Farms: the house on {Farms.Label(plot)} ({plot.Name}) was stood in an older shape and could not be stood again in the new one with its doorstep where it was: {result.Why}. It keeps its spot at {kept.tileX.Value}, {kept.tileY.Value} for now and it is tried again every morning; clear the ground around it.", LogLevel.Warn);
            }
            else if (result.Stood is null)
            {
                var arrival = Farms.DefaultArrival(plot);
                monitor.Log($"Many Farms: no house could be stood on {Farms.Label(plot)} ({plot.Name}) at or within {HouseSpot.Reach} tiles of {arrival.X}, {arrival.Y}: {result.Why}. It is tried again every morning.", LogLevel.Warn);
            }
        }
        catch (Exception ex) { monitor.Log($"Many Farms could not stand a house on {plot.Name}: {ex}", LogLevel.Error); }
    }

    /// <summary>
    /// Stands a totem beside this farm's arrival tile (in front of the house), on the nearest tile
    /// that is free. Weeds, stones and twigs give way; anything a player put there does not.
    /// </summary>
    /// <returns>The tile it stands on, or null when nothing near was free.</returns>
    public static Microsoft.Xna.Framework.Vector2? StandTotem(Farm farm) => StandTotem(farm, Farms.DefaultArrival(farm));

    /// <summary>
    /// A totem by every door of this farm that has none within reach: the farmhouse, and on the
    /// game's own farm each cabin. Says where each was stood.
    /// </summary>
    public static List<(Microsoft.Xna.Framework.Point Door, Microsoft.Xna.Framework.Vector2? Stood)> StandTotems(Farm farm)
    {
        var done = new List<(Microsoft.Xna.Framework.Point, Microsoft.Xna.Framework.Vector2?)>();
        foreach (var door in Farms.DoorSpots(farm))
        {
            if (Farms.TotemsNear(farm, door).Count > 0) continue;
            done.Add((door, StandTotem(farm, door)));
        }
        return done;
    }

    public static Microsoft.Xna.Framework.Vector2? StandTotem(Farm farm, Microsoft.Xna.Framework.Point arrival)
    {
        foreach (var spot in TotemSpot.ForTotem(new TilePoint(arrival.X, arrival.Y)))
        {
            var tile = new Microsoft.Xna.Framework.Vector2(spot.X, spot.Y);
            if (!farm.isTileOnMap(tile) || !farm.isTilePassable(tile) || farm.isWaterTile(spot.X, spot.Y)) continue;
            // Not on a building's porch, and not in front of a house's mailbox, where a farmer stands to read their mail.
            if (farm.getBuildingAt(tile) is not null) continue;
            if (farm.buildings.Any(b => Houses.IsHouse(b) && Houses.Mailbox(b) == new Microsoft.Xna.Framework.Point(spot.X, spot.Y - 1))) continue;
            if (farm.objects.TryGetValue(tile, out var there))
            {
                if (!(there.IsWeeds() || there.IsBreakableStone() || there.IsTwig())) continue;
                farm.objects.Remove(tile);
            }
            if (farm.terrainFeatures.TryGetValue(tile, out var feature))
            {
                if (feature is not StardewValley.TerrainFeatures.Grass) continue;
                farm.terrainFeatures.Remove(tile);
            }
            if (farm.IsTileOccupiedBy(tile)) continue;
            var totem = ItemRegistry.Create<StardewValley.Object>(Keys.TotemQualifiedId);
            totem.TileLocation = tile;
            totem.Location = farm;
            farm.objects.Add(tile, totem);
            return tile;
        }
        return null;
    }

    /// <summary>Puts a started plot on the map of its kind. Safe to call again.</summary>
    public static void EnsureMap(Farm farm)
    {
        if (!Game1.IsMasterGame || Farms.KindOf(farm) is not { } kind || !Farms.IsPool(farm)) return;
        if (string.Equals(farm.mapPath.Value, kind.MapAsset, StringComparison.OrdinalIgnoreCase) && farm.Map is not null) return;
        farm.mapPath.Value = kind.MapAsset;
        farm.reloadMap();
        farm.updateWarps();
        ForgetMapAnswers(farm);
    }

    /// <summary>A farm remembers what its map said (where the bin goes, whether ore spawns). On another map those are stale.</summary>
    private static void ForgetMapAnswers(Farm farm)
    {
        farm.mapGrandpaShrinePosition = null;
        farm.mapMainMailboxPosition = null;
        farm.mainFarmhouseEntry = null;
        farm.mapSpouseAreaCorner = null;
        farm.mapShippingBinPosition = null;
        var reflection = ModEntry.Instance.Helper.Reflection;
        reflection.GetField<Microsoft.Xna.Framework.Rectangle?>(farm, "_mountainForageRectangle", required: false)?.SetValue(null);
        reflection.GetField<bool?>(farm, "_shouldSpawnForestFarmForage", required: false)?.SetValue(null);
        reflection.GetField<bool?>(farm, "_shouldSpawnBeachFarmForage", required: false)?.SetValue(null);
        reflection.GetField<bool?>(farm, "_oceanCrabPotOverride", required: false)?.SetValue(null);
        reflection.GetField<string>(farm, "_fishLocationOverride", required: false)?.SetValue(null!);
    }

    private static void Empty(Farm farm)
    {
        farm.objects.Clear();
        farm.terrainFeatures.Clear();
        farm.largeTerrainFeatures.Clear();
        farm.resourceClumps.Clear();
        farm.furniture.Clear();
        farm.debris.Clear();
    }

    /// <summary>
    /// The plot's own cave follows the choice the host made with Demetrius, as the game's cave
    /// does: fruit from the bats turns up by itself, and the mushroom boxes are put in once.
    /// </summary>
    public static void PrepareCave(int slot)
    {
        if (!Game1.IsMasterGame || Farms.Cave(slot) is not FarmCave cave) return;
        if (Game1.MasterPlayer.caveChoice.Value != 2 || cave.modData.ContainsKey(Keys.Mushrooms)) return;
        cave.setUpMushroomHouse();
        cave.modData[Keys.Mushrooms] = "1";
    }

    /// <summary>The host decides who farms where. Nothing a player sends is trusted beyond which farm they ask for.</summary>
    public static ChoiceResult Choose(long farmerId, ChoiceRequest request, ModConfig config, IMonitor monitor)
    {
        static ChoiceResult No(string why) => new() { Error = why };

        var farmer = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == farmerId);
        if (farmer is null || farmer.isUnclaimedFarmhand) return No("Finish making your farmer first.");
        if (Farms.Main is null) return No("The farm is not loaded yet.");
        if (Farms.ChosenBy(farmerId) is not null && !config.AllowSwitching) return No("The host has switched off changing farms.");
        if (request.GiveUp) return GiveUp(farmer, monitor);

        Farm target;
        bool started = false;
        if (!string.IsNullOrEmpty(request.NewKind))
        {
            if (!config.AllowNewFarms) return No("The host has switched off starting new farms.");
            if (FarmKinds.Find(request.NewKind) is not { } kind) return No("That kind of farm is not in this game.");
            if (config.FarmTypes.Length > 0 && !config.FarmTypes.Contains(kind.Id)) return No("The host does not allow that kind of farm.");
            if (Farms.FirstFree() is null) return No($"All {FarmSlots.Count} spare farm plots are in use.");
            try
            {
                target = Start(kind, monitor, farmer);
                started = true;
                ModEntry.Instance.RememberEnabled();
                // The farm with the cabins needs a way out as well. The host is handed a totem to set
                // down, but a host PC with nobody at the keyboard never will: if none stands by its
                // arrival point (in front of the farmhouse) by the time another farm exists, one is stood there.
                if (Farms.Main is { } home)
                    foreach (var (door, at) in StandTotems(home))
                        monitor.Log(at is { } tile ? $"Many Farms: no totem stood by the door at {door.X}, {door.Y} on the game's own farm, so one stands at {tile.X}, {tile.Y} now."
                            : $"Many Farms: no totem stands by the door at {door.X}, {door.Y} on the game's own farm and there is no free tile near it. Place one by hand.", at is null ? LogLevel.Warn : LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Many Farms could not start a {kind.Label} for {farmer.Name}: {ex}", LogLevel.Error);
                return No("The new farm could not be made. The host's log says why.");
            }
        }
        else
        {
            var found = Farms.Started().FirstOrDefault(f => f.Name == request.Farm);
            if (found is null) return No("That farm is not there any more.");
            target = found;
        }

        foreach (var farm in Farms.Started())
            SetMembers(farm, MemberList.Without(farm.modData.TryGetValue(Keys.Members, out string raw) ? raw : null, farmerId));
        SetMembers(target, MemberList.With(target.modData.TryGetValue(Keys.Members, out string now) ? now : null, farmerId));
        monitor.Log($"Many Farms: {farmer.Name} farms {Farms.Label(target)} ({target.Name}) now.", LogLevel.Info);
        return new ChoiceResult { Ok = true, Farm = target.Name, Started = started, House = Farms.IsPool(target) && Houses.For(target) is not null };
    }

    /// <summary>
    /// A farmer gives their farm back (0.5.1): the plot is emptied as a plot nobody farms, its cave
    /// with it, and they farm the main farm. Only its one farmer may, with nobody else standing on
    /// it and no animal living there (an animal with no barn is a fault in the save, not a loss).
    /// </summary>
    public static ChoiceResult GiveUp(Farmer farmer, IMonitor monitor)
    {
        static ChoiceResult No(string why) => new() { Error = why };
        long farmerId = farmer.UniqueMultiplayerID;
        if (Farms.ChosenBy(farmerId) is not { } plot || !Farms.IsPool(plot)) return No("You have no farm of your own to give up.");
        var members = Farms.MemberIds(plot);
        if (members.Count != 1) return No("Others farm it too. Only a farm that is yours alone can be given up.");
        FarmSlots.TryFarmSlot(plot.Name, out int slot);
        var cave = Farms.Cave(slot);
        var there = Game1.getOnlineFarmers().Where(f => f.UniqueMultiplayerID != farmerId && f.currentLocation is { } at
            && (at == plot || at == cave || plot.buildings.Any(b => b.GetIndoors() == at))).Select(f => f.Name).ToList();
        if (there.Count > 0) return No($"{string.Join(", ", there)} {(there.Count == 1 ? "is" : "are")} on that farm right now. Try again when it is empty.");
        if (plot.getAllFarmAnimals().Count > 0) return No("Animals live on that farm. Move or sell them first.");
        try
        {
            int buildings = plot.buildings.Count, objects = plot.objects.Length, features = plot.terrainFeatures.Length;
            plot.buildings.Clear();
            plot.animals.Clear();
            Empty(plot);
            plot.modData.Remove(Keys.Kind);
            plot.modData.Remove(Keys.Members);
            // They farm the main farm now, and the main farm's list says so (0.5.2): without this a
            // farmer who gave a plot up had no farm at all, and was asked to pick one on every join.
            SetMembers(Farms.Main!, MemberList.With(Farms.Main!.modData.TryGetValue(Keys.Members, out string mainNow) ? mainNow : null, farmerId));
            if (cave is not null) { cave.objects.Clear(); cave.terrainFeatures.Clear(); cave.debris.Clear(); cave.modData.Remove(Keys.Mushrooms); }
            monitor.Log($"Many Farms: {farmer.Name} gave up {plot.Name} (plot {slot}): {buildings} buildings, {objects} objects and {features} terrain features are gone and the plot is free. They farm {Farms.Label(Farms.Main!)} now.", LogLevel.Info);
            return new ChoiceResult { Ok = true, GaveUp = true, Farm = FarmSlots.MainFarm };
        }
        catch (Exception ex)
        {
            monitor.Log($"Many Farms could not give up {plot.Name} for {farmer.Name}: {ex}", LogLevel.Error);
            return No("The farm could not be given up. The host's log says why.");
        }
    }

    private static void SetMembers(Farm farm, string members)
    {
        if (members.Length == 0) farm.modData.Remove(Keys.Members);
        else if (!farm.modData.TryGetValue(Keys.Members, out string had) || had != members) farm.modData[Keys.Members] = members;
    }
}

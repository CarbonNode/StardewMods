using ManyFarms.Core;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;

namespace ManyFarms.Mod;

/// <summary>
/// Which farms there are and whose they are. Everything here is read from the farms themselves
/// (their modData, which the game saves with the farm and sends to every player), so the host and
/// the players can never disagree about it and nothing is kept beside the save.
/// </summary>
internal static class Farms
{
    public static Farm? Main => Game1.getLocationFromName(FarmSlots.MainFarm) as Farm;

    public static Farm? Slot(int slot) => Game1.getLocationFromName(FarmSlots.FarmName(slot)) as Farm;

    public static GameLocation? Cave(int slot) => Game1.getLocationFromName(FarmSlots.CaveName(slot));

    /// <summary>True for a spare plot, started or not. False for the game's own farm.</summary>
    public static bool IsPool(GameLocation? location) => location is Farm && FarmSlots.TryFarmSlot(location.Name, out _);

    /// <summary>True for the game's own farm and for a plot somebody has started.</summary>
    public static bool IsStarted(Farm farm) => !IsPool(farm) || farm.modData.ContainsKey(Keys.Kind);

    public static FarmKind? KindOf(Farm farm)
    {
        if (!IsPool(farm)) return FarmKinds.OfMainFarm();
        return farm.modData.TryGetValue(Keys.Kind, out string id) ? FarmKinds.Find(id) : null;
    }

    public static IEnumerable<Farm> Pool()
    {
        foreach (int slot in FarmSlots.All)
            if (Slot(slot) is { } farm) yield return farm;
    }

    /// <summary>The game's own farm first, then the started plots in order.</summary>
    public static List<Farm> Started()
    {
        var farms = new List<Farm>();
        if (Main is { } main) farms.Add(main);
        farms.AddRange(Pool().Where(IsStarted));
        return farms;
    }

    public static Farm? FirstFree() => Pool().FirstOrDefault(farm => !IsStarted(farm));

    public static int FreeCount() => Pool().Count(farm => !IsStarted(farm));

    public static IReadOnlyList<long> MemberIds(Farm farm) =>
        MemberList.Parse(farm.modData.TryGetValue(Keys.Members, out string raw) ? raw : null);

    /// <summary>The farm this farmer picked, or null when they have not picked one.</summary>
    public static Farm? ChosenBy(long farmerId) => Started().FirstOrDefault(farm => MemberIds(farm).Contains(farmerId));

    /// <summary>A farmer's farm. Until they pick one it is the game's own farm, where their cabin stands.</summary>
    public static Farm? FarmOf(long farmerId) => ChosenBy(farmerId) ?? Main;

    /// <summary>Every farmer somebody plays: each claimed cabin, online or not, and the host unless nobody plays it.</summary>
    public static List<Farmer> AllFarmers() =>
        Game1.getAllFarmers().Where(farmer => farmer is not null && !farmer.isUnclaimedFarmhand && (ModEntry.Instance.HostPlays || farmer != Game1.MasterPlayer)).ToList();

    /// <summary>The farmers whose farm this is, in the order they joined it.</summary>
    public static List<Farmer> Farmers(Farm farm)
    {
        if (!IsPool(farm)) return MainRoster().Select(entry => entry.Who).ToList();
        var everyone = AllFarmers();
        var members = new List<Farmer>();
        foreach (long id in MemberIds(farm))
            if (everyone.FirstOrDefault(f => f.UniqueMultiplayerID == id) is { } farmer) members.Add(farmer);
        return members;
    }

    /// <summary>
    /// Who farms the game's own farm: the farmers who picked it, then whoever has not picked a farm
    /// yet (they still live and farm there), and for each which of the two they are.
    /// </summary>
    public static List<(Farmer Who, bool Picked)> MainRoster()
    {
        var list = new List<(Farmer, bool)>();
        if (Main is not { } main) return list;
        // The host PC's farmer is left out by the roster's own rule, so every farmer is handed to it.
        var all = Game1.getAllFarmers().Where(f => f is not null && !f.isUnclaimedFarmhand).ToList();
        var people = all.Select(f => new Person(f.UniqueMultiplayerID, f.Name, f == Game1.MasterPlayer)).ToList();
        var others = Pool().Where(IsStarted).Select(MemberIds).ToList();
        foreach (var (who, picked) in MainFarmRoster.For(MemberIds(main), others, people, ModEntry.Instance.HostPlays))
            if (all.FirstOrDefault(f => f.UniqueMultiplayerID == who.Id) is { } farmer) list.Add((farmer, picked));
        return list;
    }

    public static string Label(Farm farm)
    {
        if (!IsPool(farm))
        {
            string? name = Game1.MasterPlayer?.farmName.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return "The farm";
            // "Squeet Farm" is "Squeet Farm", not "Squeet Farm Farm".
            return name.EndsWith("farm", StringComparison.OrdinalIgnoreCase) ? name : name + " Farm";
        }
        return FarmLabel.For(Farmers(farm).Select(f => f.Name).ToList(), "An empty farm");
    }

    public static string KindLabel(Farm farm) => KindOf(farm)?.Label ?? "Farm";

    /// <summary>How far from the arrival tile a totem still counts as standing by it.</summary>
    public const int NearArrival = 6;

    /// <summary>
    /// Where travellers arrive on this farm, and where its first totem is stood beside: the tile in
    /// front of the farmhouse door (the map's FarmHouseEntry, or the door of the house that stands
    /// there). Every farm map names it, it is kept clear by design, and on a plot it is where the
    /// plot's house is stood, doorstep on this tile. The game's own warp-totem tile is not used: a
    /// modded map may put it anywhere (the Frontier Farm's is at its top edge, in the trees).
    /// </summary>
    public static Point DefaultArrival(Farm farm) => farm.GetMainFarmHouseEntry();

    /// <summary>The totems standing within reach of a spot on this farm.</summary>
    public static List<Vector2> TotemsNear(Farm farm, Point spot) =>
        Totems(farm).Where(tile => Math.Abs(tile.X - spot.X) <= NearArrival && Math.Abs(tile.Y - spot.Y) <= NearArrival).ToList();

    /// <summary>The totems standing within reach of this farm's arrival tile.</summary>
    public static List<Vector2> TotemsNearArrival(Farm farm) => TotemsNear(farm, DefaultArrival(farm));

    /// <summary>The tile in front of a building's door, where a farmer stands on stepping out.</summary>
    public static Point DoorStep(Building building) => new(building.tileX.Value + building.humanDoor.X, building.tileY.Value + building.humanDoor.Y + 1);

    /// <summary>
    /// The doors a farm's totems stand by: in front of the farmhouse, and on the game's own farm in
    /// front of every cabin as well, so nobody wakes up far from a totem.
    /// </summary>
    public static List<Point> DoorSpots(Farm farm)
    {
        var spots = new List<Point> { DefaultArrival(farm) };
        if (!IsPool(farm))
            foreach (var building in farm.buildings)
                if (building.isCabin && building.humanDoor.Value != new Point(-1, -1)) spots.Add(DoorStep(building));
        // A plot's house stands with its doorstep on the arrival tile; moved elsewhere on the plot, its door is one more.
        if (IsPool(farm) && Houses.On(farm) is { } house && !spots.Contains(Houses.DoorStep(house))) spots.Add(Houses.DoorStep(house));
        return spots;
    }

    /// <summary>Where this farmer's own door is on this farm (their cabin's, or the farmhouse's), if their home stands here.</summary>
    public static Point? OwnDoor(Farm farm, Farmer who)
    {
        // On a plot the door is its house's, for its farmers and for visitors alike.
        if (IsPool(farm)) return Houses.On(farm) is { } house ? Houses.DoorStep(house) : null;
        var home = Game1.getLocationFromName(who.homeLocation.Value);
        if (home is null) return null;
        foreach (var building in farm.buildings)
        {
            if (building.humanDoor.Value == new Point(-1, -1)) continue;
            var inside = building.GetIndoors();
            if (inside is not null && (ReferenceEquals(inside, home) || inside.NameOrUniqueName == home.NameOrUniqueName)) return DoorStep(building);
        }
        return null;
    }

    public static bool IsTotem(StardewValley.Object? thing) => thing is { bigCraftable.Value: true } && thing.QualifiedItemId == Keys.TotemQualifiedId;

    /// <summary>The totems standing on this farm.</summary>
    public static List<Vector2> Totems(GameLocation farm) =>
        farm.objects.Pairs.Where(pair => IsTotem(pair.Value)).Select(pair => pair.Key).OrderBy(tile => tile.Y).ThenBy(tile => tile.X).ToList();

    /// <summary>
    /// Where this traveller is set down on this farm: by their own door when their home stands here
    /// (a player going to the game's own farm lands at their cabin), otherwise by the house.
    /// </summary>
    public static Point ArrivalTile(Farm farm, Farmer who) => ArrivalTile(farm, OwnDoor(farm, who) ?? DefaultArrival(farm));

    /// <summary>Where a traveller is set down on this farm: in front of the totem nearest the house.</summary>
    public static Point ArrivalTile(Farm farm) => ArrivalTile(farm, DefaultArrival(farm));

    /// <summary>
    /// Where a traveller is set down: by the totem nearest the spot when the farm has one
    /// (<see cref="LandingBy"/>), otherwise on the spot itself.
    /// </summary>
    public static Point ArrivalTile(Farm farm, Point usual)
    {
        var totems = Totems(farm);
        if (totems.Count == 0) return usual;
        var nearest = totems.OrderBy(tile => Vector2.DistanceSquared(tile, new Vector2(usual.X, usual.Y))).First();
        return LandingBy(farm, new Point((int)nearest.X, (int)nearest.Y)) ?? usual;
    }

    /// <summary>
    /// The one rule for where a traveller lands by a totem: in front of it (the tile below) when
    /// that is ground to stand on, else beside it, else behind it (<see cref="TotemSpot.ForTraveller"/>'s
    /// order); null when none of those is. Decided once here; the labs ask the same question.
    /// </summary>
    /// <param name="farmersBlock">A farmer already standing on a tile takes it (a second traveller lands beside the first). The lab turns this off to ask about the tile its own farmer stands on.</param>
    internal static Point? LandingBy(GameLocation farm, Point totem, bool farmersBlock = true)
    {
        foreach (var spot in TotemSpot.ForTraveller(new TilePoint(totem.X, totem.Y)))
            if (CanLandOn(farm, new Point(spot.X, spot.Y), farmersBlock)) return new Point(spot.X, spot.Y);
        return null;
    }

    /// <summary>
    /// Ground a farmer can be set down on, or stand on: on the map, open, dry, outside every
    /// building except on a building's own open tiles (a house's doorstep and the lawn rows of the
    /// farmhouse's shape, where a totem may well be set down; the rest of a house, its walls and
    /// shrubs, is not), with nothing standing on it that blocks (grass and a path do not). A farmer
    /// standing there takes it unless <paramref name="farmersBlock"/> is off.
    /// </summary>
    internal static bool CanLandOn(GameLocation farm, Point p, bool farmersBlock = true)
    {
        var tile = new Vector2(p.X, p.Y);
        // Farmers are asked by their tile, outright: the game's own occupancy test did not see the host standing in front of
        // a totem (the third lab round set a second traveller down on top of the first), so it is asked only about things.
        if (farmersBlock && FarmerStandsOn(farm, p)) return false;
        return farm.isTileOnMap(tile) && farm.isTilePassable(tile) && !farm.isWaterTile(p.X, p.Y)
            && (farm.getBuildingAt(tile) is not { } building || building.isTilePassable(tile))
            && !farm.IsTileOccupiedBy(tile, ~(CollisionMask.Buildings | CollisionMask.Farmers), CollisionMask.All);
    }

    /// <summary>A farmer stands on this tile: any in the location's own list, and the player at this PC when they are there (the host's own farmer is not always in that list on a one-game host).</summary>
    internal static bool FarmerStandsOn(GameLocation farm, Point p)
    {
        foreach (var farmer in farm.farmers)
            if (farmer is not null && farmer.TilePoint == p) return true;
        return Game1.player is { } me && me.currentLocation == farm && me.TilePoint == p;
    }
}

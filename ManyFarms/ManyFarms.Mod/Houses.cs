using ManyFarms.Core;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Buildings;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;

namespace ManyFarms.Mod;

/// <summary>
/// The house on a started plot: a second front door. It is a building of the mod's own type that
/// looks like the main farmhouse (0.5.0; a cabin before: "only got a cabin, not a main farm
/// upgradable house?") and has no rooms of its own. Its door opens into the farmer's own rooms
/// (their cabin's, or the farmhouse for the host's farmer), which stay where they always were on
/// the game's own farm, and stepping out again sets them down on the plot. Nobody's cabin moves,
/// and the house is never a cabin or a farmhouse to the game: no join slot, nothing to build at Robin's.
/// </summary>
internal static class Houses
{
    /// <summary>The house's entry in Data/Buildings.</summary>
    public const string TypeId = Keys.ModId + "_House";

    /// <summary>The tile action on its door.</summary>
    public const string DoorAction = Keys.ModId + "_Door";

    private const string MailboxAction = "Mailbox";

    /// <summary>The game's entry the house is made from, in order of preference.</summary>
    public const string FarmhouseEntry = "Farmhouse";
    public const string CabinEntry = "Cabin";

    /// <summary>
    /// Which of the game's entries the house's data was made from in this session ("Farmhouse", or
    /// "Cabin" when the game has no usable Farmhouse entry), set when Data/Buildings is read. Null
    /// until then, or when neither entry was usable.
    /// </summary>
    public static string? ShapeName { get; private set; }

    /// <summary>
    /// The house's data, made from the game's Farmhouse entry as it is at that moment (another mod's
    /// changes included): its picture, size, door, shape, collision map and skins, with no rooms, no
    /// builder, the door tile answering to this mod, and a mailbox tile of the mod's own (the
    /// farmhouse has none: the main farm's mailbox is a map tile). Falls back to the Cabin entry when
    /// the Farmhouse entry is missing or unusable (no door, no size), as every house was made before
    /// 0.5.0. Null when neither is usable; the house is then left out and a plot is land only.
    /// </summary>
    /// <param name="madeFrom">Which entry it was made from (<see cref="FarmhouseEntry"/> or <see cref="CabinEntry"/>), or null.</param>
    public static BuildingData? DataFrom(BuildingData? farmhouse, BuildingData? cabin, out string? madeFrom)
    {
        madeFrom = null;
        BuildingData? from = Usable(farmhouse) ? farmhouse : Usable(cabin) ? cabin : null;
        if (from is null) return null;
        madeFrom = ReferenceEquals(from, farmhouse) ? FarmhouseEntry : CabinEntry;
        ShapeName = madeFrom;
        // Of the entry's own action tiles only its mailbox is kept: the Farmhouse entry has one (at 9, 4: the main farm's
        // mailbox beside the house, 68, 16 on the standard map) and the game draws the mail bubble and answers a press
        // there; its LumberPile tiles are the main farm's wood pile and not a house's. With no rooms inside, the game's
        // own "Mailbox" opens the reader's own mail, never another farmer's.
        var actions = new List<BuildingActionTile>();
        if (from.ActionTiles is not null)
            foreach (var tile in from.ActionTiles)
                if (tile is not null && tile.Tile != from.HumanDoor && tile.Action is not null && tile.Action.StartsWith(MailboxAction, StringComparison.Ordinal))
                    actions.Add(new BuildingActionTile { Id = tile.Id, Tile = tile.Tile, Action = tile.Action });
        actions.Add(new BuildingActionTile { Id = DoorAction, Tile = from.HumanDoor, Action = DoorAction });
        // An entry without a mailbox of its own gets one by HouseSpot.Mailbox's rule.
        var at = HouseSpot.Mailbox(new TilePoint(0, 0), new TilePoint(from.Size.X, from.Size.Y), new TilePoint(from.HumanDoor.X, from.HumanDoor.Y));
        var mailbox = new Point(at.X, at.Y);
        if (!actions.Any(a => a.Action is not null && a.Action.StartsWith(MailboxAction, StringComparison.Ordinal)) && mailbox != from.HumanDoor)
            actions.Add(new BuildingActionTile { Id = Keys.ModId + "_Mailbox", Tile = mailbox, Action = MailboxAction });
        return new BuildingData
        {
            Name = "House",
            Description = "The house on a farm of your own. Its door opens into your own rooms.",
            Texture = from.Texture,
            Skins = from.Skins is null ? new List<BuildingSkin>() : new List<BuildingSkin>(from.Skins),
            DrawShadow = from.DrawShadow,
            Size = from.Size,
            FadeWhenBehind = from.FadeWhenBehind,
            SourceRect = from.SourceRect,
            SeasonOffset = from.SeasonOffset,
            DrawOffset = from.DrawOffset,
            SortTileOffset = from.SortTileOffset,
            CollisionMap = from.CollisionMap,
            AdditionalPlacementTiles = from.AdditionalPlacementTiles,
            HumanDoor = from.HumanDoor,
            DrawLayers = from.DrawLayers,
            TileProperties = from.TileProperties ?? new List<BuildingTileProperty>(),
            AdditionalTilePropertyRadius = from.AdditionalTilePropertyRadius,
            AllowsFlooringUnderneath = from.AllowsFlooringUnderneath,
            // Nobody builds one: the host stands it. Not on Robin's list, not on the Wizard's.
            Builder = null,
            BuildDays = 0,
            BuildCost = 0,
            // No rooms of its own, so the game makes no interior and links it to none (the Farmhouse entry would link it to the one shared FarmHouse).
            IndoorMap = null,
            IndoorMapType = null,
            NonInstancedIndoorLocation = null,
            ActionTiles = actions,
        };
    }

    private static bool Usable(BuildingData? data) => data is not null && data.HumanDoor.X >= 0 && data.HumanDoor.Y >= 0 && data.Size.X >= 1 && data.Size.Y >= 1;

    /// <summary>The shape the house's data has now: what a house stood in this session is marked with.</summary>
    public static HouseShape? CurrentShape(BuildingData data) =>
        ShapeName is null ? null : new HouseShape(ShapeName, new TilePoint(data.Size.X, data.Size.Y), new TilePoint(data.HumanDoor.X, data.HumanDoor.Y));

    public static bool IsHouse(Building? building) => building is not null && building.buildingType.Value == TypeId;

    /// <summary>The plot a house was stood for, wherever it stands now (another mod may have moved it).</summary>
    public static string? PlotOf(Building building) => IsHouse(building) && building.modData.TryGetValue(Keys.HouseOf, out string plot) ? plot : null;

    /// <summary>Every house in the world, with the location it stands in.</summary>
    public static List<(Building House, GameLocation In)> All()
    {
        var found = new List<(Building, GameLocation)>();
        Utility.ForEachBuilding(building =>
        {
            if (IsHouse(building) && building.GetParentLocation() is { } parent) found.Add((building, parent));
            return true;
        });
        return found;
    }

    /// <summary>The house stood for this plot and where it stands, or null. It is found by its tag, not by where it was put.</summary>
    public static (Building House, GameLocation In)? For(Farm plot)
    {
        // Almost always on the plot itself.
        foreach (var building in plot.buildings)
            if (PlotOf(building) == plot.Name) return (building, plot);
        foreach (var entry in All())
            if (PlotOf(entry.House) == plot.Name) return entry;
        return null;
    }

    /// <summary>This plot's house when it stands on the plot itself, else null.</summary>
    public static Building? On(Farm plot) => plot.buildings.FirstOrDefault(building => PlotOf(building) == plot.Name);

    public static Point DoorStep(Building house) => Farms.DoorStep(house);

    /// <summary>
    /// The house's mailbox tile: the Mailbox action tile its data carries (<see cref="DataFrom"/>
    /// put it there by <see cref="HouseSpot.Mailbox"/>'s rule), at the house as it stands. The game's
    /// Building.getMailboxPosition answers this for a real cabin only (anything else gets the farmhouse's 68, 16).
    /// </summary>
    public static Point Mailbox(Building house)
    {
        var at = house.GetData() is { } data ? MailboxTile(data)
            : HouseSpot.Mailbox(new TilePoint(0, 0), new TilePoint(house.tilesWide.Value, house.tilesHigh.Value), new TilePoint(house.humanDoor.X, house.humanDoor.Y));
        return new Point(house.tileX.Value + at.X, house.tileY.Value + at.Y);
    }

    /// <summary>
    /// The mailbox tile of a house with this data, relative to its top-left: the data's own Mailbox
    /// action tile (the farmhouse's is beside the house, outside its footprint: 9, 4 of a 9 by 5
    /// house), else the rule's answer for its shape.
    /// </summary>
    internal static TilePoint MailboxTile(BuildingData data)
    {
        var mail = data.ActionTiles?.FirstOrDefault(a => a?.Action is not null && a.Action.StartsWith(MailboxAction, StringComparison.Ordinal));
        if (mail is not null) return new TilePoint(mail.Tile.X, mail.Tile.Y);
        return HouseSpot.Mailbox(new TilePoint(0, 0), new TilePoint(data.Size.X, data.Size.Y), new TilePoint(data.HumanDoor.X, data.HumanDoor.Y));
    }

    // ---- its look ----

    /// <summary>The house upgrade each started plot's house shows, by the plot's name. Read every frame by the drawing, so kept here and brought up to date twice a second.</summary>
    private static readonly Dictionary<string, int> levels = new();

    /// <summary>
    /// Works out again what every house shows: the highest house upgrade among its farm's farmers
    /// (<see cref="HouseLook.Level"/>). Their levels are the game's own (Farmer.houseUpgradeLevel),
    /// which every player's game has for every farmer, in the game or not, so every screen agrees.
    /// </summary>
    public static void RefreshLevels()
    {
        levels.Clear();
        try
        {
            var everyone = Game1.getAllFarmers().Where(f => f is not null && !f.isUnclaimedFarmhand).ToList();
            foreach (var plot in Farms.Pool())
            {
                if (!Farms.IsStarted(plot)) continue;
                var ids = Farms.MemberIds(plot);
                levels[plot.Name] = HouseLook.Level(everyone.Where(f => ids.Contains(f.UniqueMultiplayerID)).Select(f => f.HouseUpgradeLevel));
            }
        }
        catch (Exception ex) { Patches.Warn("reading the farmers' house upgrades", ex); }
    }

    public static void ForgetLevels() => levels.Clear();

    /// <summary>The house upgrade this house shows (0 to 3); 0 for anything that is not a plot's house.</summary>
    public static int LevelOf(Building house) => PlotOf(house) is { } plot && levels.TryGetValue(plot, out int level) ? level : 0;

    // ---- standing one (the host) ----

    /// <param name="Stood">The house, when one stands for this plot after the call.</param>
    /// <param name="New">It was stood by this call.</param>
    /// <param name="Restood">It stood already, in an older shape, and was stood again by this call in the shape the data has now (0.5.0).</param>
    /// <param name="Why">When none stands, or one stands in an older shape that could not be stood again: what was in the way.</param>
    public readonly record struct Result(Building? Stood, bool New, bool Restood, string? Why, int Cleared, Vector2? TotemMovedTo);

    /// <summary>A house stood again in this session: for the host's log and the lab. The doorstep is the one it kept.</summary>
    public readonly record struct Restanding(string Plot, Point From, Point To, Point DoorStep);

    private static readonly List<Restanding> restood = new();

    /// <summary>The houses stood again in this session, in a new shape (see <see cref="Ensure"/>).</summary>
    internal static IReadOnlyList<Restanding> Restood => restood;

    public static void ForgetRestood() => restood.Clear();

    /// <summary>
    /// One house per started plot, stood so its doorstep is the plot's arrival tile. Host only and
    /// safe to call again: a plot whose house exists anywhere is left alone, unless that house was
    /// stood in another shape (0.5.0: a 0.3.0 house in the cabin's shape), when it is stood again
    /// once, in the shape the data has now, its doorstep where it was (<see cref="Restand"/>).
    /// Weeds, stones, twigs and grass give way; water, cliffs, buildings, trees, clumps, crops and
    /// anything a player placed do not, and then the nearest clear spot within twelve tiles is taken.
    /// With none, nothing is stood and it is tried again the next morning.
    /// </summary>
    /// <param name="founder">The farmer the plot is being started for, before they are on its list: the house takes the look of their cabin when the house's data has that skin.</param>
    public static Result Ensure(Farm plot, Farmer? founder = null)
    {
        if (!Game1.IsMasterGame || !Farms.IsPool(plot) || !Farms.IsStarted(plot)) return new Result(null, false, false, "not a started plot", 0, null);
        if (!Game1.buildingData.TryGetValue(TypeId, out var data) || data is null) return new Result(null, false, false, "the game has no entry for the house (Data/Buildings)", 0, null);
        if (For(plot) is { } has) return Restand(plot, has.House, has.In, data);

        var arrival = Farms.DefaultArrival(plot);
        var door = new TilePoint(data.HumanDoor.X, data.HumanDoor.Y);
        var size = new TilePoint(data.Size.X, data.Size.Y);
        string? firstWhy = null;
        foreach (var step in HouseSpot.DoorSteps(new TilePoint(arrival.X, arrival.Y)))
        {
            var topLeft = HouseSpot.TopLeft(step, door);
            string? why = Blocked(plot, topLeft, size, step, data, except: null);
            if (why is not null) { firstWhy ??= why; continue; }

            var (cleared, totemMoved) = ClearGround(plot, topLeft, size, data);
            var house = new Building(TypeId, new Vector2(topLeft.X, topLeft.Y));
            house.modData[Keys.HouseOf] = plot.Name;
            if (CurrentShape(data) is { } shape) house.modData[Keys.HouseShape] = shape.ToString();
            house.daysOfConstructionLeft.Value = 0;
            if (FounderSkin(plot, founder, data) is { } skin) house.skinId.Value = skin;
            house.load();
            plot.buildings.Add(house);

            Vector2? totemAt = totemMoved || Farms.TotemsNear(plot, new Point(step.X, step.Y)).Count == 0 ? Founder.StandTotem(plot, new Point(step.X, step.Y)) : null;
            return new Result(house, true, false, null, cleared, totemAt);
        }
        return new Result(null, false, false, firstWhy ?? "no clear ground", 0, null);
    }

    /// <summary>
    /// A house that stands already is left alone, unless it was stood in another shape than the
    /// data has now. Then (0.5.0: the live save's plot 1 house, stood by 0.3.0 as a cabin) it gets
    /// the new size and picture, its top-left worked out again from its OLD doorstep so the
    /// doorstep stays on the arrival tile, the weeds and stones under the new footprint cleared
    /// and a totem caught inside it stood again beside it, as for a new house. If something is in
    /// the way it keeps its old spot (in the new shape, so the doorstep is off by the shapes'
    /// difference) and it is tried again next morning. A house another mod moved off its plot is
    /// left where it stands and only marked. A 0.4.0 host loading the save after this draws a
    /// cabin-shaped house at the new spot: no new building type, nothing it must know.
    /// </summary>
    private static Result Restand(Farm plot, Building house, GameLocation standsIn, BuildingData data)
    {
        Result unchanged = new(house, false, false, null, 0, null);
        if (CurrentShape(data) is not { } now) return unchanged;
        var stoodWith = HouseShape.Parse(house.modData.TryGetValue(Keys.HouseShape, out string marker) ? marker : null);
        // The marker was written before any shape but the cabin's existed, so a house without one was stood in the cabin's
        // shape: the game's Cabin entry as it is now, else the 5 by 3 with the door at 2, 1 that 1.6.15 has.
        var old = stoodWith ?? (Game1.buildingData.TryGetValue(CabinEntry, out var cabin) && Usable(cabin)
            ? new HouseShape(CabinEntry, new TilePoint(cabin.Size.X, cabin.Size.Y), new TilePoint(cabin.HumanDoor.X, cabin.HumanDoor.Y))
            : HouseShape.Cabin);
        if (stoodWith is { } had && had == now) return unchanged;
        if (old.SameGround(now) || standsIn != plot)
        {
            // Same footprint and door, or not on its plot to be stood by its doorstep: nothing to move, only the shape to bring up to date.
            TakeShape(house, data);
            house.modData[Keys.HouseShape] = now.ToString();
            return unchanged;
        }

        var was = new TilePoint(house.tileX.Value, house.tileY.Value);
        var (step, topLeft) = HouseSpot.Restand(was, old, now);
        var size = now.Size;
        // Out of the way of its own check: its old footprint overlaps its new one.
        plot.buildings.Remove(house);
        string? why;
        try
        {
            why = Blocked(plot, topLeft, size, step, data, except: house);
            if (why is null)
            {
                var (cleared, totemMoved) = ClearGround(plot, topLeft, size, data);
                house.tileX.Value = topLeft.X;
                house.tileY.Value = topLeft.Y;
                TakeShape(house, data);
                house.modData[Keys.HouseShape] = now.ToString();
                plot.buildings.Add(house);
                var stepPoint = new Point(step.X, step.Y);
                Vector2? totemAt = totemMoved || Farms.TotemsNear(plot, stepPoint).Count == 0 ? Founder.StandTotem(plot, stepPoint) : null;
                restood.Add(new Restanding(plot.Name, new Point(was.X, was.Y), new Point(topLeft.X, topLeft.Y), stepPoint));
                return new Result(house, false, true, null, cleared, totemAt);
            }
        }
        finally
        {
            if (!plot.buildings.Contains(house)) plot.buildings.Add(house);
        }
        // Kept where it was, in the new shape, and not marked: tried again next morning.
        TakeShape(house, data);
        return new Result(house, false, false, why, 0, null);
    }

    /// <summary>
    /// A building saved in one shape and loaded with another: its size and door are set from the
    /// data as it is now (the game's own load may or may not have done so already; set either
    /// way), and a skin the data does not have (a cabin's skin on the farmhouse's data) is dropped.
    /// </summary>
    private static void TakeShape(Building house, BuildingData data)
    {
        house.tilesWide.Value = data.Size.X;
        house.tilesHigh.Value = data.Size.Y;
        house.humanDoor.Value = data.HumanDoor;
        if (house.skinId.Value is { } skin && (data.Skins is null || !data.Skins.Any(s => s is not null && s.Id == skin))) house.skinId.Value = null;
    }

    /// <summary>
    /// Clears what gives way under a footprint and under its mailbox (the farmhouse's stands just
    /// beside the house), takes our own totems out of those tiles and from in front of the mailbox,
    /// to be stood again beside the house.
    /// </summary>
    private static (int Cleared, bool TotemMoved) ClearGround(Farm plot, TilePoint topLeft, TilePoint size, BuildingData data)
    {
        int cleared = 0;
        bool totemMoved = false;
        var at = MailboxTile(data);
        var mailbox = new TilePoint(topLeft.X + at.X, topLeft.Y + at.Y);
        var front = new TilePoint(mailbox.X, mailbox.Y + 1);
        var ground = HouseSpot.Footprint(topLeft, size).Append(mailbox).Append(front).Distinct().ToList();
        foreach (var t in ground)
        {
            var tile = new Vector2(t.X, t.Y);
            bool inside = t != front;
            if (plot.objects.TryGetValue(tile, out var thing))
            {
                if (Farms.IsTotem(thing)) { plot.objects.Remove(tile); totemMoved = true; }
                else if (inside) { plot.objects.Remove(tile); cleared++; }
            }
            if (inside && plot.terrainFeatures.ContainsKey(tile)) { plot.terrainFeatures.Remove(tile); cleared++; }
        }
        return (cleared, totemMoved);
    }

    /// <summary>
    /// What stops a house standing here, or null when the ground is clear or can be cleared. A
    /// map tile that is not open ground blocks only under a tile the house itself leaves open (its
    /// porch, its doorstep): under its walls it is covered, as the game stands the main farmhouse on
    /// its map's house ground without asking (0.5.0; the cabin's shape never met it).
    /// </summary>
    /// <param name="except">A building that does not count as in the way: the house itself, being stood again.</param>
    private static string? Blocked(Farm plot, TilePoint topLeft, TilePoint size, TilePoint step, BuildingData data, Building? except)
    {
        foreach (var t in HouseSpot.Footprint(topLeft, size))
        {
            var tile = new Vector2(t.X, t.Y);
            if (!plot.isTileOnMap(tile)) return $"{t.X}, {t.Y} is off the map";
            if (plot.isWaterTile(t.X, t.Y)) return $"water at {t.X}, {t.Y}";
            bool wall = !data.IsTilePassable(t.X - topLeft.X, t.Y - topLeft.Y);
            if (!wall && !plot.isTilePassable(tile)) return $"the map is not open ground at {t.X}, {t.Y}";
            if (plot.getBuildingAt(tile) is { } other && other != except) return $"a building at {t.X}, {t.Y}";
            if (plot.objects.TryGetValue(tile, out var thing) && !(Farms.IsTotem(thing) || thing.IsWeeds() || thing.IsBreakableStone() || thing.IsTwig()))
                return $"{thing.DisplayName} at {t.X}, {t.Y}";
            if (plot.terrainFeatures.TryGetValue(tile, out var feature) && !(feature is Grass || feature is HoeDirt { crop: null }))
                return $"{(feature is Tree or FruitTree ? "a tree" : feature is HoeDirt ? "a crop" : feature is Flooring ? "a path" : "something growing")} at {t.X}, {t.Y}";
            if (plot.furniture.Any(f => f.GetBoundingBox().Intersects(new Rectangle(t.X * 64, t.Y * 64, 64, 64)))) return $"furniture at {t.X}, {t.Y}";
            var box = new Rectangle(t.X * 64, t.Y * 64, 64, 64);
            if (plot.resourceClumps.Any(c => c.getBoundingBox().Intersects(box))) return $"a stump or boulder at {t.X}, {t.Y}";
            if (plot.largeTerrainFeatures.Any(f => f.getBoundingBox().Intersects(box))) return $"a bush at {t.X}, {t.Y}";
            if (plot.farmers.Any(f => f.TilePoint == new Point(t.X, t.Y))) return $"a farmer stands at {t.X}, {t.Y}";
            if (plot.animals.Values.Any(a => a.GetBoundingBox().Intersects(box))) return $"an animal at {t.X}, {t.Y}";
        }
        // The doorstep has to be somewhere a farmer can stand.
        var stand = new Vector2(step.X, step.Y);
        if (!plot.isTileOnMap(stand) || plot.isWaterTile(step.X, step.Y) || !plot.isTilePassable(stand)) return $"no standing room in front of the door at {step.X}, {step.Y}";
        return null;
    }

    /// <summary>
    /// The look of the cabin of the plot's first farmer, so their house matches their cabin: only
    /// when the house's data has a skin of that name (the farmhouse's data has no cabin skins, so a
    /// farmhouse-shaped house is plain, as the main farmhouse is).
    /// </summary>
    private static string? FounderSkin(Farm plot, Farmer? founder, BuildingData data)
    {
        try
        {
            if (data.Skins is null || data.Skins.Count == 0) return null;
            foreach (var farmer in (founder is null ? Enumerable.Empty<Farmer>() : new[] { founder }).Concat(Farms.Farmers(plot)))
                if (Game1.getLocationFromName(farmer.homeLocation.Value) is FarmHouse { ParentBuilding: { isCabin: true } cabin } && cabin.skinId.Value is { } skin && data.Skins.Any(s => s is not null && s.Id == skin))
                    return skin;
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>Takes every house out again (the host's console; before going back to a version without houses).</summary>
    public static int RemoveAll()
    {
        if (!Game1.IsMasterGame) return 0;
        int gone = 0;
        foreach (var (house, at) in All())
            if (at.buildings.Remove(house)) gone++;
        return gone;
    }

    // ---- the door ----

    /// <summary>The rooms a farmer entered by a house's door, and where that door is: stepping out sets them down there.</summary>
    internal readonly record struct Entered(string Home, string Location, Point Step);

    /// <summary>
    /// The door of a house. A farmer of that farm steps into their own rooms; a visitor into the
    /// rooms of the farm's first farmer; a house nobody farms says so. A rider never gets
    /// here: the game's Building.doAction answers nothing to anyone on a horse, before any door or
    /// action tile is tried, exactly as at a real cabin's door (no message there either).
    /// </summary>
    public static bool UseDoor(GameLocation location, string[] args, Farmer who, Point tile)
    {
        try
        {
            if (who != Game1.player) return false;
            if (who.team.demolishLock.IsLocked())
            {
                Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:CantEnter"));
                return false;
            }
            var house = location.getBuildingAt(new Vector2(tile.X, tile.Y));
            if (house is null || PlotOf(house) is not { } plotName || Game1.getLocationFromName(plotName) is not Farm plot)
            {
                Game1.drawObjectDialogue(RoadWords.NobodyHome);
                return true;
            }
            var farmers = Farms.Farmers(plot);
            bool mine = Farms.MemberIds(plot).Contains(who.UniqueMultiplayerID);
            var host = mine ? who : farmers.FirstOrDefault();
            if (host is null || Game1.getLocationFromName(host.homeLocation.Value) is not FarmHouse home)
            {
                Game1.drawObjectDialogue(RoadWords.NobodyHome);
                return true;
            }
            // The game's own way in through a door: just inside the rooms' door to the farm.
            Point inside = home.warps.Count > 0 ? new Point(home.warps[0].X, home.warps[0].Y - 1) : home.getEntryLocation();
            ModEntry.Instance.EnteredByHouse(new Entered(home.NameOrUniqueName, location.NameOrUniqueName, DoorStep(house)));
            location.playSound("doorClose", new Vector2(tile.X, tile.Y));
            if (!mine) Game1.addHUDMessage(new HUDMessage(RoadWords.VisitorEnters(host.Name), HUDMessage.newQuest_type));
            Game1.warpFarmer(home.NameOrUniqueName, inside.X, inside.Y, who.FacingDirection, home.isStructure.Value);
            return true;
        }
        catch (Exception ex)
        {
            Patches.Warn("opening a house's door", ex);
            return false;
        }
    }
}

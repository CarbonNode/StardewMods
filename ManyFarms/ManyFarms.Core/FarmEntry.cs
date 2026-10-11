namespace ManyFarms.Core;

public readonly record struct TilePoint(int X, int Y);

/// <summary>
/// Where a farmer lands on a farm. The game works these out for its one farm from that farm's
/// type; a second farm of another type needs the same answers for its own map. The numbers are
/// the game's (Game1.warpFarmer and Object.totemWarpForReal in 1.6.15): a map may name the tile
/// itself, and the Four Corners (5) and Beach (6) maps differ from the rest when it does not.
/// </summary>
public static class FarmEntry
{
    public const int FourCorners = 5;
    public const int Beach = 6;

    /// <summary>The tile the cave's own door sends a farmer to on every farm map.</summary>
    public static readonly TilePoint CaveDoor = new(34, 6);

    /// <summary>Where a totem sets a farmer down.</summary>
    public static TilePoint Totem(int whichFarm, TilePoint? mapSays) => mapSays ?? whichFarm switch
    {
        Beach => new TilePoint(82, 29),
        FourCorners => new TilePoint(48, 39),
        _ => new TilePoint(48, 7),
    };

    /// <summary>Where a farmer walking out of the farm's cave comes out.</summary>
    public static TilePoint FromCave(int whichFarm, TilePoint asked, TilePoint? mapSays)
    {
        // Only the cave's own door is moved; a warp that names another tile means that tile.
        if (asked != CaveDoor) return asked;
        return mapSays ?? whichFarm switch
        {
            Beach => new TilePoint(34, 16),
            FourCorners => new TilePoint(30, 36),
            _ => asked,
        };
    }

    // ---- the roads home ----

    public const string BusStop = "BusStop";
    public const string Forest = "Forest";
    public const string Backwoods = "Backwoods";

    /// <summary>The tile each road asks for on "Farm" in the game's own maps; Game1.warpFarmer reads it as "this road's entry".</summary>
    public static TilePoint? RoadSentinel(string? from) => from switch
    {
        BusStop => new TilePoint(79, 17),
        Forest => new TilePoint(41, 64),
        Backwoods => new TilePoint(40, 0),
        _ => null,
    };

    /// <summary>The map property a farm map names a road's entry tile with.</summary>
    public static string? RoadProperty(string? road) => road switch
    {
        BusStop => "BusStopEntry",
        Forest => "ForestEntry",
        Backwoods => "BackwoodsEntry",
        _ => null,
    };

    /// <summary>
    /// Which road home a warp to the game's own farm is, or null when it is no road. A road is
    /// told by where the farmer stands and the tile asked for, not by how they move: the tile is
    /// the game's own for that road, or within a tile of where the main farm's map says that road
    /// comes in. The second catches a modded farm whose valley maps name the tile outright (the
    /// Frontier Farm's Bus Stop sends walkers to 142, 16 through a touch action, not through the
    /// game's 79, 17). Any other tile is a door of the main farm (a shortcut, a tunnel) and stays one.
    /// </summary>
    /// <param name="from">The location the farmer stands in.</param>
    /// <param name="asked">The tile on "Farm" the warp asks for.</param>
    /// <param name="mainEntryForThatRoad">Where the main farm's own map says this road comes in, when it says.</param>
    public static string? Road(string? from, TilePoint asked, TilePoint? mainEntryForThatRoad)
    {
        if (RoadSentinel(from) is not { } sentinel) return null;
        if (asked == sentinel) return from;
        if (mainEntryForThatRoad is { } entry && Math.Abs(asked.X - entry.X) <= 1 && Math.Abs(asked.Y - entry.Y) <= 1) return from;
        return null;
    }

    /// <summary>A side of a map.</summary>
    public enum Side { None, North, East, South, West }

    /// <summary>How near a map's border a tile has to be to count as being on that side: the border itself or one tile in.</summary>
    public const int EdgeDepth = 1;

    /// <summary>The side of a map a tile lies on, or None for a tile in its middle. A corner belongs to the top or the bottom.</summary>
    public static Side SideOf(TilePoint tile, int width, int height)
    {
        if (width < 1 || height < 1) return Side.None;
        if (tile.Y <= EdgeDepth) return Side.North;
        if (tile.Y >= height - 1 - EdgeDepth) return Side.South;
        if (tile.X <= EdgeDepth) return Side.West;
        if (tile.X >= width - 1 - EdgeDepth) return Side.East;
        return Side.None;
    }

    public static Side Opposite(Side side) => side switch
    {
        Side.North => Side.South,
        Side.South => Side.North,
        Side.East => Side.West,
        Side.West => Side.East,
        _ => Side.None,
    };

    /// <summary>
    /// A second way in from one of the three neighbouring maps: a modded farm may open more than
    /// one gap in the same border (the Frontier Farm has a western entrance from the forest, a
    /// touch tile at Forest 39, 0 that asks for Farm 89, 63, beside the forest road's own 136, 63).
    /// To a farmer it is the same walk home, so it is that road when all of this holds: the farmer
    /// stands on the border of the neighbouring map; the tile asked for is on the facing border of
    /// the main farm's map (out of the forest's top, in at the farm's bottom); and the farm it
    /// would lead to has its own entry for that road on that same side of its map (nearer that
    /// border than any other: the Beach farm's forest road comes in six tiles above its bottom
    /// edge, with the sea below it). Anything else from
    /// those maps (a minecart, a shortcut that comes out in the middle of the farm) is a door of
    /// the main farm and stays one, and so is every way in from any other map (a cave, a tunnel,
    /// the woods, the desert), which this never looks at.
    /// </summary>
    /// <param name="from">The location the farmer stands in.</param>
    /// <param name="stands">The tile they stand on there, and that map's size.</param>
    /// <param name="asked">The tile on "Farm" the warp asks for, and the main farm map's size.</param>
    /// <param name="plotEntry">Where that road comes in on the farm it would lead to (<see cref="FromRoad"/>), and that map's size.</param>
    public static string? EdgeRoad(string? from, TilePoint stands, int fromWidth, int fromHeight, TilePoint asked, int mainWidth, int mainHeight, TilePoint plotEntry, int plotWidth, int plotHeight)
    {
        if (RoadSentinel(from) is null) return null;
        var leaves = SideOf(stands, fromWidth, fromHeight);
        var enters = SideOf(asked, mainWidth, mainHeight);
        if (leaves == Side.None || enters != Opposite(leaves)) return null;
        return NearestSide(plotEntry, plotWidth, plotHeight) == enters ? from : null;
    }

    /// <summary>The border of a map a tile is nearest to; a tie goes to the top or the bottom.</summary>
    public static Side NearestSide(TilePoint tile, int width, int height)
    {
        if (width < 1 || height < 1) return Side.None;
        int north = tile.Y, south = height - 1 - tile.Y, west = tile.X, east = width - 1 - tile.X;
        int least = Math.Min(Math.Min(north, south), Math.Min(west, east));
        if (north == least) return Side.North;
        if (south == least) return Side.South;
        return west == least ? Side.West : Side.East;
    }

    /// <summary>
    /// Where a road sets a farmer down on a farm of this kind, on a map this size: the map's own
    /// entry for that road, else the game's table, then one tile in from the right and bottom
    /// edges. The game pulls a farmer in from an edge only on the location named "Farm", so on a
    /// plot the mod does it, or they would stand on the way out and walk straight off again.
    /// </summary>
    public static TilePoint FromRoad(string road, int whichFarm, TilePoint? mapSays, int width, int height)
    {
        var tile = mapSays ?? road switch
        {
            Forest => whichFarm switch
            {
                Beach => new TilePoint(82, 103),
                FourCorners => new TilePoint(40, 64),
                _ => new TilePoint(41, 64),
            },
            Backwoods => new TilePoint(40, 0),
            _ => new TilePoint(79, 17),
        };
        return PulledIn(tile, width, height);
    }

    /// <summary>A tile one step in from the right and bottom edges of a map, and never off its left or top.</summary>
    public static TilePoint PulledIn(TilePoint tile, int width, int height)
    {
        int x = tile.X, y = tile.Y;
        if (width > 1 && x >= width - 1) x = width - 2;
        if (height > 1 && y >= height - 1) y = height - 2;
        return new TilePoint(Math.Max(0, x), Math.Max(0, y));
    }
}

/// <summary>Which farm a road home leads a farmer to.</summary>
public static class RoadRule
{
    /// <param name="turnedRoundFrom">
    /// The farm this farmer walked off straight onto this road, having been nowhere else since
    /// (its location name), or null. Stepping back is stepping back in, whoever's farm it is.
    /// </param>
    /// <param name="ownFarm">The started plot that is this farmer's own farm (its location name), or null when they have none.</param>
    /// <param name="roadsLeadHome">Their own "Roads lead to my own farm" setting.</param>
    /// <returns>The plot the road leads to, or null to leave the game's own warp to the main farm alone.</returns>
    public static string? Decide(string? turnedRoundFrom, string? ownFarm, bool roadsLeadHome)
    {
        if (turnedRoundFrom is not null) return turnedRoundFrom == FarmSlots.MainFarm ? null : turnedRoundFrom;
        return roadsLeadHome ? ownFarm : null;
    }

    /// <summary>Whether a warp home by Warp Totem: Farm, the Return Scepter or the island's farm obelisk goes to the farmer's own farm.</summary>
    public static string? DecideMagic(string? ownFarm, bool roadsLeadHome) => roadsLeadHome ? ownFarm : null;
}

/// <summary>What a player is told about the roads and the house.</summary>
public static class RoadWords
{
    /// <summary>On screen, the first time in a session a road brings a farmer to their own farm.</summary>
    public const string RoadHomeShort = "This road leads to your own farm now.";

    /// <summary>The same in chat, with the way to the main farm.</summary>
    public static string RoadHome(string mainFarm) => $"The roads from town lead to your own farm now. To visit {mainFarm}, use any Farmstead Totem.";

    /// <summary>A farmer whose roads are switched to the main farm walks off their own farm.</summary>
    public static string WalkedOffOwn(string mainFarm) =>
        $"You walked off your farm. Turn straight round to go back in. Other roads lead to {mainFarm}, not here, because \"Roads lead to my own farm\" is off in Mod Options: use any Farmstead Totem to return.";

    /// <summary>The same on screen: the game's message box is a single line, so it says only what to do.</summary>
    public const string WalkedOffOwnShort = "You left your farm. Turn round to go back in.";

    /// <summary>A visitor's walk off somebody's farm, on screen.</summary>
    public static string VisitorLeftShort(string left) => $"You left {left}. Turn round to go back in.";

    /// <summary>A visitor walks off somebody's farm.</summary>
    /// <param name="left">The farm they left ("Alice's farm").</param>
    /// <param name="roadsLeadTo">Where the roads take them: "your own farm", or the main farm's name.</param>
    public static string VisitorLeft(string left, string roadsLeadTo) =>
        $"You left {left}. Turn straight round to go back in. Other roads lead to {roadsLeadTo}.";

    /// <summary>Said once to a farmer whose farm had no house before 0.3.0.</summary>
    public static string NewHouse(string mainFarm) =>
        $"New: your farm has a house. Its door opens into your rooms, and the roads from town lead to your farm now. To visit {mainFarm}, use any Farmstead Totem.";

    public const string NobodyHome = "Nobody lives here yet.";

    public static string VisitorEnters(string whose) => $"You step into {whose}'s rooms.";

    public const string HouseBlocked = "Your house could not be built: something stands where it goes. Clear the ground in front of your Farmstead Totem and it is built the next morning.";

    public const string OptionName = "Roads lead to my own farm";

    public static string OptionHelp(string mainFarm) =>
        $"On: walking home from the Bus Stop, the forest or the backwoods brings you to your own farm, and so do a Warp Totem: Farm and the Return Scepter. Off: they lead to {mainFarm}, as in an ordinary game.";
}

/// <summary>
/// The shape a house was stood with: which of the game's entries it was made from, its size and
/// its door. Kept on the house (0.5.0), so a house stood with one shape is stood again, once, when
/// the shape changes (0.5.0 went from the cabin's shape to the farmhouse's), its doorstep where it
/// was. A house without it was stood by 0.3.0 or 0.4.0, in the cabin's shape.
/// </summary>
public readonly record struct HouseShape(string Name, TilePoint Size, TilePoint Door)
{
    /// <summary>The shape every house had before 0.5.0: the game's cabin, 5 by 3 with the door at 2, 1.</summary>
    public static readonly HouseShape Cabin = new("Cabin", new TilePoint(5, 3), new TilePoint(2, 1));

    /// <summary>The same footprint and door, whatever it was made from.</summary>
    public bool SameGround(HouseShape other) => Size == other.Size && Door == other.Door;

    public override string ToString() => $"{Name};{Size.X},{Size.Y};{Door.X},{Door.Y}";

    /// <summary>Reads what <see cref="ToString"/> wrote; null for anything else.</summary>
    public static HouseShape? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string[] parts = text.Split(';');
        if (parts.Length != 3 || parts[0].Length == 0) return null;
        return Pair(parts[1]) is { } size && Pair(parts[2]) is { } door && size.X > 0 && size.Y > 0 ? new HouseShape(parts[0], size, door) : null;
    }

    private static TilePoint? Pair(string text)
    {
        string[] parts = text.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y) ? new TilePoint(x, y) : null;
    }
}

/// <summary>
/// Where a plot's house stands. The house is the farmhouse's picture (0.5.0; the cabin's before)
/// with no rooms of its own: its door opens into the farmer's own rooms. It is stood so that its
/// doorstep is the farm's arrival tile, the map's FarmHouseEntry: where the map's own farmhouse
/// stands, so its footprint is the ground every farm map keeps clear for a house.
/// </summary>
public static class HouseSpot
{
    /// <summary>How far from the arrival tile a house may be stood when the usual spot is taken.</summary>
    public const int Reach = 12;

    /// <summary>The building's top-left tile for a doorstep (the tile in front of its door).</summary>
    public static TilePoint TopLeft(TilePoint doorStep, TilePoint humanDoor) => new(doorStep.X - humanDoor.X, doorStep.Y - 1 - humanDoor.Y);

    /// <summary>The tile in front of the door of a building at this top-left tile.</summary>
    public static TilePoint DoorStep(TilePoint topLeft, TilePoint humanDoor) => new(topLeft.X + humanDoor.X, topLeft.Y + humanDoor.Y + 1);

    /// <summary>
    /// Where a house's mailbox goes when the game's entry it was made from has no mailbox tile of
    /// its own (the Farmhouse entry has one, at 9, 4: the main farm's mailbox beside the house, four
    /// right of the door and one row below it, 68, 16 on the standard map; that one is used as it
    /// is). The rule: the bottom row, four to the right of the door or the right-hand end of that row
    /// when the house is narrower. On the cabin's shape (5 by 3, door at 2) that is the bottom-right
    /// tile, as before 0.5.0. (Building.getMailboxPosition answers for a real cabin only, so the rule lives here.)
    /// </summary>
    public static TilePoint Mailbox(TilePoint topLeft, TilePoint size, TilePoint humanDoor) =>
        new(topLeft.X + Math.Min(humanDoor.X + 4, size.X - 1), topLeft.Y + size.Y - 1);

    /// <summary>
    /// Where a house stood with one shape goes when its shape changes: its doorstep stays where it
    /// was, and the top-left is worked out again from it with the new door.
    /// </summary>
    public static (TilePoint DoorStep, TilePoint TopLeft) Restand(TilePoint oldTopLeft, HouseShape old, HouseShape now)
    {
        var step = DoorStep(oldTopLeft, old.Door);
        return (step, TopLeft(step, now.Door));
    }

    /// <summary>Every tile the building covers.</summary>
    public static IEnumerable<TilePoint> Footprint(TilePoint topLeft, TilePoint size)
    {
        for (int y = 0; y < size.Y; y++)
            for (int x = 0; x < size.X; x++)
                yield return new TilePoint(topLeft.X + x, topLeft.Y + y);
    }

    /// <summary>Doorsteps to try, nearest first: the arrival tile itself, then rings around it out to <see cref="Reach"/>.</summary>
    public static IEnumerable<TilePoint> DoorSteps(TilePoint arrival)
    {
        yield return arrival;
        for (int ring = 1; ring <= Reach; ring++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == ring) yield return new TilePoint(arrival.X + dx, arrival.Y + dy);
    }
}

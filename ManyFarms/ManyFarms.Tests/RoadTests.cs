using ManyFarms.Core;
using Xunit;

namespace ManyFarms.Tests;

/// <summary>Which warps to the game's own farm are roads home, and where a road sets a farmer down on a plot.</summary>
public class RoadTests
{
    // Where the Frontier Farm's own map says its three roads come in (the live Squeet Farm).
    private static readonly TilePoint FrontierBus = new(142, 16);
    private static readonly TilePoint FrontierForest = new(136, 63);
    private static readonly TilePoint FrontierBackwoods = new(135, 1);

    [Theory]
    [InlineData("BusStop", 79, 17)]
    [InlineData("Forest", 41, 64)]
    [InlineData("Backwoods", 40, 0)]
    public void The_games_own_tile_for_a_road_is_that_road(string from, int x, int y)
    {
        Assert.Equal(from, FarmEntry.Road(from, new TilePoint(x, y), null));
        // Whatever the main farm's map says about its own entry.
        Assert.Equal(from, FarmEntry.Road(from, new TilePoint(x, y), new TilePoint(142, 16)));
    }

    [Theory]
    [InlineData("BusStop", 142, 16)]
    [InlineData("BusStop", 141, 16)]
    [InlineData("BusStop", 143, 17)]
    [InlineData("Forest", 136, 63)]
    [InlineData("Forest", 135, 62)]
    [InlineData("Backwoods", 135, 1)]
    [InlineData("Backwoods", 136, 0)]
    public void A_tile_at_the_main_farms_own_entry_for_that_road_is_that_road(string from, int x, int y)
    {
        var entry = from == "BusStop" ? FrontierBus : from == "Forest" ? FrontierForest : FrontierBackwoods;
        Assert.Equal(from, FarmEntry.Road(from, new TilePoint(x, y), entry));
    }

    [Theory]
    // The Frontier Farm's other ways in from the same three maps: shortcuts and tunnels of the main farm.
    [InlineData("BusStop", 103, 6)]
    [InlineData("Forest", 45, 60)]
    [InlineData("Backwoods", 133, 8)]
    [InlineData("Forest", 10, 41)]
    // Two tiles off the entry is not the entry.
    [InlineData("BusStop", 140, 16)]
    [InlineData("BusStop", 142, 18)]
    // A festival in the forest or a wedding sets the farmer down on their porch.
    [InlineData("Forest", 64, 15)]
    [InlineData("BusStop", 64, 15)]
    public void Any_other_tile_is_a_door_of_the_main_farm_and_stays_one(string from, int x, int y)
    {
        var entry = from == "BusStop" ? FrontierBus : from == "Forest" ? FrontierForest : FrontierBackwoods;
        Assert.Null(FarmEntry.Road(from, new TilePoint(x, y), entry));
    }

    [Theory]
    [InlineData("Town")]
    [InlineData("FarmCave")]
    [InlineData("FarmHouse")]
    [InlineData("Greenhouse")]
    [InlineData("IslandWest")]
    [InlineData("Farm")]
    [InlineData("Custom_FrontierFarm_Tunnel")]
    [InlineData("")]
    [InlineData(null)]
    public void A_warp_from_anywhere_else_is_no_road(string? from)
    {
        foreach (var tile in new[] { new TilePoint(79, 17), new TilePoint(41, 64), new TilePoint(40, 0), FrontierBus })
            Assert.Null(FarmEntry.Road(from, tile, FrontierBus));
    }

    [Fact]
    public void One_roads_tile_asked_from_another_road_is_no_road()
    {
        Assert.Null(FarmEntry.Road("Forest", new TilePoint(79, 17), null));
        Assert.Null(FarmEntry.Road("BusStop", new TilePoint(41, 64), null));
        Assert.Null(FarmEntry.Road("Backwoods", new TilePoint(79, 17), null));
        // Nor is another road's entry on the main farm's map.
        Assert.Null(FarmEntry.Road("Forest", FrontierBus, FrontierForest));
    }

    [Theory]
    // whichFarm, map width and height, then where each road comes in: Bus Stop, Forest, Backwoods.
    [InlineData(0, 80, 65, 78, 17, 41, 63, 40, 0)]   // Standard
    [InlineData(1, 80, 65, 78, 17, 41, 63, 40, 0)]   // Riverland
    [InlineData(2, 80, 65, 78, 17, 41, 63, 40, 0)]   // Forest
    [InlineData(3, 80, 65, 78, 17, 41, 63, 40, 0)]   // Hill-top
    [InlineData(4, 80, 65, 78, 17, 41, 63, 40, 0)]   // Wilderness
    [InlineData(5, 80, 80, 78, 17, 40, 64, 40, 0)]   // Four Corners
    [InlineData(6, 110, 110, 79, 17, 82, 103, 40, 0)] // Beach
    public void A_road_sets_a_farmer_down_at_that_kinds_own_edge(int whichFarm, int width, int height, int busX, int busY, int forestX, int forestY, int backX, int backY)
    {
        Assert.Equal(new TilePoint(busX, busY), FarmEntry.FromRoad("BusStop", whichFarm, null, width, height));
        Assert.Equal(new TilePoint(forestX, forestY), FarmEntry.FromRoad("Forest", whichFarm, null, width, height));
        Assert.Equal(new TilePoint(backX, backY), FarmEntry.FromRoad("Backwoods", whichFarm, null, width, height));
    }

    [Fact]
    public void A_map_that_names_its_own_entry_is_believed()
    {
        // A second Frontier Farm (156 by 65), and Meadowlands with entries of its own.
        Assert.Equal(new TilePoint(142, 16), FarmEntry.FromRoad("BusStop", 7, FrontierBus, 156, 65));
        Assert.Equal(new TilePoint(136, 63), FarmEntry.FromRoad("Forest", 7, FrontierForest, 156, 65));
        Assert.Equal(new TilePoint(135, 1), FarmEntry.FromRoad("Backwoods", 7, FrontierBackwoods, 156, 65));
        Assert.Equal(new TilePoint(98, 20), FarmEntry.FromRoad("BusStop", 7, new TilePoint(99, 20), 100, 75));
    }

    [Theory]
    [InlineData(79, 17, 80, 65, 78, 17)]
    [InlineData(80, 17, 80, 65, 78, 17)]
    [InlineData(41, 64, 80, 65, 41, 63)]
    [InlineData(41, 65, 80, 65, 41, 63)]
    [InlineData(79, 64, 80, 65, 78, 63)]
    [InlineData(40, 0, 80, 65, 40, 0)]
    [InlineData(0, 30, 80, 65, 0, 30)]
    [InlineData(-1, -1, 80, 65, 0, 0)]
    [InlineData(50, 30, 80, 65, 50, 30)]
    public void A_tile_on_the_right_or_bottom_edge_is_pulled_one_step_in(int x, int y, int width, int height, int wantX, int wantY)
    {
        Assert.Equal(new TilePoint(wantX, wantY), FarmEntry.PulledIn(new TilePoint(x, y), width, height));
    }

    [Fact]
    public void Every_road_has_the_map_property_the_game_reads()
    {
        Assert.Equal("BusStopEntry", FarmEntry.RoadProperty("BusStop"));
        Assert.Equal("ForestEntry", FarmEntry.RoadProperty("Forest"));
        Assert.Equal("BackwoodsEntry", FarmEntry.RoadProperty("Backwoods"));
        Assert.Null(FarmEntry.RoadProperty("Town"));
    }
}

/// <summary>Which farm a road leads to, for which player.</summary>
public class RoadRuleTests
{
    private static readonly string Plot1 = FarmSlots.FarmName(1);
    private static readonly string Plot2 = FarmSlots.FarmName(2);

    [Fact]
    public void A_farmer_with_a_farm_of_their_own_comes_home_to_it()
    {
        Assert.Equal(Plot1, RoadRule.Decide(turnedRoundFrom: null, ownFarm: Plot1, roadsLeadHome: true));
    }

    [Fact]
    public void A_farmer_with_no_farm_of_their_own_is_left_to_the_game()
    {
        Assert.Null(RoadRule.Decide(null, ownFarm: null, roadsLeadHome: true));
        Assert.Null(RoadRule.Decide(null, ownFarm: null, roadsLeadHome: false));
    }

    [Fact]
    public void A_farmer_who_switched_it_off_is_left_to_the_game()
    {
        Assert.Null(RoadRule.Decide(null, ownFarm: Plot1, roadsLeadHome: false));
    }

    [Fact]
    public void Turning_straight_round_goes_back_to_the_farm_just_left()
    {
        // Out of the main farm and back: the main farm, even for a farmer with a plot.
        Assert.Null(RoadRule.Decide(FarmSlots.MainFarm, ownFarm: Plot1, roadsLeadHome: true));
        // Out of somebody else's plot and back: that plot, for a visitor with none, one with their own, and one who switched roads off.
        Assert.Equal(Plot2, RoadRule.Decide(Plot2, ownFarm: null, roadsLeadHome: true));
        Assert.Equal(Plot2, RoadRule.Decide(Plot2, ownFarm: Plot1, roadsLeadHome: true));
        Assert.Equal(Plot2, RoadRule.Decide(Plot2, ownFarm: Plot1, roadsLeadHome: false));
        // Out of their own and back.
        Assert.Equal(Plot1, RoadRule.Decide(Plot1, ownFarm: Plot1, roadsLeadHome: false));
    }

    [Fact]
    public void A_totem_a_scepter_and_the_island_obelisk_follow_the_same_switch()
    {
        Assert.Equal(Plot1, RoadRule.DecideMagic(Plot1, roadsLeadHome: true));
        Assert.Null(RoadRule.DecideMagic(Plot1, roadsLeadHome: false));
        Assert.Null(RoadRule.DecideMagic(null, roadsLeadHome: true));
    }
}

public class HouseSpotTests
{
    // The game's cabin: five by three, its door in the middle (the lab prints the real numbers).
    private static readonly TilePoint Size = new(5, 3);
    private static readonly TilePoint Door = new(2, 1);

    [Fact]
    public void The_house_stands_so_that_its_doorstep_is_the_arrival_tile()
    {
        var arrival = new TilePoint(64, 15);
        var topLeft = HouseSpot.TopLeft(arrival, Door);
        Assert.Equal(new TilePoint(62, 13), topLeft);
        Assert.Equal(arrival, HouseSpot.DoorStep(topLeft, Door));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    [InlineData(4, 2)]
    public void Doorstep_and_top_left_undo_each_other_for_any_door(int doorX, int doorY)
    {
        var door = new TilePoint(doorX, doorY);
        foreach (var step in new[] { new TilePoint(64, 15), new TilePoint(118, 26), new TilePoint(3, 4) })
            Assert.Equal(step, HouseSpot.DoorStep(HouseSpot.TopLeft(step, door), door));
    }

    [Fact]
    public void The_cabin_shaped_mailbox_is_the_bottom_right_tile()
    {
        // The cabin's door is at 2, so four to its right is past the right-hand end: the bottom-right tile, as before 0.5.0.
        Assert.Equal(new TilePoint(66, 15), HouseSpot.Mailbox(new TilePoint(62, 13), Size, new TilePoint(2, 1)));
    }

    [Fact]
    public void The_footprint_is_every_tile_once()
    {
        var tiles = HouseSpot.Footprint(new TilePoint(62, 13), Size).ToList();
        Assert.Equal(15, tiles.Count);
        Assert.Equal(15, tiles.Distinct().Count());
        Assert.Contains(new TilePoint(62, 13), tiles);
        Assert.Contains(new TilePoint(66, 15), tiles);
        Assert.DoesNotContain(new TilePoint(67, 15), tiles);
    }

    [Fact]
    public void Other_spots_are_tried_nearest_first_and_no_further_than_twelve_tiles()
    {
        var arrival = new TilePoint(64, 15);
        var steps = HouseSpot.DoorSteps(arrival).ToList();
        Assert.Equal(arrival, steps[0]);
        Assert.Equal(25 * 25, steps.Count);
        Assert.Equal(steps.Count, steps.Distinct().Count());
        int Far(TilePoint t) => Math.Max(Math.Abs(t.X - arrival.X), Math.Abs(t.Y - arrival.Y));
        for (int i = 1; i < steps.Count; i++) Assert.True(Far(steps[i]) >= Far(steps[i - 1]));
        Assert.Equal(HouseSpot.Reach, steps.Max(Far));
    }
}

public class RoadWordsTests
{
    [Fact]
    public void Every_line_names_the_main_farm_and_the_way_there()
    {
        Assert.Equal("This road leads to your own farm now.", RoadWords.RoadHomeShort);
        Assert.Equal("The roads from town lead to your own farm now. To visit Squeet Farm, use any Farmstead Totem.", RoadWords.RoadHome("Squeet Farm"));
        Assert.Equal("New: your farm has a house. Its door opens into your rooms, and the roads from town lead to your farm now. To visit Squeet Farm, use any Farmstead Totem.", RoadWords.NewHouse("Squeet Farm"));
    }

    [Fact]
    public void A_visitor_is_told_how_to_get_back_in_and_where_the_other_roads_go()
    {
        Assert.Equal("You left Alice's farm. Turn straight round to go back in. Other roads lead to your own farm.", RoadWords.VisitorLeft("Alice's farm", "your own farm"));
        Assert.Equal("You left Alice's farm. Turn straight round to go back in. Other roads lead to Squeet Farm.", RoadWords.VisitorLeft("Alice's farm", "Squeet Farm"));
    }

    [Fact]
    public void A_farmer_with_roads_switched_off_is_told_why_and_how_to_return()
    {
        string line = RoadWords.WalkedOffOwn("Squeet Farm");
        Assert.Contains("Turn straight round to go back in", line);
        Assert.Contains("Other roads lead to Squeet Farm", line);
        Assert.Contains("\"Roads lead to my own farm\" is off in Mod Options", line);
        Assert.Contains("use any Farmstead Totem to return", line);
    }

    [Fact]
    public void What_is_shown_on_screen_for_a_walk_off_fits_the_games_one_line_box()
    {
        // The game's message box does not wrap; the road-home line it already shows is 37 characters.
        Assert.Equal("You left your farm. Turn round to go back in.", RoadWords.WalkedOffOwnShort);
        Assert.Equal("You left Alice's farm. Turn round to go back in.", RoadWords.VisitorLeftShort("Alice's farm"));
        Assert.True(RoadWords.VisitorLeftShort(FarmLabel.For(new[] { "Wwwwwwwwwwww", "Wwwwwwwwwwww", "Wwwwwwwwwwww" }, "a farm")).Length <= 90);
        Assert.True(RoadWords.WalkedOffOwn("Squeet Farm").Length > 150);
    }

    [Fact]
    public void At_the_house()
    {
        Assert.Equal("Nobody lives here yet.", RoadWords.NobodyHome);
        Assert.Equal("You step into Alice's rooms.", RoadWords.VisitorEnters("Alice"));
        Assert.Equal("Your house could not be built: something stands where it goes. Clear the ground in front of your Farmstead Totem and it is built the next morning.", RoadWords.HouseBlocked);
    }

    [Fact]
    public void The_setting_says_what_on_and_off_do()
    {
        Assert.Equal("Roads lead to my own farm", RoadWords.OptionName);
        string help = RoadWords.OptionHelp("the main farm");
        Assert.StartsWith("On: walking home from the Bus Stop, the forest or the backwoods brings you to your own farm", help);
        Assert.Contains("Off: they lead to the main farm, as in an ordinary game.", help);
    }
}

/// <summary>0.4.0: a second way in from a neighbouring map, through the same border as its road, is that road.</summary>
public class EdgeRoadTests
{
    // Where the Frontier Farm's own map says the forest road comes in (the live Squeet Farm).
    private static readonly TilePoint FrontierForest = new(136, 63);

    // The forest is 120 by 120; the Frontier Farm 156 by 65; a vanilla farm 80 by 65.
    private static string? Edge(string from, int standX, int standY, int askX, int askY, TilePoint plotEntry, int plotW = 80, int plotH = 65, int fromW = 120, int fromH = 120) =>
        FarmEntry.EdgeRoad(from, new TilePoint(standX, standY), fromW, fromH, new TilePoint(askX, askY), 156, 65, plotEntry, plotW, plotH);

    [Fact]
    public void The_Frontier_Farms_western_forest_entrance_is_the_forest_road_for_a_farm_with_a_forest_edge()
    {
        // Forest 39, 0 asks for Farm 89, 63: out of the forest's top, in at the farm's bottom.
        foreach (int whichFarm in new[] { 0, 1, 2, 3, 4 })
            Assert.Equal("Forest", Edge("Forest", 39, 0, 89, 63, FarmEntry.FromRoad("Forest", whichFarm, null, 80, 65)));
        Assert.Equal("Forest", Edge("Forest", 39, 0, 89, 63, FarmEntry.FromRoad("Forest", FarmEntry.FourCorners, null, 80, 80), 80, 80));
        // The Beach farm's forest road comes in above the sea: still its bottom side.
        Assert.Equal("Forest", Edge("Forest", 39, 0, 89, 63, FarmEntry.FromRoad("Forest", FarmEntry.Beach, null, 110, 110), 110, 110));
        // A second Frontier Farm, by its own map's entry.
        Assert.Equal("Forest", Edge("Forest", 39, 0, 89, 63, FarmEntry.FromRoad("Forest", 7, FrontierForest, 156, 65), 156, 65));
        // One tile in from the border still stands on the border.
        Assert.Equal("Forest", Edge("Forest", 39, 1, 89, 63, new TilePoint(41, 63)));
    }

    [Theory]
    // A minecart at the Bus Stop, a shortcut that starts in the middle of the forest: the farmer does not stand on a border.
    [InlineData("BusStop", 14, 10, 89, 63, 65, 30)]
    [InlineData("Forest", 60, 60, 89, 63, 120, 120)]
    // Out of the border but into the middle of the farm: a tunnel or a cave of the main farm.
    [InlineData("Forest", 39, 0, 103, 30, 120, 120)]
    [InlineData("Backwoods", 20, 39, 73, 20, 50, 40)]
    // Out of the top and in at the top is no walk across a border.
    [InlineData("Forest", 39, 0, 89, 0, 120, 120)]
    // Out of the forest's top and in at the farm's right-hand side.
    [InlineData("Forest", 39, 0, 155, 30, 120, 120)]
    public void A_way_in_that_is_no_walk_across_the_border_stays_a_door_of_the_main_farm(string from, int standX, int standY, int askX, int askY, int fromW, int fromH)
    {
        Assert.Null(Edge(from, standX, standY, askX, askY, FarmEntry.FromRoad(from, 0, null, 80, 65), 80, 65, fromW, fromH));
    }

    [Fact]
    public void A_farm_whose_own_entry_for_that_road_is_on_another_side_is_not_led_to()
    {
        // A modded farm map whose forest road comes in on its left-hand side: the forest's top does not lead there.
        Assert.Null(Edge("Forest", 39, 0, 89, 63, new TilePoint(1, 30)));
        // Out of the Bus Stop's left-hand side into the farm's right: a farm whose Bus Stop road is its right-hand side is led to.
        Assert.Equal("BusStop", Edge("BusStop", 0, 23, 154, 40, FarmEntry.FromRoad("BusStop", 0, null, 80, 65), 80, 65, 65, 30));
        Assert.Null(Edge("BusStop", 0, 23, 154, 40, new TilePoint(40, 1), 80, 65, 65, 30));
    }

    [Theory]
    [InlineData("Town")]
    [InlineData("Woods")]
    [InlineData("Desert")]
    [InlineData("FarmCave")]
    [InlineData("Custom_FrontierFarm_Tunnel")]
    [InlineData(null)]
    public void Only_the_three_neighbouring_maps_have_roads(string? from)
    {
        // The very same walk from any other map is a door of the main farm: caves, tunnels, the woods, the desert.
        Assert.Null(FarmEntry.EdgeRoad(from, new TilePoint(39, 0), 120, 120, new TilePoint(89, 63), 156, 65, new TilePoint(41, 63), 80, 65));
    }

    [Theory]
    [InlineData(40, 0, FarmEntry.Side.North)]
    [InlineData(40, 1, FarmEntry.Side.North)]
    [InlineData(40, 2, FarmEntry.Side.None)]
    [InlineData(41, 64, FarmEntry.Side.South)]
    [InlineData(41, 63, FarmEntry.Side.South)]
    [InlineData(41, 62, FarmEntry.Side.None)]
    [InlineData(0, 30, FarmEntry.Side.West)]
    [InlineData(79, 17, FarmEntry.Side.East)]
    [InlineData(78, 17, FarmEntry.Side.East)]
    [InlineData(77, 17, FarmEntry.Side.None)]
    // A corner belongs to the top or the bottom.
    [InlineData(0, 0, FarmEntry.Side.North)]
    [InlineData(79, 64, FarmEntry.Side.South)]
    public void A_tile_is_on_a_side_of_its_map_only_at_the_border(int x, int y, FarmEntry.Side side)
    {
        Assert.Equal(side, FarmEntry.SideOf(new TilePoint(x, y), 80, 65));
    }

    [Fact]
    public void Opposite_sides_face_each_other()
    {
        Assert.Equal(FarmEntry.Side.South, FarmEntry.Opposite(FarmEntry.Side.North));
        Assert.Equal(FarmEntry.Side.North, FarmEntry.Opposite(FarmEntry.Side.South));
        Assert.Equal(FarmEntry.Side.West, FarmEntry.Opposite(FarmEntry.Side.East));
        Assert.Equal(FarmEntry.Side.East, FarmEntry.Opposite(FarmEntry.Side.West));
        Assert.Equal(FarmEntry.Side.None, FarmEntry.Opposite(FarmEntry.Side.None));
        Assert.Equal(FarmEntry.Side.None, FarmEntry.SideOf(new TilePoint(0, 0), 0, 0));
        Assert.Equal(FarmEntry.Side.None, FarmEntry.NearestSide(new TilePoint(0, 0), 0, 0));
    }
}

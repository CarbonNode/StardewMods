using ManyFarms.Core;
using Xunit;

namespace ManyFarms.Tests;

public class FarmSlotsTests
{
    [Fact]
    public void Every_plot_has_its_own_farm_and_cave_name()
    {
        var farms = FarmSlots.All.Select(FarmSlots.FarmName).ToList();
        var caves = FarmSlots.All.Select(FarmSlots.CaveName).ToList();
        Assert.Equal(FarmSlots.Count, farms.Distinct().Count());
        Assert.Equal(FarmSlots.Count, caves.Distinct().Count());
        Assert.Empty(farms.Intersect(caves));
        Assert.DoesNotContain(FarmSlots.MainFarm, farms);
    }

    [Fact]
    public void A_name_reads_back_as_the_plot_it_was_made_for()
    {
        foreach (int slot in FarmSlots.All)
        {
            Assert.True(FarmSlots.TryFarmSlot(FarmSlots.FarmName(slot), out int farm));
            Assert.Equal(slot, farm);
            Assert.True(FarmSlots.TryCaveSlot(FarmSlots.CaveName(slot), out int cave));
            Assert.Equal(slot, cave);
        }
    }

    [Fact]
    public void A_cave_is_not_a_farm_and_a_farm_is_not_a_cave()
    {
        Assert.False(FarmSlots.TryFarmSlot(FarmSlots.CaveName(3), out _));
        Assert.False(FarmSlots.TryCaveSlot(FarmSlots.FarmName(3), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Farm")]
    [InlineData("FarmCave")]
    [InlineData("CarbonNode.ManyFarms_Farm")]
    [InlineData("CarbonNode.ManyFarms_Farm0")]
    [InlineData("CarbonNode.ManyFarms_Farm9")]
    [InlineData("CarbonNode.ManyFarms_Farm01")]
    [InlineData("CarbonNode.ManyFarms_Farm1x")]
    [InlineData("CarbonNode.ManyFarms_Farm-1")]
    [InlineData("carbonnode.manyfarms_farm1")]
    public void Other_names_are_not_plots(string? name)
    {
        Assert.False(FarmSlots.TryFarmSlot(name, out int slot));
        Assert.Equal(0, slot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    public void There_is_no_name_for_a_plot_that_does_not_exist(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FarmSlots.FarmName(slot));
        Assert.Throws<ArgumentOutOfRangeException>(() => FarmSlots.CaveName(slot));
    }
}

public class MemberListTests
{
    [Fact]
    public void Ids_keep_all_64_bits_and_their_order()
    {
        const long big = 9_007_199_254_740_993; // one past what a double holds
        string raw = MemberList.Format(new[] { big, -5L, 42L });
        Assert.Equal("9007199254740993,-5,42", raw);
        Assert.Equal(new[] { big, -5L, 42L }, MemberList.Parse(raw));
    }

    [Fact]
    public void Junk_costs_one_member_not_the_list()
    {
        Assert.Equal(new[] { 1L, 3L }, MemberList.Parse("1, two ,3,,0, 1"));
    }

    [Fact]
    public void Nothing_is_an_empty_list()
    {
        Assert.Empty(MemberList.Parse(null));
        Assert.Empty(MemberList.Parse("  "));
        Assert.Equal("", MemberList.Format(Array.Empty<long>()));
    }

    [Fact]
    public void Joining_twice_is_joining_once_and_leaving_removes()
    {
        string raw = MemberList.With(MemberList.With(MemberList.With(null, 7), 8), 7);
        Assert.Equal("7,8", raw);
        Assert.True(MemberList.Has(raw, 8));
        Assert.Equal("8", MemberList.Without(raw, 7));
        Assert.Equal("7,8", MemberList.Without(raw, 99));
        Assert.Equal("", MemberList.Without("7", 7));
    }
}

public class FarmLabelTests
{
    [Theory]
    [InlineData("An empty farm", new string[0])]
    [InlineData("Alice's farm", new[] { "Alice" })]
    [InlineData("Alice and Bob's farm", new[] { "Alice", "Bob" })]
    [InlineData("Alice, Bob and Cy's farm", new[] { "Alice", "Bob", "Cy" })]
    [InlineData("Alice, Bob and 3 more", new[] { "Alice", "Bob", "Cy", "Di", "Ed" })]
    public void A_farm_is_named_after_its_farmers(string expected, string[] names)
    {
        Assert.Equal(expected, FarmLabel.For(names, "An empty farm"));
    }

    [Fact]
    public void A_farmer_with_no_name_yet_is_left_out()
    {
        Assert.Equal("Bob's farm", FarmLabel.For(new[] { "", " Bob ", "  " }, "An empty farm"));
    }
}

public class FarmEntryTests
{
    [Theory]
    [InlineData(0, 48, 7)]
    [InlineData(1, 48, 7)]
    [InlineData(4, 48, 7)]
    [InlineData(5, 48, 39)]
    [InlineData(6, 82, 29)]
    [InlineData(7, 48, 7)]
    public void A_totem_lands_where_the_game_lands_one_on_that_kind_of_farm(int whichFarm, int x, int y)
    {
        Assert.Equal(new TilePoint(x, y), FarmEntry.Totem(whichFarm, null));
    }

    [Fact]
    public void A_map_that_names_its_own_tile_is_believed()
    {
        Assert.Equal(new TilePoint(12, 34), FarmEntry.Totem(6, new TilePoint(12, 34)));
        Assert.Equal(new TilePoint(12, 34), FarmEntry.FromCave(5, FarmEntry.CaveDoor, new TilePoint(12, 34)));
    }

    [Theory]
    [InlineData(0, 34, 6)]
    [InlineData(2, 34, 6)]
    [InlineData(5, 30, 36)]
    [InlineData(6, 34, 16)]
    public void Leaving_the_cave_comes_out_at_that_farms_cave(int whichFarm, int x, int y)
    {
        Assert.Equal(new TilePoint(x, y), FarmEntry.FromCave(whichFarm, FarmEntry.CaveDoor, null));
    }

    [Fact]
    public void A_warp_to_another_tile_is_left_alone()
    {
        Assert.Equal(new TilePoint(10, 10), FarmEntry.FromCave(6, new TilePoint(10, 10), new TilePoint(1, 1)));
    }
}

public class TotemSpotTests
{
    [Fact]
    public void A_farms_totem_is_never_stood_on_the_arrival_tile_and_the_nearest_spots_are_beside_it()
    {
        var arrival = new TilePoint(48, 7);
        var spots = TotemSpot.ForTotem(arrival).ToList();
        Assert.DoesNotContain(arrival, spots);
        // Two tiles out, to the side first: the doorstep and the tiles beside it stay clear.
        Assert.Equal(new TilePoint(50, 7), spots[0]);
        Assert.Equal(new TilePoint(46, 7), spots[1]);
        Assert.Equal(spots.Count, spots.Distinct().Count());
        Assert.All(spots, s => Assert.InRange(Math.Max(Math.Abs(s.X - 48), Math.Abs(s.Y - 7)), 2, 5));
        // Every tile two and three steps out is offered before the search gives up.
        for (int dx = -3; dx <= 3; dx++)
            for (int dy = -3; dy <= 3; dy++)
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) >= 2) Assert.Contains(new TilePoint(48 + dx, 7 + dy), spots);
    }

    [Fact]
    public void A_traveller_is_set_down_in_front_of_the_totem_first_and_never_on_it()
    {
        var totem = new TilePoint(10, 10);
        var spots = TotemSpot.ForTraveller(totem).ToList();
        Assert.Equal(new TilePoint(10, 11), spots[0]);
        Assert.DoesNotContain(totem, spots);
        Assert.Equal(spots.Count, spots.Distinct().Count());
    }
}

public class MenuPlanTests
{
    [Fact]
    public void TallRowsHoldAPictureAndTwoLines()
    {
        var plan = MenuPlan.For(1024, 768, 8, 0, MenuPlan.TallRowHeight);
        Assert.All(plan.Rows, r => Assert.Equal(MenuPlan.TallRowHeight, r.Height));
        Assert.True(plan.PerPage < MenuPlan.For(1024, 768, 8, 0).PerPage);
        Assert.True(plan.Panel.Bottom <= 768);
        var last = MenuPlan.For(1024, 768, 8, plan.Pages - 1, MenuPlan.TallRowHeight);
        Assert.Equal(8, last.First + last.Rows.Count);
    }

    public static IEnumerable<object[]> Screens()
    {
        foreach (var (w, h) in new[] { (640, 480), (1024, 768), (1280, 720), (1920, 1080), (2560, 1440), (960, 540) })
            foreach (int items in new[] { 0, 1, 2, 5, 9, 16 })
                yield return new object[] { w, h, items };
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Everything_is_on_the_screen_and_inside_the_panel(int width, int height, int items)
    {
        for (int page = 0; page < 20; page++)
        {
            var plan = MenuPlan.For(width, height, items, page);
            Assert.InRange(plan.Panel.X, 0, width);
            Assert.True(plan.Panel.Right <= width, $"panel runs off the right at {width}x{height}");
            Assert.True(plan.Panel.Y >= 0);
            foreach (var box in plan.Rows.Append(plan.Head).Append(plan.Foot))
            {
                Assert.True(box.X >= plan.Panel.X && box.Right <= plan.Panel.Right);
                Assert.True(box.Y >= plan.Panel.Y && box.Bottom <= plan.Panel.Bottom);
            }
            if (page >= plan.Pages - 1) break;
        }
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_item_is_on_exactly_one_page(int width, int height, int items)
    {
        var seen = new List<int>();
        int pages = MenuPlan.For(width, height, items, 0).Pages;
        for (int page = 0; page < pages; page++)
        {
            var plan = MenuPlan.For(width, height, items, page);
            Assert.Equal(pages, plan.Pages);
            for (int i = 0; i < plan.Rows.Count; i++) seen.Add(plan.First + i);
        }
        Assert.Equal(Enumerable.Range(0, items), seen);
    }

    [Fact]
    public void Rows_do_not_overlap_each_other_or_the_head_and_foot()
    {
        var plan = MenuPlan.For(1280, 720, 9, 0);
        Assert.True(plan.Rows.Count >= 1);
        for (int i = 1; i < plan.Rows.Count; i++) Assert.True(plan.Rows[i].Y >= plan.Rows[i - 1].Bottom);
        Assert.True(plan.Rows[0].Y >= plan.Head.Bottom);
        Assert.True(plan.Rows[^1].Bottom <= plan.Foot.Y);
    }

    [Fact]
    public void A_page_past_the_end_is_the_last_page_and_one_before_the_start_is_the_first()
    {
        var last = MenuPlan.For(1024, 768, 16, 99);
        Assert.Equal(last.Pages - 1, last.Page);
        Assert.Equal(0, MenuPlan.For(1024, 768, 16, -3).Page);
    }

    [Fact]
    public void Arrows_show_only_when_there_is_another_page_and_never_cover_a_row()
    {
        Assert.Null(MenuPlan.For(1920, 1080, 3, 0).Up);
        var paged = MenuPlan.For(640, 480, 9, 0);
        Assert.True(paged.Pages > 1);
        Assert.NotNull(paged.Up);
        Assert.NotNull(paged.Down);
        foreach (var row in paged.Rows)
        {
            Assert.True(row.Right <= paged.Up!.Value.X);
            Assert.True(row.Right <= paged.Down!.Value.X);
        }
    }

    [Fact]
    public void A_point_finds_the_item_under_it_and_nothing_between_rows()
    {
        var plan = MenuPlan.For(1280, 720, 9, 1);
        var row = plan.Rows[1];
        Assert.Equal(plan.First + 1, plan.ItemAt(row.X + 5, row.Y + 5));
        Assert.Equal(-1, plan.ItemAt(row.X + 5, row.Bottom + 2));
        Assert.Equal(-1, plan.ItemAt(plan.Panel.X - 10, row.Y + 5));
    }
}

public class FarmMapTests
{
    // A made-up valley: mountains along the top, a forest block on the left, a river down the middle, a sandy shore and sea along the bottom.
    private static Terrain Valley(int x, int y) =>
        y < 44 ? Terrain.Rock :
        y > 226 ? Terrain.Water :
        y > 212 ? Terrain.Sand :
        x >= 430 && x < 452 ? Terrain.Water :
        x < 150 && y < 150 ? Terrain.Forest :
        Terrain.Grass;

    private static readonly FarmPlacement[] Farms =
    {
        new(0, null, true), new(1, "6", false), new(2, "1", false), new(3, "2", false), new(4, "3", false), new(5, "0", false), new(6, "4", false), new(7, "MeadowlandsFarm", false), new(8, "6", false),
    };

    private static double Share((int X, int Y, int W, int H) box, Func<Terrain, bool> counts)
    {
        int hit = 0, all = 0;
        for (int yy = box.Y; yy < box.Y + box.H; yy += 2)
            for (int xx = box.X; xx < box.X + box.W; xx += 2)
            {
                if (xx < 0 || yy < 0 || xx >= FarmMap.Width || yy >= FarmMap.Height) continue;
                all++;
                if (counts(Valley(xx, yy))) hit++;
            }
        return (double)hit / all;
    }

    [Fact]
    public void EveryFarmGetsItsOwnSpotOnTheMapWithRoomAndOnLand()
    {
        foreach (ulong seed in new ulong[] { 0, 1, 450922254, ulong.MaxValue, 639269394610414546 })
        {
            var spots = FarmMap.Deal(seed, Farms, Valley);
            Assert.Equal(Farms.Length, spots.Length);
            Assert.Equal(spots.Length, spots.Distinct().Count());
            for (int i = 0; i < spots.Length; i++)
            {
                var (x, y, w, h) = FarmMap.Room(spots[i]);
                Assert.InRange(x, 0, FarmMap.Width - w);
                Assert.InRange(y, 0, FarmMap.Height - h);
                Assert.True(Share(FarmMap.Ground(spots[i]), t => FarmMap.MayStandOn(Farms[i].KindId, Farms[i].MainFarm, t)) >= 0.85, $"seed {seed}: farm {i} at {spots[i]} is not on its land");
                for (int j = i + 1; j < spots.Length; j++)
                {
                    var b = FarmMap.Room(spots[j]);
                    bool apart = x + w <= b.X || b.X + b.W <= x || y + h <= b.Y || b.Y + b.H <= y;
                    Assert.True(apart, $"seed {seed}: {spots[i]} and {spots[j]} overlap");
                }
            }
            Assert.Equal(spots, FarmMap.Deal(seed, Farms, Valley));
        }
        Assert.NotEqual(FarmMap.Deal(1, Farms, Valley), FarmMap.Deal(2, Farms, Valley));
    }

    [Fact]
    public void AKindOfFarmStandsByItsOwnCountry()
    {
        var spots = FarmMap.Deal(450922254, Farms, Valley);
        Assert.True(Share(FarmMap.Surround(spots[1]), t => t == Terrain.Sand) >= 0.1, $"the beach farm at {spots[1]} is not by the shore");
        Assert.True(Share(FarmMap.Surround(spots[8]), t => t == Terrain.Sand) >= 0.1, $"the second beach farm at {spots[8]} is not by the shore");
        Assert.NotEqual(spots[1], spots[8]);
        Assert.True(Share(FarmMap.Surround(spots[2]), t => t == Terrain.Water) >= 0.1, $"the riverland farm at {spots[2]} is not by water");
        Assert.True(Share(FarmMap.Surround(spots[3]), t => t == Terrain.Forest) >= 0.2, $"the forest farm at {spots[3]} is not in the woods");
        Assert.True(Share(FarmMap.Surround(spots[4]), t => t == Terrain.Rock) >= 0.2, $"the hill-top farm at {spots[4]} is not under the mountains");
        Assert.True(Share(FarmMap.Surround(spots[6]), t => t is Terrain.Forest or Terrain.Rock) >= 0.15, $"the wilderness farm at {spots[6]} is not by woods or rock");
    }

    [Fact]
    public void AFarmStartedLaterNeverMovesOneStartedBefore()
    {
        var some = FarmMap.Deal(7, Farms.Take(4).ToArray(), Valley);
        var all = FarmMap.Deal(7, Farms, Valley);
        Assert.Equal(some, all.Take(4));
    }

    [Fact]
    public void FacesTurnLeftOnlyByTheRightEdge()
    {
        Assert.False(FarmMap.FacesLeft(100));
        Assert.True(FarmMap.FacesLeft(FarmMap.Width - 30));
        var room = FarmMap.Room(new TilePoint(FarmMap.Width - 30, 100));
        Assert.True(room.X + room.W <= FarmMap.Width);
    }

    [Fact]
    public void EveryKindHasAPictureAndItsCountry()
    {
        Assert.Equal("main", FarmMap.IconFor("2", mainFarm: true));
        Assert.Equal("main", FarmMap.IconFor(null, mainFarm: true));
        Assert.NotEqual(FarmMap.MainIcon, FarmMap.MainIconBefore);
        Assert.Equal("forest", FarmMap.IconFor("2", mainFarm: false));
        Assert.Equal("meadowlands", FarmMap.IconFor("MeadowlandsFarm", mainFarm: false));
        Assert.Equal("standard", FarmMap.IconFor("FrontierFarm", mainFarm: false));
        Assert.Equal("standard", FarmMap.IconFor(null, mainFarm: false));
        Assert.Equal(new[] { Terrain.Grass }, FarmMap.Wants("FrontierFarm", mainFarm: false));
        Assert.Equal(new[] { Terrain.Sand }, FarmMap.Wants("6", mainFarm: false));
    }
}

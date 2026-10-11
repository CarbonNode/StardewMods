using ManyFarms.Core;
using Xunit;

namespace ManyFarms.Tests;

public class MainFarmRosterTests
{
    private static readonly Person Host = new(1, "HostPC", true);
    private static readonly Person Alice = new(2, "Alice", false);
    private static readonly Person Bob = new(3, "Bob", false);
    private static readonly Person Cy = new(4, "Cy", false);
    private static readonly Person[] Everyone = { Host, Alice, Bob, Cy };

    [Fact]
    public void The_main_farm_lists_who_picked_it_then_who_has_picked_nothing()
    {
        // Bob picked the main farm, Alice has a farm of her own, Cy has not picked.
        var roster = MainFarmRoster.For(new long[] { 3 }, new[] { new long[] { 2 } }, Everyone, hostPlays: false);
        Assert.Equal(new[] { (Bob, true), (Cy, false) }, roster);
    }

    [Fact]
    public void A_host_pc_nobody_plays_is_never_on_it()
    {
        Assert.DoesNotContain(MainFarmRoster.For(Array.Empty<long>(), Array.Empty<long[]>(), Everyone, hostPlays: false), e => e.Who.IsHost);
        // Not even when its farmer's id is in the main farm's own list.
        Assert.DoesNotContain(MainFarmRoster.For(new long[] { 1 }, Array.Empty<long[]>(), Everyone, hostPlays: false), e => e.Who.IsHost);
    }

    [Fact]
    public void A_host_somebody_plays_is_a_farmer_like_any_other()
    {
        var roster = MainFarmRoster.For(Array.Empty<long>(), Array.Empty<long[]>(), Everyone, hostPlays: true);
        Assert.Equal(new[] { (Host, false), (Alice, false), (Bob, false), (Cy, false) }, roster);
    }

    [Fact]
    public void A_farmer_with_a_farm_of_their_own_is_not_on_the_main_farm()
    {
        var roster = MainFarmRoster.For(Array.Empty<long>(), new[] { new long[] { 2, 3 }, new long[] { 4 } }, Everyone, hostPlays: true);
        Assert.Equal(new[] { (Host, false) }, roster);
    }

    [Fact]
    public void Nobody_is_listed_twice_and_an_unknown_id_is_skipped()
    {
        var roster = MainFarmRoster.For(new long[] { 3, 3, 99 }, Array.Empty<long[]>(), new[] { Bob }, hostPlays: true);
        Assert.Equal(new[] { (Bob, true) }, roster);
    }

    [Fact]
    public void Everyone_on_farms_of_their_own_leaves_the_main_farm_empty()
    {
        Assert.Empty(MainFarmRoster.For(Array.Empty<long>(), new[] { new long[] { 2, 3, 4 } }, Everyone, hostPlays: false));
    }
}

public class MapWordsTests
{
    [Fact]
    public void The_main_farm_says_it_is_the_main_farm()
    {
        Assert.Equal("Main farm", MapWords.MainPlate);
        Assert.StartsWith("The main farm, shared by everyone", MapWords.Sub(mainFarm: true, "Frontier Farm", mine: false, here: false));
        Assert.DoesNotContain("main farm", MapWords.Sub(mainFarm: false, "Forest Farm", mine: false, here: false), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_line_under_the_name_says_what_is_true()
    {
        Assert.Equal("Forest Farm", MapWords.Sub(false, "Forest Farm", mine: false, here: false));
        Assert.Equal("Forest Farm  |  Your farm  |  You are here", MapWords.Sub(false, "Forest Farm", mine: true, here: true));
        Assert.Equal("Forest Farm  |  Your farm: your house is here", MapWords.Sub(false, "Forest Farm", mine: true, here: false, houseHere: true));
        // A farmer with no land of their own is on the main farm; it is not called theirs.
        Assert.Equal("The main farm, shared by everyone  |  Frontier Farm  |  You are here", MapWords.Sub(true, "Frontier Farm", mine: true, here: true));
    }

    [Fact]
    public void The_heading_says_why_these_farmers_are_on_the_main_farm()
    {
        Assert.Equal("No farm of their own, so they farm here:", MapWords.Heading(mainFarm: true, 2));
        Assert.Equal("No farm of their own, so they farm here:", MapWords.Heading(mainFarm: true, 1));
        Assert.Equal("Its farmer:", MapWords.Heading(mainFarm: false, 1));
        Assert.Equal("Its farmers:", MapWords.Heading(mainFarm: false, 3));
    }

    [Fact]
    public void A_farm_nobody_farms_says_so()
    {
        Assert.Equal("Nobody farms here yet.", MapWords.Heading(mainFarm: false, 0));
        Assert.Equal("Nobody farms here: everyone has a farm of their own.", MapWords.Heading(mainFarm: true, 0));
    }

    [Fact]
    public void A_farmer_on_the_main_farm_is_told_apart_by_whether_they_picked_it()
    {
        Assert.Equal("chose the main farm", MapWords.Note(mainFarm: true, picked: true, online: true));
        Assert.Equal("has not picked a farm yet", MapWords.Note(mainFarm: true, picked: false, online: true));
        Assert.Equal("has not picked a farm yet, away", MapWords.Note(mainFarm: true, picked: false, online: false));
        Assert.Equal("", MapWords.Note(mainFarm: false, picked: true, online: true));
        Assert.Equal("away", MapWords.Note(mainFarm: false, picked: true, online: false));
    }

    [Fact]
    public void More_farmers_than_the_panel_names_are_counted()
    {
        Assert.Equal("", MapWords.More(0));
        Assert.Equal("and 1 more", MapWords.More(1));
        Assert.Equal("and 4 more", MapWords.More(4));
    }

    [Fact]
    public void The_last_line_says_what_a_press_does()
    {
        Assert.Equal("Press to go there.", MapWords.Foot(here: false));
        Assert.Equal("You are on this farm.", MapWords.Foot(here: true));
    }

    [Fact]
    public void A_press_on_the_farm_you_stand_on_is_answered_not_travelled()
    {
        Assert.Equal("You are already on this farm. Pick another to travel there.", MapWords.AlreadyHere);
        Assert.NotEqual(MapWords.AlreadyHere, MapWords.Foot(here: true));
    }
}

public class HoverPanelTests
{
    public static IEnumerable<object[]> Screens() => new[]
    {
        new object[] { 1280, 720 }, new object[] { 1024, 768 }, new object[] { 1920, 1080 }, new object[] { 2560, 1440 }, new object[] { 800, 600 }, new object[] { 3840, 2160 },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_panel_is_on_the_screen_and_never_over_its_farmstead(int width, int height)
    {
        // A farmstead with two faces takes about 180 by 92; the largest panel (six farmers) about 440 by 420.
        // The menu makes a panel no wider than the room beside its farmstead (WidthRoom), as here.
        foreach (var (wide, ph) in new[] { (300, 180), (440, 300), (440, 420) })
            for (int x = 0; x <= width - 180; x += 37)
                for (int y = 0; y <= height - 92; y += 29)
                {
                    var about = new Box(x, y, 180, 92);
                    int pw = Math.Min(wide, HoverPanel.WidthRoom(width, about));
                    Assert.True(pw >= 200, $"{width}x{height}: only {pw} pixels beside a farmstead at {x},{y}");
                    var panel = HoverPanel.Place(width, height, pw, ph, about);
                    Assert.True(HoverPanel.OnScreen(panel, width, height), $"{width}x{height}: panel {panel} for a farmstead at {x},{y} is off the screen");
                    Assert.False(HoverPanel.Overlaps(panel, about), $"{width}x{height}: panel {panel} covers the farmstead at {x},{y}");
                }
    }

    [Fact]
    public void It_stands_to_the_right_when_there_is_room_and_to_the_left_by_the_right_edge()
    {
        var about = new Box(200, 300, 180, 92);
        Assert.Equal(about.Right + HoverPanel.Gap, HoverPanel.Place(1280, 720, 300, 200, about).X);
        var byEdge = new Box(1080, 300, 180, 92);
        Assert.Equal(byEdge.X - HoverPanel.Gap - 300, HoverPanel.Place(1280, 720, 300, 200, byEdge).X);
    }

    [Fact]
    public void A_farmstead_as_wide_as_the_screen_gets_its_panel_below_or_above()
    {
        var wide = new Box(40, 100, 1200, 92);
        var panel = HoverPanel.Place(1280, 720, 300, 200, wide);
        Assert.Equal(wide.Bottom + HoverPanel.Gap, panel.Y);
        var low = new Box(40, 560, 1200, 92);
        Assert.Equal(low.Y - HoverPanel.Gap - 200, HoverPanel.Place(1280, 720, 300, 200, low).Y);
    }

    [Fact]
    public void On_a_screen_too_small_for_any_side_it_is_still_on_the_screen()
    {
        var panel = HoverPanel.Place(400, 300, 380, 280, new Box(150, 100, 100, 80));
        Assert.True(panel.X >= 0 && panel.Y >= 0 && panel.Right <= 400 && panel.Bottom <= 300);
    }

    [Fact]
    public void The_room_beside_a_farmstead_is_its_roomier_side()
    {
        Assert.Equal(1280 - 380 - HoverPanel.Gap - HoverPanel.Edge, HoverPanel.WidthRoom(1280, new Box(200, 300, 180, 92)));
        Assert.Equal(1000 - HoverPanel.Gap - HoverPanel.Edge, HoverPanel.WidthRoom(1280, new Box(1000, 300, 180, 92)));
    }
}

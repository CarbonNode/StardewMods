using ManyFarms.Core;
using Xunit;

namespace ManyFarms.Tests;

/// <summary>0.4.0: a house upgrade is seen on the farmer's own farm. What the house shows, where Robin works, what is said.</summary>
public class HouseUpgradeTests
{
    [Theory]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { 0 }, 0)]
    [InlineData(new[] { 1 }, 1)]
    [InlineData(new[] { 3 }, 3)]
    // Two farmers of one farm: the house shows the one who has built furthest.
    [InlineData(new[] { 0, 2 }, 2)]
    [InlineData(new[] { 2, 1, 0 }, 2)]
    // A level the game does not have is no look the picture has.
    [InlineData(new[] { 9 }, 3)]
    [InlineData(new[] { -1 }, 0)]
    public void A_house_shows_the_highest_upgrade_among_its_farmers(int[] levels, int shown)
    {
        Assert.Equal(shown, HouseLook.Level(levels));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    // The cellar changes nothing outside: the game draws a cabin at level 3 as at level 2.
    [InlineData(3, 2)]
    public void The_picture_has_three_looks(int level, int look)
    {
        Assert.Equal(look, HouseLook.Look(level));
    }

    [Theory]
    // The game's cabin picture: three looks of 80 pixels side by side in 240.
    [InlineData(0, 80, 0, 240, 0)]
    [InlineData(1, 80, 0, 240, 80)]
    [InlineData(2, 80, 0, 240, 160)]
    [InlineData(3, 80, 0, 240, 160)]
    // A picture with one look only (another mod's cabin): drawn as built, never from beyond its edge.
    [InlineData(2, 80, 0, 80, 0)]
    [InlineData(2, 80, 0, 160, 0)]
    [InlineData(1, 80, 0, 160, 80)]
    // A source rectangle that does not start at the left edge.
    [InlineData(1, 80, 80, 240, 80)]
    [InlineData(2, 80, 80, 240, 0)]
    [InlineData(1, 0, 0, 240, 0)]
    public void The_look_is_taken_from_inside_the_picture_or_not_at_all(int level, int lookWidth, int startX, int pictureWidth, int shift)
    {
        Assert.Equal(shift, HouseLook.ShiftX(level, lookWidth, startX, pictureWidth));
    }

    private const long Host = 1;

    [Fact]
    public void Robin_works_only_for_a_farmer_who_is_there_with_an_upgrade_and_a_house_of_their_own()
    {
        Assert.Null(BuilderRule.Pick(Array.Empty<Upgrader>(), Host));
        // No farm of their own: their cabin on the main farm upgrades as in an ordinary game, with nobody sent anywhere.
        Assert.Null(BuilderRule.Pick(new[] { new Upgrader(5, 3, true, null) }, Host));
        // Nothing being built.
        Assert.Null(BuilderRule.Pick(new[] { new Upgrader(5, -1, true, "P1"), new Upgrader(6, 0, true, "P2") }, Host));
        // Not in the game: the game counts no nights for them, so she would never leave.
        Assert.Null(BuilderRule.Pick(new[] { new Upgrader(5, 2, false, "P1") }, Host));
        Assert.Equal(5, BuilderRule.Pick(new[] { new Upgrader(5, 2, true, "P1") }, Host)!.Value.Id);
    }

    [Fact]
    public void Of_several_upgrades_the_hosts_comes_first_then_the_nearest_done_then_the_lowest_number()
    {
        var all = new[]
        {
            new Upgrader(9, 1, true, "P3"),
            new Upgrader(4, 3, true, "P1"),
            new Upgrader(7, 1, true, "P2"),
            new Upgrader(Host, 3, true, "P4"),
        };
        Assert.Equal(Host, BuilderRule.Pick(all, Host)!.Value.Id);
        Assert.Equal(7, BuilderRule.Pick(all, hostId: 99)!.Value.Id);
        Assert.Equal(7, BuilderRule.Pick(all.Reverse(), hostId: 99)!.Value.Id);
        // The host with no farm of their own is the game's to place: the rule does not pick them.
        Assert.Equal(7, BuilderRule.Pick(all.Take(3).Append(new Upgrader(Host, 3, true, null)), Host)!.Value.Id);
        // Two farmers of one farm: either way she is at that farm's one house.
        Assert.Equal("P1", BuilderRule.Pick(new[] { new Upgrader(4, 3, true, "P1"), new Upgrader(5, 2, true, "P1") }, Host)!.Value.HouseOn);
    }

    [Fact]
    public void A_farmer_who_paid_today_gets_Robin_from_the_next_morning()
    {
        Assert.Null(BuilderRule.Pick(new[] { new Upgrader(5, 3, true, "P1", PaidToday: true) }, Host));
        Assert.Equal(6, BuilderRule.Pick(new[] { new Upgrader(5, 3, true, "P1", PaidToday: true), new Upgrader(6, 3, true, "P2") }, Host)!.Value.Id);

        var watch = new OrderWatch();
        // First look of the session: an upgrade already under way was paid for on some earlier day.
        Assert.False(watch.PaidToday(5, 2, today: 10));
        // Nothing under way, then some: paid just now.
        Assert.False(watch.PaidToday(6, -1, today: 10));
        Assert.True(watch.PaidToday(6, 3, today: 10));
        Assert.True(watch.PaidToday(6, 3, today: 10));
        // The next morning the game has counted a night, and she may start.
        Assert.False(watch.PaidToday(6, 2, today: 11));
        Assert.False(watch.PaidToday(6, 1, today: 12));
        // Done, and paid for again the same day: today's payment.
        Assert.False(watch.PaidToday(6, -1, today: 13));
        Assert.True(watch.PaidToday(6, 3, today: 13));
        watch.Forget();
        Assert.False(watch.PaidToday(6, 3, today: 13));
    }

    [Fact]
    public void Robin_stands_on_the_porch_away_from_the_door_and_the_mailbox()
    {
        // The game's cabin: 5 by 3, door at 2, 1, so the doorstep is on the porch row; the mailbox is its right-hand end.
        var topLeft = new TilePoint(62, 13);
        var size = new TilePoint(5, 3);
        var door = new TilePoint(2, 1);
        var step = HouseSpot.DoorStep(topLeft, door);
        var mailbox = HouseSpot.Mailbox(topLeft, size, door);
        var spots = BuilderRule.Spots(topLeft, size, step, mailbox).ToList();

        Assert.Equal(new TilePoint(62, 15), spots[0]);
        Assert.Equal(2, step.X - spots[0].X);
        // The next one is the porch tile beside it, still clear of the doorstep: used when something stands in front of the first.
        Assert.Equal(new TilePoint(63, 15), spots[1]);
        Assert.DoesNotContain(step, spots);
        Assert.DoesNotContain(mailbox, spots);
        // Nor the tile in front of the mailbox, where a farmer stands to read their mail.
        Assert.DoesNotContain(new TilePoint(mailbox.X, mailbox.Y + 1), spots);
        Assert.Equal(spots.Count, spots.Distinct().Count());
        // Every spot touches the house or its porch row.
        Assert.All(spots, s => Assert.True(s.X >= topLeft.X - 1 && s.X <= topLeft.X + size.X && s.Y >= topLeft.Y + size.Y - 1 && s.Y <= topLeft.Y + size.Y));
    }

    /// <summary>
    /// The game's farmhouse as the 0.5.0 lab read it off the real game (Data/Buildings "Farmhouse" in 1.6.15): 9 by 5,
    /// the door at 5, 2, stood at 59, 12 on the standard map with its doorstep on 64, 15 (row 3 of the house), and its
    /// own mailbox action tile at 9, 4: beside the house, outside the footprint, where the main farm's mailbox is (68, 16).
    /// </summary>
    private static readonly TilePoint FarmhouseSize = new(9, 5);
    private static readonly TilePoint FarmhouseDoor = new(5, 2);
    private static readonly TilePoint FarmhouseMailbox = new(9, 4);

    [Theory]
    // The game's cabin: 5 by 3, door at 2, 1; its bottom row is its porch, and the doorstep is on it. Its mailbox: the bottom-right tile.
    [InlineData(5, 3, 2, 1, 4, 2)]
    // The game's farmhouse (0.5.0): 9 by 5 with the door at 5, 2, so the doorstep is the open row inside its footprint; its mailbox beside it at 9, 4.
    [InlineData(9, 5, 5, 2, 9, 4)]
    // A house wider than four tiles right of its door, one whose door is on its bottom row, and one with the door on the top row of a deep footprint.
    [InlineData(12, 6, 5, 5, 9, 5)]
    [InlineData(9, 6, 5, 5, 8, 5)]
    [InlineData(10, 4, 3, 1, 7, 3)]
    public void Robins_spots_are_on_the_doorstep_row_for_either_shape_and_never_on_the_door_or_the_mailbox(int wide, int high, int doorX, int doorY, int mailX, int mailY)
    {
        var topLeft = new TilePoint(59, 12);
        var size = new TilePoint(wide, high);
        var door = new TilePoint(doorX, doorY);
        var step = HouseSpot.DoorStep(topLeft, door);
        var mailbox = new TilePoint(topLeft.X + mailX, topLeft.Y + mailY);
        var spots = BuilderRule.Spots(topLeft, size, step, mailbox).ToList();

        // The porch row is the doorstep's row: the bottom row of a cabin, the third row of the farmhouse, the row in front of a house whose door is on its bottom row.
        Assert.Equal(new TilePoint(topLeft.X, step.Y), spots[0]);
        Assert.Equal(step.Y, topLeft.Y + door.Y + 1);
        Assert.All(spots, s => Assert.True(s.Y == step.Y || s.Y == step.Y + 1));
        Assert.All(spots, s => Assert.True(s.X >= topLeft.X - 1 && s.X <= topLeft.X + size.X));
        Assert.DoesNotContain(step, spots);
        Assert.DoesNotContain(mailbox, spots);
        Assert.DoesNotContain(new TilePoint(mailbox.X, mailbox.Y + 1), spots);
        Assert.Equal(spots.Count, spots.Distinct().Count());
        Assert.True(spots.Count >= 5);
    }

    [Fact]
    public void The_farmhouses_mailbox_beside_it_keeps_Robin_off_it_and_off_the_tile_in_front()
    {
        // 59, 12: the farmhouse's right-hand spots, 68, 15 and 68, 16, are the tile above its mailbox (68, 16 is the mailbox itself) and the mailbox.
        var topLeft = new TilePoint(59, 12);
        var step = HouseSpot.DoorStep(topLeft, FarmhouseDoor);
        var mailbox = new TilePoint(topLeft.X + FarmhouseMailbox.X, topLeft.Y + FarmhouseMailbox.Y);
        Assert.Equal(new TilePoint(68, 16), mailbox);
        var spots = BuilderRule.Spots(topLeft, FarmhouseSize, step, mailbox).ToList();
        Assert.DoesNotContain(new TilePoint(68, 16), spots);
        Assert.DoesNotContain(new TilePoint(68, 17), spots);
        Assert.Contains(new TilePoint(68, 15), spots);
        Assert.Equal(new TilePoint(59, 15), spots[0]);
    }

    [Theory]
    // The cabin's mailbox is its bottom-right tile, as before 0.5.0 (the rule for an entry that has no mailbox tile of its own).
    [InlineData(5, 3, 2, 1, 4, 2)]
    // On the bottom row, four right of the door (the main farm's mailbox is four right of its door), or that row's right-hand end when the house is narrower.
    [InlineData(9, 5, 5, 2, 8, 4)]
    [InlineData(12, 6, 5, 5, 9, 5)]
    [InlineData(10, 4, 3, 1, 7, 3)]
    public void Without_a_mailbox_of_its_own_a_house_gets_one_on_the_bottom_row_four_right_of_the_door_or_at_its_end(int wide, int high, int doorX, int doorY, int mailX, int mailY)
    {
        var at = HouseSpot.Mailbox(new TilePoint(0, 0), new TilePoint(wide, high), new TilePoint(doorX, doorY));
        Assert.Equal(new TilePoint(mailX, mailY), at);
        Assert.True(at.X < wide && at.Y < high);
        // Placed on a map, it moves with the house.
        Assert.Equal(new TilePoint(59 + mailX, 12 + mailY), HouseSpot.Mailbox(new TilePoint(59, 12), new TilePoint(wide, high), new TilePoint(doorX, doorY)));
    }

    [Theory]
    // The cabin's picture: three looks side by side, 80 wide in 240.
    [InlineData(1, 80, 112, 0, 0, 240, 112, 80, 0)]
    [InlineData(2, 80, 112, 0, 0, 240, 112, 160, 0)]
    // The farmhouse's Buildings\houses: three looks one above the other, 160 by 144 in 160 by 432 (the lab saw no shift to the right at level 2).
    [InlineData(1, 160, 144, 0, 0, 160, 432, 0, 144)]
    [InlineData(2, 160, 144, 0, 0, 160, 432, 0, 288)]
    [InlineData(3, 160, 144, 0, 0, 160, 432, 0, 288)]
    [InlineData(0, 160, 144, 0, 0, 160, 432, 0, 0)]
    // A picture with one look only: as built.
    [InlineData(2, 160, 144, 0, 0, 160, 144, 0, 0)]
    // Wide enough for the looks side by side: that way, as a cabin's, even when tall enough as well.
    [InlineData(1, 160, 144, 0, 0, 480, 432, 160, 0)]
    public void The_look_is_moved_right_in_a_wide_picture_and_down_in_a_tall_one(int level, int w, int h, int startX, int startY, int pictureW, int pictureH, int dx, int dy)
    {
        Assert.Equal((dx, dy), HouseLook.Shift(level, w, h, startX, startY, pictureW, pictureH));
    }

    [Fact]
    public void A_house_stood_in_the_cabins_shape_is_stood_again_in_the_farmhouses_with_its_doorstep_where_it_was()
    {
        // The live save's plot 1 house: a 0.3.0 cabin shape with its doorstep on the map's FarmHouseEntry, 64, 15.
        var oldTopLeft = HouseSpot.TopLeft(new TilePoint(64, 15), HouseShape.Cabin.Door);
        Assert.Equal(new TilePoint(62, 13), oldTopLeft);
        var farmhouse = new HouseShape("Farmhouse", FarmhouseSize, FarmhouseDoor);
        var (step, topLeft) = HouseSpot.Restand(oldTopLeft, HouseShape.Cabin, farmhouse);
        Assert.Equal(new TilePoint(64, 15), step);
        // Where the lab saw the game stand the farmhouse on the standard map.
        Assert.Equal(new TilePoint(59, 12), topLeft);
        Assert.Equal(step, HouseSpot.DoorStep(topLeft, farmhouse.Door));
        // Same ground, whatever the shape is called: nothing to move.
        Assert.True(HouseShape.Cabin.SameGround(new HouseShape("Farmhouse", new TilePoint(5, 3), new TilePoint(2, 1))));
        Assert.False(HouseShape.Cabin.SameGround(farmhouse));
    }

    [Fact]
    public void The_shape_a_house_was_stood_with_is_written_on_it_and_read_back()
    {
        var shape = new HouseShape("Farmhouse", FarmhouseSize, FarmhouseDoor);
        Assert.Equal("Farmhouse;9,5;5,2", shape.ToString());
        Assert.Equal(shape, HouseShape.Parse("Farmhouse;9,5;5,2"));
        Assert.Equal(HouseShape.Cabin, HouseShape.Parse(HouseShape.Cabin.ToString()));
        Assert.Null(HouseShape.Parse(null));
        Assert.Null(HouseShape.Parse(""));
        Assert.Null(HouseShape.Parse("Farmhouse"));
        Assert.Null(HouseShape.Parse("Farmhouse;9,6"));
        Assert.Null(HouseShape.Parse("Farmhouse;0,6;5,5"));
        Assert.Null(HouseShape.Parse("Farmhouse;a,b;5,5"));
    }

    [Fact]
    public void What_a_farmer_last_saw_of_their_upgrade_is_kept_on_them_and_read_back()
    {
        Assert.Equal("3,1", UpgradeMemory.Keep(3, 1));
        Assert.Equal((3, 1), UpgradeMemory.Read("3,1"));
        Assert.Equal((-1, 0), UpgradeMemory.Read(UpgradeMemory.Keep(-1, 0)));
        Assert.Null(UpgradeMemory.Read(null));
        Assert.Null(UpgradeMemory.Read(""));
        Assert.Null(UpgradeMemory.Read("3"));
        Assert.Null(UpgradeMemory.Read("x,1"));
        // An upgrade finished while the farmer was away (a turn mod counted their nights): told on their first look back, from what they last saw.
        Assert.Equal(HouseWords.Finished(1), HouseWords.About(UpgradeMemory.Read("2,0"), -1, 1, true, "Squeet Farm"));
        // A farmer who never looked is told nothing of what is already so.
        Assert.Null(HouseWords.About(UpgradeMemory.Read(null), -1, 1, true, "Squeet Farm"));
    }

    [Fact]
    public void With_their_nights_counted_for_them_the_ordered_line_says_so_instead_of_telling_them_to_stay()
    {
        string counted = HouseWords.Ordered(3, "Squeet Farm", nightsCountedWhileAway: true);
        Assert.Contains("Your nights are counted for you while you are away, so it goes on being built when you leave.", counted);
        Assert.DoesNotContain("stay until the night has passed", counted);
        Assert.Contains("stay until the night has passed", HouseWords.Ordered(3, "Squeet Farm"));
        Assert.Equal(counted, HouseWords.About((-1, 0), 3, 0, true, "Squeet Farm", nightsCountedWhileAway: true));
    }

    [Fact]
    public void Robin_stays_at_an_absent_farmers_house_when_their_nights_are_counted_for_them()
    {
        // The host hands the rule an absent farmer as in the game when Shared Day counts their nights (Builder.Upgraders): the rule itself is unchanged.
        var away = new Upgrader(5, 2, false, "P1");
        Assert.Null(BuilderRule.Pick(new[] { away }, Host));
        Assert.Equal(5, BuilderRule.Pick(new[] { away with { Online = true } }, Host)!.Value.Id);
        Assert.True(BuilderRule.WantedAt(new[] { away with { Online = true } }, "P1"));
    }

    [Fact]
    public void A_house_whose_door_is_at_its_left_end_keeps_its_doorstep_free()
    {
        var topLeft = new TilePoint(10, 10);
        var step = new TilePoint(10, 12);
        var mailbox = new TilePoint(14, 12);
        Assert.DoesNotContain(step, BuilderRule.Spots(topLeft, new TilePoint(5, 3), step, mailbox));
        // The porch tile beside the door comes first then, and the ground left of the house after it.
        Assert.Equal(new[] { new TilePoint(11, 12), new TilePoint(9, 12) }, BuilderRule.Spots(topLeft, new TilePoint(5, 3), step, mailbox).Take(2));
    }

    [Fact]
    public void An_upgrade_just_paid_for_is_told_once_with_where_and_when()
    {
        string? line = HouseWords.About((-1, 0), 3, 0, houseOnOwnFarm: true, "Squeet Farm");
        Assert.Equal("Robin builds your house upgrade at the house on your own farm. When she is not busy elsewhere you will find her there. It is ready after 3 nights that you sleep through. The game counts a night only while you are in it, so on a farm played in turns stay until the night has passed. Your cabin on Squeet Farm is the same home, so it grows too.", line);
        // The host's farmer has no cabin: their home on the main farm is the farmhouse.
        string? hosts = HouseWords.About((-1, 0), 3, 0, houseOnOwnFarm: true, "Squeet Farm", farmhouse: true);
        Assert.Contains("Your farmhouse on Squeet Farm is the same home", hosts);
        Assert.DoesNotContain("cabin", hosts);
        // She may be at a building, the town's upgrade or another farmer's house first: no promise that she is there.
        Assert.DoesNotContain("you will find her there.", line!.Replace("When she is not busy elsewhere you will find her there.", ""));
        // Still being built the next time it is looked at: nothing more.
        Assert.Null(HouseWords.About((3, 0), 3, 0, true, "Squeet Farm"));
        Assert.Null(HouseWords.About((3, 0), 2, 0, true, "Squeet Farm"));
        Assert.Contains("ready tomorrow morning", HouseWords.Ordered(1, "Squeet Farm"));
    }

    [Fact]
    public void A_finished_upgrade_is_told_once_and_the_cellar_by_name()
    {
        Assert.Equal("Your house upgrade is finished. The house on your farm shows it, and the bigger rooms are behind its door.", HouseWords.About((1, 0), -1, 1, true, "Squeet Farm"));
        Assert.Equal(HouseWords.Finished(2), HouseWords.About((1, 1), -1, 2, true, "Squeet Farm"));
        Assert.Equal("Your cellar is finished. Go in by the house on your farm: the stairs down are in your kitchen.", HouseWords.About((1, 2), -1, 3, true, "Squeet Farm"));
        Assert.Null(HouseWords.About((-1, 1), -1, 1, true, "Squeet Farm"));
    }

    [Fact]
    public void Nothing_is_said_without_a_house_of_their_own_or_about_what_was_so_on_joining()
    {
        // No farm of their own: an ordinary game says nothing, and nor does the mod.
        Assert.Null(HouseWords.About((-1, 0), 3, 0, houseOnOwnFarm: false, "Squeet Farm"));
        Assert.Null(HouseWords.About((1, 0), -1, 1, houseOnOwnFarm: false, "Squeet Farm"));
        // The first look of a session: an upgrade ordered yesterday is not news.
        Assert.Null(HouseWords.About(null, 2, 0, true, "Squeet Farm"));
        Assert.Null(HouseWords.About(null, -1, 3, true, "Squeet Farm"));
    }

    [Fact]
    public void No_line_about_an_upgrade_uses_a_word_a_new_player_would_not_know()
    {
        var lines = new[] { HouseWords.Ordered(3, "Squeet Farm"), HouseWords.Ordered(1, "Squeet Farm"), HouseWords.Ordered(3, "Squeet Farm", farmhouse: true), HouseWords.OrderedShort, HouseWords.Finished(1), HouseWords.Finished(3), HouseWords.RobinOut("Alice's farm") };
        foreach (string line in lines)
        {
            foreach (string word in new[] { "plot", "main farm", "modData", "slot", "interior", "warp", "host" })
                Assert.DoesNotContain(word, line, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".", line);
        }
    }

    [Fact]
    public void Robins_counter_says_where_she_is_and_that_it_still_serves()
    {
        Assert.Equal("Robin is out building a house upgrade at Alice and Bob's farm. Her order book is on the counter, so you can still buy and order.", HouseWords.RobinOut("Alice and Bob's farm"));
    }

    [Fact]
    public void Robin_stays_at_a_house_only_while_a_farmer_of_that_farm_who_is_there_has_the_upgrade_under_way()
    {
        var a = new Upgrader(5, 2, true, "P1");
        Assert.True(BuilderRule.WantedAt(new[] { a }, "P1"));
        Assert.False(BuilderRule.WantedAt(new[] { a }, "P2"));
        Assert.False(BuilderRule.WantedAt(new[] { a }, null));
        // The farmer left the game, the upgrade is done, or it was paid for today.
        Assert.False(BuilderRule.WantedAt(new[] { a with { Online = false } }, "P1"));
        Assert.False(BuilderRule.WantedAt(new[] { a with { DaysLeft = 0 } }, "P1"));
        Assert.False(BuilderRule.WantedAt(new[] { a with { DaysLeft = -1 } }, "P1"));
        Assert.False(BuilderRule.WantedAt(new[] { a with { PaidToday = true } }, "P1"));
        // Either farmer of a shared farm keeps her there, whoever the rule would pick first.
        Assert.True(BuilderRule.WantedAt(new[] { a with { Online = false }, new Upgrader(6, 3, true, "P1") }, "P1"));
        Assert.False(BuilderRule.WantedAt(Array.Empty<Upgrader>(), "P1"));
    }

    [Theory]
    // At a house where she is still wanted: left there, whatever else is true (once there she stays for the day).
    [InlineData(true, true, false, true, false, true, false, RobinCall.Leave)]
    [InlineData(true, true, true, true, true, true, true, RobinCall.Leave)]
    // Nowhere yet, and there is a house to go to.
    [InlineData(false, false, true, false, false, false, false, RobinCall.Send)]
    // At a house that needs her no more, and another does: sent on.
    [InlineData(true, false, true, true, false, true, false, RobinCall.Send)]
    // At a house that needs her no more (the farmer left the game, the upgrade finished overnight): home.
    [InlineData(true, false, false, true, false, true, false, RobinCall.Home)]
    // The same while the game has a building under construction (it may stand beside that house): the game's, left alone.
    [InlineData(true, false, false, true, true, true, true, RobinCall.Leave)]
    // Found standing on a plot, not hammering, nothing built there (a save written with her on it): home.
    [InlineData(false, false, false, true, false, false, false, RobinCall.Home)]
    // On a plot where the game has her constructing a building: the game's, left alone.
    [InlineData(false, false, false, true, true, true, true, RobinCall.Leave)]
    [InlineData(false, false, false, true, true, false, true, RobinCall.Leave)]
    // Back in her own house by a turn mod's morning, this mod's hammering still on, nothing to build: set on her day afresh.
    [InlineData(false, false, false, false, false, true, false, RobinCall.Home)]
    // Hammering for the game's own work (the host's farmhouse, a building, the town): left alone.
    [InlineData(false, false, false, false, false, true, true, RobinCall.Leave)]
    [InlineData(false, false, false, false, true, true, true, RobinCall.Leave)]
    // An ordinary day.
    [InlineData(false, false, false, false, false, false, false, RobinCall.Leave)]
    [InlineData(false, false, false, false, false, false, true, RobinCall.Leave)]
    public void What_the_host_does_about_Robin_at_one_look(bool atHouse, bool wantedThere, bool canSend, bool onPlot, bool buildingThere, bool hammering, bool gameWork, RobinCall call)
    {
        Assert.Equal(call, BuilderRule.Call(atHouse, wantedThere, canSend, onPlot, buildingThere, hammering, gameWork));
    }

    [Fact]
    public void Sent_home_in_the_middle_of_a_day_she_is_where_her_schedule_has_her_by_now()
    {
        int[] day = { 800, 1700, 2100 };
        Assert.Null(BuilderRule.LastDue(day, 610));
        Assert.Equal(800, BuilderRule.LastDue(day, 800));
        Assert.Equal(800, BuilderRule.LastDue(day, 1650));
        Assert.Equal(1700, BuilderRule.LastDue(day, 1700));
        Assert.Equal(2100, BuilderRule.LastDue(new[] { 2100, 800, 1700 }, 2600));
        Assert.Null(BuilderRule.LastDue(Array.Empty<int>(), 1200));
    }
}

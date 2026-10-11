using ManyFarms.Core;
using Xunit;

namespace ManyFarms.Tests;

public class PickerWordsTests
{
    [Fact]
    public void A_farm_with_nobody_reads_as_no_farmers_yet_not_as_zero()
    {
        Assert.Equal("No farmers yet", PickerWords.Farmers(0));
        Assert.Equal("1 farmer", PickerWords.Farmers(1));
        Assert.Equal("2 farmers", PickerWords.Farmers(2));
        Assert.DoesNotContain("0", PickerWords.Farmers(0));
        // A count can never be drawn below nothing.
        Assert.Equal("No farmers yet", PickerWords.Farmers(-1));
    }

    [Fact]
    public void Giving_a_farm_back_says_what_is_lost_what_is_kept_and_asks_twice()
    {
        Assert.Equal("Give up my farm", PickerWords.GiveUpTitle);
        Assert.Equal("Its land, buildings and everything on it are gone for good; its plot is free again. You farm Squeet Farm with the others.", PickerWords.GiveUpDetail("Squeet Farm"));
        Assert.Equal("Give up Alice's farm?", PickerWords.GiveUpConfirmTitle("Alice's farm"));
        Assert.Equal("DELETE", PickerWords.GiveUpWord);
        Assert.Contains("Type DELETE to go on.", PickerWords.GiveUpConfirmWords);
        Assert.Equal("Type DELETE, in capitals, to give the farm up.", PickerWords.GiveUpNotTyped);
        Assert.Equal("Nothing was given up.", PickerWords.GiveUpCancelled);
        Assert.Equal("Your farm is given up and its plot is free. You farm Squeet Farm with the others; your cabin and everything in it are as they were.", PickerWords.AfterGiveUp("Squeet Farm"));
    }

    [Fact]
    public void The_heading_and_the_closing_button_fit_a_farmer_who_already_has_a_farm()
    {
        Assert.Equal("Pick your farm", PickerWords.Title(hasFarm: false));
        Assert.Equal("Decide later", PickerWords.CloseButton(hasFarm: false));
        // Nothing is being put off by somebody who has a farm.
        Assert.Equal("Change your farm", PickerWords.Title(hasFarm: true));
        Assert.Equal("Keep my farm", PickerWords.CloseButton(hasFarm: true));
    }

    [Theory]
    [InlineData(false, true, "Pick or start a farm")]
    [InlineData(false, false, "Pick a farm")]
    [InlineData(true, true, "Change my farm")]
    [InlineData(true, false, "Change my farm")]
    public void The_choice_that_opens_the_picker_promises_only_what_it_can_do(bool hasFarm, bool canStart, string expected)
    {
        Assert.Equal(expected, PickerWords.OpenChoice(hasFarm, canStart));
        string help = PickerWords.OpenChoiceHelp(hasFarm, canStart);
        Assert.StartsWith(expected + " opens the list of farms", help);
        Assert.Equal(canStart, help.Contains("start one of your own"));
        Assert.Contains("does not end your turn", help);
        Assert.Contains("you keep your cabin and everything in it", help);
        Assert.Contains("a house whose door opens into those same rooms", help);
        Assert.DoesNotContain("stay where", help);
    }

    [Fact]
    public void The_foot_says_why_no_farm_can_be_started_when_none_can()
    {
        Assert.Equal("The main farm is shared: pick it and you get no land of your own. Whatever you pick, you keep your cabin.", PickerWords.Hint(hostAllowsNewFarms: true, free: 8, total: 8));
        Assert.Equal("Whatever you pick, you keep your cabin. All 8 plots are farmed, so pick one of these farms.", PickerWords.Hint(true, 0, 8));
        Assert.Contains("switched off starting new farms", PickerWords.Hint(false, 8, 8));
        // The host's switch is the reason even when plots are left.
        Assert.DoesNotContain("plots are farmed", PickerWords.Hint(false, 0, 8));
    }

    [Fact]
    public void A_row_with_little_room_leaves_parts_out_whole_instead_of_cutting_the_line()
    {
        var parts = new[] { "Shared main farm", "Frontier Farm", "3 farmers", "yours now" };
        var giveUp = new[] { 1, 2, 3 };
        Func<int, Func<string, bool>> room = most => line => line.Length <= most;
        Assert.Equal("Shared main farm  |  Frontier Farm  |  3 farmers  |  yours now", PickerWords.Detail(parts, giveUp, room(80)));
        // The kind of land goes first, then the count the faces already show, and what is yours last.
        Assert.Equal("Shared main farm  |  3 farmers  |  yours now", PickerWords.Detail(parts, giveUp, room(50)));
        Assert.Equal("Shared main farm  |  yours now", PickerWords.Detail(parts, giveUp, room(30)));
        Assert.Equal("Shared main farm", PickerWords.Detail(parts, giveUp, room(20)));
        // What the row is always stays, even with no room at all.
        Assert.Equal("Shared main farm", PickerWords.Detail(parts, giveUp, room(0)));
    }

    [Fact]
    public void A_part_that_may_not_be_left_out_stays_on_the_line()
    {
        var parts = new[] { "Forest Farm", "2 farmers", "yours now" };
        Assert.Equal("yours now", PickerWords.Detail(parts, new[] { 0, 1 }, _ => false));
        Assert.Equal("Forest Farm  |  2 farmers  |  yours now", PickerWords.Detail(parts, Array.Empty<int>(), _ => false));
    }

    [Fact]
    public void The_main_farm_says_it_is_shared_and_gives_no_land_of_your_own()
    {
        Assert.Equal("Shared main farm", PickerWords.MainFarmTag);
        Assert.Equal("Press again to share Squeet Farm with the others. You get no land of your own there.", PickerWords.ArmNote("Squeet Farm", mainFarm: true));
        Assert.Equal("Press again to join Bob's farm. The house there opens into your own rooms.", PickerWords.ArmNote("Bob's farm", mainFarm: false));
    }

    [Fact]
    public void A_started_farm_says_it_has_a_house_before_and_after()
    {
        Assert.Equal("Your own land and a house. Its door opens into your own rooms: the same bed and chests as your cabin.", PickerWords.KindsHint);
        Assert.Equal("Press again to start a Forest Farm of your own, with a house on it.", PickerWords.ArmKindNote("Forest Farm"));
        Assert.Equal("Your farm has a house. Its door opens into your own rooms, the same ones as your cabin on Squeet Farm. The roads from town lead to your farm now. To visit Squeet Farm, use any Farmstead Totem.",
            PickerWords.AfterPick(started: true, pickedMainFarm: false, "Squeet Farm"));
        Assert.Equal("The house on this farm opens into your own rooms. The roads from town lead here now. To visit Squeet Farm, use any Farmstead Totem.",
            PickerWords.AfterPick(started: false, pickedMainFarm: false, "Squeet Farm"));
    }

    [Fact]
    public void A_farm_whose_house_could_not_be_built_is_not_said_to_have_one()
    {
        string started = PickerWords.AfterPick(started: true, pickedMainFarm: false, "Squeet Farm", house: false);
        Assert.StartsWith(RoadWords.HouseBlocked, started);
        Assert.DoesNotContain("has a house", started);
        Assert.EndsWith("The roads from town lead to your farm now. To visit Squeet Farm, use any Farmstead Totem.", started);
        string joined = PickerWords.AfterPick(started: false, pickedMainFarm: false, "Squeet Farm", house: false);
        Assert.DoesNotContain("opens into", joined);
        Assert.Contains("lead to this farm now", joined);
        // The main farm never had one to miss.
        Assert.Equal(PickerWords.AfterPick(false, true, "Squeet Farm"), PickerWords.AfterPick(false, true, "Squeet Farm", house: false));
    }

    [Fact]
    public void Picking_the_main_farm_promises_no_house_and_no_roads()
    {
        string line = PickerWords.AfterPick(started: false, pickedMainFarm: true, "Squeet Farm");
        Assert.Equal("You farm Squeet Farm with the others. Use any Farmstead Totem to visit another farm.", line);
        Assert.DoesNotContain("house", line);
        Assert.DoesNotContain("road", line);
    }

    [Fact]
    public void Nothing_the_picker_says_still_claims_a_started_farm_has_no_house()
    {
        foreach (string line in new[]
        {
            PickerWords.KindsHint, PickerWords.ArmKindNote("Forest Farm"), PickerWords.NewFarmDetail(3, 8),
            PickerWords.AfterPick(true, false, "Squeet Farm"), PickerWords.AfterPick(false, false, "Squeet Farm"), PickerWords.AfterPick(false, true, "Squeet Farm"),
            PickerWords.Hint(true, 3, 8), PickerWords.Hint(true, 0, 8), PickerWords.Hint(false, 3, 8),
            PickerWords.OpenChoiceHelp(false, true), PickerWords.OpenChoiceHelp(true, false),
        })
        {
            Assert.DoesNotContain("no house", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("land only", line, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_new_farm_row_counts_the_plots_left()
    {
        Assert.Equal("Your own land, with a house. 8 of 8 plots left.", PickerWords.NewFarmDetail(8, 8));
        Assert.Equal("Your own land, with a house. 1 of 8 plots left.", PickerWords.NewFarmDetail(1, 8));
        Assert.Equal("Your own land, with a house. 0 of 8 plots left.", PickerWords.NewFarmDetail(-3, 8));
    }

    [Fact]
    public void Closing_the_picker_names_the_turn_menu_first_when_it_offers_the_picker()
    {
        string line = PickerWords.WayBack("Squeet Farm", turnMenuKey: "F8", menuKey: null, totemStands: true, canStart: true, asksOnJoin: true);
        Assert.Equal("No farm picked: you farm Squeet Farm until you pick one. To pick later, press F8 and choose Pick or start a farm, "
            + "or use any Farmstead Totem and choose Pick or start a farm. It also asks again next time you join.", line);
        Assert.Equal("No farm picked. Press F8 to pick one later.", PickerWords.WayBackShort("F8", null));
    }

    [Fact]
    public void Closing_the_picker_never_names_a_way_that_is_not_there()
    {
        // No Shared Day, no key of its own and no totem set down yet: the one way left is to make a totem.
        string bare = PickerWords.WayBack("Squeet Farm", null, null, totemStands: false, canStart: true, asksOnJoin: true);
        Assert.DoesNotContain("press", bare);
        Assert.DoesNotContain("use any Farmstead Totem", bare);
        Assert.Contains("make a Farmstead Totem (15 wood and 10 fiber), set it down, use it and choose Pick or start a farm", bare);
        Assert.Contains("asks again next time you join", bare);
        Assert.Equal("No farm picked. Any Farmstead Totem lets you pick later.", PickerWords.WayBackShort(null, " "));

        // Its own menu key, and no plots left to start a farm on.
        string keyed = PickerWords.WayBack("The farm", "", "F7", totemStands: true, canStart: false, asksOnJoin: true);
        Assert.Contains("press F7 and choose Pick a farm", keyed);
        Assert.DoesNotContain("start", keyed);
        Assert.Equal("No farm picked. Press F7 to pick one later.", PickerWords.WayBackShort(null, "F7"));
    }

    [Fact]
    public void The_way_back_says_the_same_words_as_the_choice_it_points_at()
    {
        foreach (bool canStart in new[] { true, false })
            Assert.Contains("choose " + PickerWords.OpenChoice(false, canStart), PickerWords.WayBack("Squeet Farm", "F8", "F7", true, canStart, asksOnJoin: true));
    }

    [Fact]
    public void The_way_back_promises_a_question_on_joining_only_to_a_player_who_will_get_one()
    {
        // AskOnJoin is each player's own switch: with it off the picker never opens by itself.
        string off = PickerWords.WayBack("Squeet Farm", "F8", null, totemStands: true, canStart: true, asksOnJoin: false);
        Assert.Equal("No farm picked: you farm Squeet Farm until you pick one. To pick later, press F8 and choose Pick or start a farm, "
            + "or use any Farmstead Totem and choose Pick or start a farm.", off);
        Assert.DoesNotContain("next time you join", off);
        Assert.EndsWith("It also asks again next time you join.", PickerWords.WayBack("Squeet Farm", "F8", null, true, true, asksOnJoin: true));
    }
}

public sealed class PickerRulesTests
{
    [Fact]
    public void The_picker_does_not_open_over_a_festival_a_cutscene_a_fade_or_a_trip()
    {
        Assert.True(PickerRules.CanOpenNow(eventUp: false, fading: false, travelling: false));
        Assert.False(PickerRules.CanOpenNow(eventUp: true, fading: false, travelling: false));
        Assert.False(PickerRules.CanOpenNow(eventUp: false, fading: true, travelling: false));
        Assert.False(PickerRules.CanOpenNow(eventUp: false, fading: false, travelling: true));
    }

    [Fact]
    public void Picking_a_farm_takes_a_free_player_there_at_once()
    {
        Assert.True(PickerRules.GoesThereNow(playerFree: true, eventUp: false, travelling: false, heldByTurnMod: false));
    }

    [Fact]
    public void A_player_whose_day_is_done_is_not_sent_on_a_trip_under_the_waiting_popup()
    {
        // Free in the game's eyes for the tick between the picker closing and Shared Day's popup coming back.
        Assert.False(PickerRules.GoesThereNow(playerFree: true, eventUp: false, travelling: false, heldByTurnMod: true));
        Assert.Contains("When the next day starts, use any Farmstead Totem", PickerRules.GoLater(heldByTurnMod: true));
        Assert.Contains("Your day is finished", PickerRules.GoLater(heldByTurnMod: true));
    }

    [Fact]
    public void Nobody_is_carried_off_a_festival_or_out_of_a_menu_or_onto_a_second_trip()
    {
        // The game calls a player at a festival free.
        Assert.False(PickerRules.GoesThereNow(playerFree: true, eventUp: true, travelling: false, heldByTurnMod: false));
        Assert.False(PickerRules.GoesThereNow(playerFree: false, eventUp: false, travelling: false, heldByTurnMod: false));
        Assert.False(PickerRules.GoesThereNow(playerFree: true, eventUp: false, travelling: true, heldByTurnMod: false));
        Assert.Equal("You were not taken there now. Use any Farmstead Totem to go to your farm.", PickerRules.GoLater(heldByTurnMod: false));
    }
}

public class HostVisibilityTests
{
    [Fact]
    public void Without_a_turn_mod_the_switch_decides()
    {
        Assert.True(HostVisibility.Shown(hostPlaysSetting: true, turnModSays: null));
        Assert.False(HostVisibility.Shown(hostPlaysSetting: false, turnModSays: null));
    }

    [Fact]
    public void A_host_pc_that_only_hosts_is_nobodys_farmer_whatever_the_switch_says()
    {
        // Squeet Farm: HostPlays was left true, and "HostPC" stood among the farm's faces.
        Assert.False(HostVisibility.Shown(hostPlaysSetting: true, turnModSays: false));
        Assert.False(HostVisibility.Shown(false, false));
    }

    [Fact]
    public void A_host_who_really_plays_stays_visible_unless_switched_off_by_hand()
    {
        Assert.True(HostVisibility.Shown(true, true));
        Assert.False(HostVisibility.Shown(false, true));
    }
}

using ManyFarms.Core;
using Xunit;

namespace ManyFarms.Tests;

/// <summary>0.4.0, the first step of "the farm this call is about": the scope, the rewriting, the repeating, and the small rules that use them.</summary>
public class LayerTests
{
    private sealed class Land
    {
        public Land(string name) => Name = name;
        public string Name { get; }
    }

    [Fact]
    public void Between_calls_no_scope_is_open_and_the_answer_is_nothing()
    {
        var stack = new ScopeStack<Land>();
        Assert.Equal(0, stack.Depth);
        Assert.Null(stack.Current);
        Assert.Equal(0, stack.Reset());
    }

    [Fact]
    public void A_scope_is_about_its_farm_until_it_closes_and_then_the_outer_one_is_back()
    {
        var stack = new ScopeStack<Land>();
        var a = new Land("a");
        var b = new Land("b");
        int outer = stack.Enter(a);
        Assert.Same(a, stack.Current);
        int inner = stack.Enter(b);
        Assert.Same(b, stack.Current);
        Assert.Equal(2, stack.Depth);
        stack.Exit(inner);
        Assert.Same(a, stack.Current);
        stack.Exit(outer);
        Assert.Null(stack.Current);
        Assert.Equal(0, stack.Depth);
    }

    [Fact]
    public void A_scope_about_nothing_hides_the_outer_one()
    {
        var stack = new ScopeStack<Land>();
        int outer = stack.Enter(new Land("a"));
        int inner = stack.Enter(null);
        Assert.Null(stack.Current);
        stack.Exit(inner);
        Assert.NotNull(stack.Current);
        stack.Exit(outer);
    }

    [Fact]
    public void Closing_an_outer_scope_closes_what_was_left_open_inside_it()
    {
        var stack = new ScopeStack<Land>();
        int outer = stack.Enter(new Land("a"));
        stack.Enter(new Land("b"));
        stack.Enter(new Land("c"));
        stack.Exit(outer);
        Assert.Equal(0, stack.Depth);
    }

    [Fact]
    public void Closing_twice_or_closing_what_was_never_opened_does_nothing()
    {
        var stack = new ScopeStack<Land>();
        var a = new Land("a");
        int outer = stack.Enter(a);
        int inner = stack.Enter(new Land("b"));
        stack.Exit(inner);
        stack.Exit(inner);
        Assert.Same(a, stack.Current);
        // A handle that was never opened (a call that had no plot to be about).
        stack.Exit(0);
        stack.Exit(-3);
        stack.Exit(99);
        Assert.Equal(1, stack.Depth);
        stack.Exit(outer);
        Assert.Equal(0, stack.Depth);
    }

    [Fact]
    public void A_scope_left_open_is_found_counted_and_closed()
    {
        var stack = new ScopeStack<Land>();
        stack.Enter(new Land("a"));
        stack.Enter(new Land("b"));
        Assert.Equal(2, stack.Reset());
        Assert.Equal(0, stack.Depth);
        Assert.Null(stack.Current);
        // And the next call starts clean.
        int again = stack.Enter(new Land("c"));
        Assert.Equal(1, again);
        stack.Exit(again);
    }

    // ---- rewriting one method ----

    // A method as a list of instructions: "call getFarm", "ldstr Farm", "call op_Equality" and so on.
    private static bool IsGetFarm(string code) => code == "call getFarm";
    private static bool IsFarmLiteral(string code) => code == "ldstr Farm";
    private static bool IsEquals(string code) => code == "call op_Equality";

    [Fact]
    public void Every_call_to_the_farm_in_the_named_method_is_replaced_and_nothing_else()
    {
        var code = new List<string> { "ldarg.0", "call getFarm", "stloc.0", "call getFarmer", "call getFarm", "ret" };
        Assert.Equal(2, LexicalRewrite.Count(code, IsGetFarm));
        Assert.Equal(2, LexicalRewrite.ReplaceEach(code, IsGetFarm, _ => "call FarmOrMain"));
        Assert.Equal(new[] { "ldarg.0", "call FarmOrMain", "stloc.0", "call getFarmer", "call FarmOrMain", "ret" }, code);
        Assert.Equal(0, LexicalRewrite.Count(code, IsGetFarm));
    }

    [Fact]
    public void A_method_without_the_call_is_left_as_it_is()
    {
        var code = new List<string> { "ldarg.0", "ret" };
        Assert.Equal(0, LexicalRewrite.Count(code, IsGetFarm));
        Assert.Equal(0, LexicalRewrite.ReplaceEach(code, IsGetFarm, _ => "call FarmOrMain"));
        Assert.Equal(new[] { "ldarg.0", "ret" }, code);
    }

    [Fact]
    public void A_test_for_the_farms_name_becomes_the_question_and_keeps_its_length()
    {
        // GameLocation.spawnWeeds: ... name.Value == "Farm" ...
        var code = new List<string> { "ldarg.0", "ldfld name", "callvirt get_Value", "ldstr Farm", "call op_Equality", "brfalse L1", "ret" };
        Assert.Equal(1, LexicalRewrite.CountPairs(code, IsFarmLiteral, IsEquals));
        Assert.Equal(1, LexicalRewrite.ReplacePairs(code, IsFarmLiteral, IsEquals, _ => "nop", _ => "call IsAFarmName"));
        Assert.Equal(new[] { "ldarg.0", "ldfld name", "callvirt get_Value", "nop", "call IsAFarmName", "brfalse L1", "ret" }, code);
    }

    [Fact]
    public void The_word_Farm_used_for_anything_else_is_not_a_name_test()
    {
        // "Farm" handed to a lookup, and a comparison with another word.
        var code = new List<string> { "ldstr Farm", "call getLocationFromName", "ldstr Town", "call op_Equality", "ldstr Farm", "ret" };
        Assert.Equal(0, LexicalRewrite.CountPairs(code, IsFarmLiteral, IsEquals));
        Assert.Equal(0, LexicalRewrite.ReplacePairs(code, IsFarmLiteral, IsEquals, _ => "nop", _ => "call IsAFarmName"));
        Assert.Equal(6, code.Count);
        Assert.Equal("ldstr Farm", code[0]);
    }

    [Fact]
    public void Two_name_tests_in_one_method_are_both_found()
    {
        var code = new List<string> { "ldstr Farm", "call op_Equality", "ldstr Farm", "call op_Equality" };
        Assert.Equal(2, LexicalRewrite.CountPairs(code, IsFarmLiteral, IsEquals));
        Assert.Equal(2, LexicalRewrite.ReplacePairs(code, IsFarmLiteral, IsEquals, _ => "nop", _ => "q"));
        Assert.Equal(new[] { "nop", "q", "nop", "q" }, code);
    }

    [Fact]
    public void The_replacement_is_made_from_what_it_replaces()
    {
        // Whatever hangs on the old instruction (a jump lands on it) is handed to the maker of the new one.
        var code = new List<string> { "L7: call getFarm" };
        LexicalRewrite.ReplaceEach(code, c => c.EndsWith("call getFarm"), old => old.Replace("getFarm", "FarmOrMain"));
        Assert.Equal("L7: call FarmOrMain", code[0]);
    }

    // ---- repeating for every plot ----

    [Fact]
    public void Each_farm_gets_its_turn_in_order()
    {
        var seen = new List<int>();
        Assert.Equal(3, EachFarmRule.Run(new[] { 1, 4, 7 }, seen.Add, (_, _) => Assert.Fail("nothing fails here")));
        Assert.Equal(new[] { 1, 4, 7 }, seen);
        Assert.Equal(0, EachFarmRule.Run(Array.Empty<int>(), seen.Add, (_, _) => { }));
    }

    [Fact]
    public void One_farm_that_fails_is_reported_and_the_others_still_get_theirs()
    {
        var seen = new List<int>();
        var failed = new List<int>();
        int done = EachFarmRule.Run(new[] { 1, 2, 3 }, slot =>
        {
            if (slot == 2) throw new InvalidOperationException("no rods");
            seen.Add(slot);
        }, (slot, ex) => { failed.Add(slot); Assert.Equal("no rods", ex.Message); });
        Assert.Equal(2, done);
        Assert.Equal(new[] { 1, 3 }, seen);
        Assert.Equal(new[] { 2 }, failed);
    }

    // ---- the animal menu's view ----

    [Fact]
    public void The_view_opens_centred_on_the_animals_building()
    {
        // A coop (6 by 3) at 30, 20 on an 80 by 65 map, in a 1280 by 720 view.
        var (x, y) = AnimalView.Origin(new TilePoint(30, 20), new TilePoint(6, 3), 1280, 720, 80, 65);
        Assert.Equal(30 * 64 + 6 * 32 - 640, x);
        Assert.Equal(20 * 64 + 3 * 32 - 360, y);
    }

    [Theory]
    // A building in a corner: the view stops at the map's edge.
    [InlineData(0, 0, 0, 0)]
    [InlineData(78, 63, 80 * 64 - 1280, 65 * 64 - 720)]
    public void The_view_never_leaves_the_map(int tileX, int tileY, int wantX, int wantY)
    {
        Assert.Equal((wantX, wantY), AnimalView.Origin(new TilePoint(tileX, tileY), new TilePoint(2, 2), 1280, 720, 80, 65));
    }

    [Fact]
    public void A_map_smaller_than_the_view_is_shown_from_its_corner()
    {
        Assert.Equal((0, 0), AnimalView.Origin(new TilePoint(5, 5), new TilePoint(4, 3), 3840, 2160, 20, 20));
    }

    // ---- the world map ----

    [Fact]
    public void Every_plot_has_its_own_entry_on_the_world_map_and_is_found_again_by_it()
    {
        var ids = FarmSlots.All.Select(WorldMapIds.For).ToList();
        Assert.Equal(FarmSlots.Count, ids.Distinct().Count());
        foreach (int slot in FarmSlots.All)
        {
            Assert.True(WorldMapIds.TryPlot(WorldMapIds.For(slot), out int found));
            Assert.Equal(slot, found);
            Assert.Equal(new[] { FarmSlots.FarmName(slot), FarmSlots.CaveName(slot) }, WorldMapIds.Locations(slot));
        }
        Assert.Equal("CarbonNode.ManyFarms_Plot3", WorldMapIds.For(3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Farm")]
    [InlineData("CarbonNode.ManyFarms_Plot")]
    [InlineData("CarbonNode.ManyFarms_Plot0")]
    [InlineData("CarbonNode.ManyFarms_Plot9")]
    [InlineData("CarbonNode.ManyFarms_Plot01")]
    [InlineData("CarbonNode.ManyFarms_Farm1")]
    public void Any_other_entry_of_the_world_map_is_not_a_plots(string? id)
    {
        Assert.False(WorldMapIds.TryPlot(id, out _));
    }

    // ---- whose kind of farm a farmer's finds follow ----

    [Fact]
    public void A_farmers_finds_follow_their_own_farm_and_the_main_farm_when_they_have_none()
    {
        Assert.Equal("CarbonNode.ManyFarms_Farm2", FarmerFinds.FarmFor("CarbonNode.ManyFarms_Farm2"));
        Assert.Equal(FarmSlots.MainFarm, FarmerFinds.FarmFor(null));
    }
}

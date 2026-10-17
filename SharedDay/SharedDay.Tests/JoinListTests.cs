using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

public sealed class JoinListTests
{
    private static readonly JoinRow Free = new(Made: false, AnotherPlayers: false);
    private static readonly JoinRow Mine = new(Made: true, AnotherPlayers: false);
    private static readonly JoinRow Theirs = new(Made: true, AnotherPlayers: true);

    [Fact]
    public void AFirstTimeJoinerSeesOneNewFarmerRowHoweverManyCabinsAreFree()
    {
        // Squeet Farm on 2026-10-07: two empty cabins, shown as "1. New Farmer" and "2. New Farmer".
        var plan = JoinList.Plan(new[] { Free, Free });
        Assert.Equal(new[] { 0 }, plan.Rows);
        Assert.Equal(0, plan.NewFarmerRow);
        Assert.Equal(2, plan.FreeCabins);
    }

    [Fact]
    public void TheNewFarmerRowIsTheFirstFreeRowTheHostListed()
    {
        // Picking it must do exactly what picking the first free row did, so it IS that row.
        var plan = JoinList.Plan(new[] { Mine, Free, Theirs, Free, Free });
        Assert.Equal(1, plan.NewFarmerRow);
        Assert.Equal(3, plan.FreeCabins);
        Assert.Single(plan.Rows, row => row == plan.NewFarmerRow);
        Assert.DoesNotContain(3, plan.Rows);
        Assert.DoesNotContain(4, plan.Rows);
    }

    [Fact]
    public void AFarmerThePlayerAlreadyHasIsListedFirstAndOtherPlayersFarmersLast()
    {
        var plan = JoinList.Plan(new[] { Free, Theirs, Mine, Free, Mine });
        Assert.Equal(new[] { 2, 4, 0, 1 }, plan.Rows);
    }

    [Fact]
    public void EveryMadeFarmerKeepsItsRowAndTheHostsOrder()
    {
        var plan = JoinList.Plan(new[] { Mine, Mine, Theirs, Theirs });
        Assert.Equal(new[] { 0, 1, 2, 3 }, plan.Rows.Where(row => row != JoinList.NoRoomRow));
        Assert.Equal(-1, plan.NewFarmerRow);
        Assert.Equal(0, plan.FreeCabins);
    }

    [Fact]
    public void AFullFarmSaysSoWhereTheNewFarmerRowWouldBe()
    {
        // Every cabin claimed: without a word, a newcomer's only possible action is to take someone's farmer.
        Assert.Equal(new[] { 0, 1, JoinList.NoRoomRow, 2, 3 }, JoinList.Plan(new[] { Mine, Mine, Theirs, Theirs }).Rows);
        Assert.Equal(new[] { 0, JoinList.NoRoomRow }, JoinList.Plan(new[] { Mine }).Rows);
        Assert.Equal(new[] { JoinList.NoRoomRow, 0 }, JoinList.Plan(new[] { Theirs }).Rows);
        // With a cabin free there is no such line.
        Assert.DoesNotContain(JoinList.NoRoomRow, JoinList.Plan(new[] { Mine, Free }).Rows);
        Assert.Equal("No room for a new farmer", JoinList.NoRoomTitle);
        Assert.Equal("Every cabin is taken. Ask the host to build one.", JoinList.NoRoomDetail);
        Assert.Contains("Ask the host to build another cabin", JoinList.NoRoomHover);
    }

    [Fact]
    public void JoinedByAddressTheNewFarmerRowIsAlwaysOnTheFirstPage()
    {
        // Squeet Farm is joined by its address: the game marks nobody's farmer as another player's,
        // so four friends' farmers all count as pickable. A fifth, first-time joiner must not have to
        // scroll past them to find "Make a new farmer".
        var plan = JoinList.Plan(new[] { Mine, Mine, Mine, Mine, Free, Free });
        Assert.Equal(new[] { 0, 1, 2, 4, 3 }, plan.Rows);
        Assert.InRange(plan.Rows.ToList().IndexOf(plan.NewFarmerRow), 0, JoinList.PageRows - 1);
        // Up to three farmers the order is the plain one: farmers, then the new-farmer row.
        Assert.Equal(new[] { 0, 1, 2, 3 }, JoinList.Plan(new[] { Mine, Mine, Mine, Free }).Rows);
        // The same holds for the line that says a full farm is full.
        var full = JoinList.Plan(new[] { Mine, Mine, Mine, Mine, Mine });
        Assert.Equal(new[] { 0, 1, 2, JoinList.NoRoomRow, 3, 4 }, full.Rows);
    }

    [Fact]
    public void AnEmptyListStaysEmptyAndNoRowIsEverShownTwice()
    {
        Assert.Empty(JoinList.Plan(Array.Empty<JoinRow>()).Rows);
        var rows = JoinList.Plan(new[] { Free, Mine, Free, Theirs, Mine, Free }).Rows;
        Assert.Equal(rows.Count, rows.Distinct().Count());
        Assert.All(rows, row => Assert.InRange(row, 0, 5));
        Assert.DoesNotContain(JoinList.NoRoomRow, rows);
    }

    [Theory]
    [InlineData(1, "1 cabin free")]
    [InlineData(2, "2 cabins free")]
    [InlineData(8, "8 cabins free")]
    [InlineData(0, "0 cabins free")]
    [InlineData(-1, "0 cabins free")]
    public void TheNewFarmerRowSaysHowManyCabinsAreFree(int count, string expected)
    {
        Assert.Equal(expected, JoinList.CabinsFree(count));
        Assert.Equal("Make a new farmer", JoinList.NewFarmerTitle);
    }

    [Theory]
    // "Squeet Farm Farm" on the live farm.
    [InlineData("Squeet Farm", "Squeet Farm Farm", "Squeet Farm")]
    [InlineData("squeet farm", "squeet farm Farm", "squeet farm")]
    [InlineData(" Squeet Farm ", "Squeet Farm Farm", "Squeet Farm")]
    [InlineData("Farm", "Farm Farm", "Farm")]
    // Not doubled: the game's own line stands.
    [InlineData("Sunny", "Sunny Farm", "Sunny Farm")]
    [InlineData("Farmville", "Farmville Farm", "Farmville Farm")]
    [InlineData("Farm Sunny", "Farm Sunny Farm", "Farm Sunny Farm")]
    // Another language's wording is never guessed at.
    [InlineData("Squeet Farm", "Granja Squeet Farm", "Granja Squeet Farm")]
    [InlineData("", " Farm", " Farm")]
    [InlineData(null, " Farm", " Farm")]
    public void TheFarmsNameIsNotDoubled(string? farmName, string gameLine, string expected)
    {
        Assert.Equal(expected, JoinList.FarmTitle(farmName, gameLine));
    }
}

public sealed class FarmWordsTests
{
    [Theory]
    [InlineData("Sunny", "Sunny Farm")]
    [InlineData("Squeet Farm", "Squeet Farm")]
    [InlineData("squeet farm ", "squeet farm")]
    [InlineData("Windfarm", "Windfarm Farm")]
    [InlineData("Farm", "Farm")]
    public void AFarmIsNamedWithTheWordOnce(string name, string expected)
    {
        Assert.Equal(expected, FarmWords.Titled(name));
    }

    [Theory]
    [InlineData("Sunny", "Sunny")]
    [InlineData("Squeet Farm", "Squeet")]
    [InlineData("Squeet  FARM", "Squeet")]
    [InlineData("Windfarm", "Windfarm")]
    // A farm called just "Farm" keeps its name: there is nothing else to call it.
    [InlineData("Farm", "Farm")]
    public void ASentenceThatAddsTheWordItselfGetsTheNameWithoutIt(string name, string expected)
    {
        Assert.Equal(expected, FarmWords.Bare(name));
        // Whatever the name, the sentence never says the word twice in a row unless the name is the word.
        if (!name.Trim().Equals("Farm", StringComparison.OrdinalIgnoreCase))
            Assert.DoesNotContain(" farm farm", (" " + FarmWords.Bare(name) + " Farm").ToLowerInvariant());
    }
}

public sealed class JoinListWordsTests
{
    [Xunit.Fact]
    public void TheNewFarmerRowSaysWhatItIsNotHowManyCabins()
    {
        Xunit.Assert.Equal("Your own farmer on Squeet Farm", SharedDay.Core.JoinList.NewFarmerDetail("Squeet Farm"));
        Xunit.Assert.Equal("Your own farmer on this farm", SharedDay.Core.JoinList.NewFarmerDetail(null));
        Xunit.Assert.Equal("M0SSLING's farmer", SharedDay.Core.JoinList.OwnedBy("M0SSLING"));
    }
}

using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>
/// The farm an unattended host loads: the one asked for, exactly; with nothing asked, the one
/// hosted last; and never a different farm than the one named.
/// </summary>
public sealed class AutoHostChoiceTests
{
    private static readonly string[] Saves = { "Meadow_400864387", "Sunny_385772993", "Sunny_Side_116222875", "Orchard" };

    [Fact]
    public void NothingNamedHostsTheFarmHostedLast()
        => Assert.Equal("Sunny_385772993", AutoHostChoice.Pick(Saves, "", "sunny_385772993"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Deleted_1")]
    public void NothingNamedAndNothingRememberedHostsTheMostRecentlyPlayed(string? lastHosted)
        => Assert.Equal("Meadow_400864387", AutoHostChoice.Pick(Saves, "  ", lastHosted));

    [Theory]
    [InlineData("Sunny_385772993", "Sunny_385772993")]
    [InlineData("  sunny_385772993 ", "Sunny_385772993")]
    [InlineData("Sunny", "Sunny_385772993")]
    [InlineData("sunny_side", "Sunny_Side_116222875")]
    [InlineData("Orchard", "Orchard")]
    public void ANamedFarmIsHostedByFolderOrByFarmName(string configured, string expected)
        => Assert.Equal(expected, AutoHostChoice.Pick(Saves, configured, "Meadow_400864387"));

    [Theory]
    [InlineData("Sunn")]
    [InlineData("Sunny_1")]
    [InlineData("Valley")]
    public void ANameThatMatchesNoFarmHostsNothing(string configured)
        => Assert.Null(AutoHostChoice.Pick(Saves, configured, "Meadow_400864387"));

    [Fact]
    public void AFarmNameSharedByTwoSavesHostsNeither()
        => Assert.Null(AutoHostChoice.Pick(new[] { "Sunny_1", "Sunny_2" }, "Sunny", "Sunny_1"));

    [Fact]
    public void NoSavesHostsNothing()
        => Assert.Null(AutoHostChoice.Pick(Array.Empty<string>(), "", "Sunny_1"));

    [Theory]
    [InlineData("Sunny_385772993", "Sunny")]
    [InlineData("Sunny_Side_116222875", "Sunny_Side")]
    [InlineData("Orchard", "Orchard")]
    [InlineData("Farm_", "Farm_")]
    [InlineData("_12", "_12")]
    public void TheFarmNameIsTheFolderWithoutItsNumber(string save, string name)
        => Assert.Equal(name, AutoHostChoice.FarmName(save));
}

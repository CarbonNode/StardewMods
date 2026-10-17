using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>0.1.40: the host keeps a free cabin for every group member who still has no farmer.</summary>
public sealed class CabinPlanTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(2, 2, 0)]
    [InlineData(1, 4, 0)]
    [InlineData(3, -1, 3)]
    public void OneCabinPerMemberWithNoFarmerLessTheEmptyOnes(int members, int free, int expected)
        => Assert.Equal(expected, CabinPlan.HowMany(members, free));

    [Fact]
    public void TheFirstSpotIsTheOriginAndTheRestRingItOutwards()
    {
        var spots = CabinPlan.Spots(new Spot(60, 20), rings: 2).ToArray();
        Assert.Equal(new Spot(60, 20), spots[0]);
        // A cabin's own width and height apart, so they stand clear of one another.
        Assert.Contains(new Spot(66, 20), spots);
        Assert.Contains(new Spot(54, 16), spots);
        Assert.Equal(spots.Length, spots.Distinct().Count());
        // Nearest ring first.
        int far = Array.IndexOf(spots, new Spot(72, 28));
        int near = Array.IndexOf(spots, new Spot(66, 24));
        Assert.True(near < far);
    }

    [Fact]
    public void SpotsOffTheMapAreLeftOut()
    {
        Assert.All(CabinPlan.Spots(new Spot(2, 1), rings: 3), s => Assert.True(s.X >= 0 && s.Y >= 0));
        Assert.Single(CabinPlan.Spots(new Spot(0, 0), rings: 0));
        Assert.Empty(CabinPlan.Spots(new Spot(0, 0), rings: -1).Skip(1));
    }
}

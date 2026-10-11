using ManyFarms.Core;
using Xunit;

namespace ManyFarms.Tests;

public sealed class JoinAskTests
{
    [Fact]
    public void ThePickerOpensByItselfOnceEverForAFarmerWithNoFarm()
    {
        Assert.True(JoinAsk.ShouldOpen(askedThisSession: false, askOnJoin: true, settled: true, hasPlots: true, hasFarm: false, askedEver: false));
        // Asked on an earlier join, whether they picked or decided later: not again.
        Assert.False(JoinAsk.ShouldOpen(false, true, true, true, false, askedEver: true));
        // Already this session, already farming somewhere, nothing to pick from, the setting off, or not settled yet.
        Assert.False(JoinAsk.ShouldOpen(true, true, true, true, false, false));
        Assert.False(JoinAsk.ShouldOpen(false, true, true, true, hasFarm: true, false));
        Assert.False(JoinAsk.ShouldOpen(false, true, true, hasPlots: false, false, false));
        Assert.False(JoinAsk.ShouldOpen(false, askOnJoin: false, true, true, false, false));
        Assert.False(JoinAsk.ShouldOpen(false, true, settled: false, true, false, false));
    }
}

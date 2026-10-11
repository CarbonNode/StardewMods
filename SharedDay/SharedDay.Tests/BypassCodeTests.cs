using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

public sealed class BypassCodeTests
{
    [Fact]
    public void TheRightPasswordMatchesAndNothingElseDoes()
    {
        string hash = BypassCode.Hash("squeet", "open sesame");
        Assert.True(BypassCode.IsSet(hash));
        Assert.True(BypassCode.Matches("squeet", hash, "open sesame"));
        Assert.True(BypassCode.Matches("squeet", hash.ToUpperInvariant(), "open sesame"));
        Assert.False(BypassCode.Matches("squeet", hash, "Open Sesame"));
        Assert.False(BypassCode.Matches("squeet", hash, ""));
        Assert.False(BypassCode.Matches("another farm", hash, "open sesame"));
    }

    [Fact]
    public void AFarmWithNoPasswordHasNoBypass()
    {
        Assert.Equal("", BypassCode.Hash("squeet", ""));
        Assert.False(BypassCode.IsSet(""));
        Assert.False(BypassCode.IsSet(null));
        Assert.False(BypassCode.Matches("squeet", "", ""));
        Assert.False(BypassCode.Matches("squeet", null, "anything"));
        Assert.False(BypassCode.Matches("squeet", "not a hash", "anything"));
    }

    [Fact]
    public void TheHashDoesNotContainThePassword()
    {
        Assert.DoesNotContain("sesame", BypassCode.Hash("squeet", "open sesame"));
        Assert.NotEqual(BypassCode.Hash("a", "open sesame"), BypassCode.Hash("b", "open sesame"));
    }
}

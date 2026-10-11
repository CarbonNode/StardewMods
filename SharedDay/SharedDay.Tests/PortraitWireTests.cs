using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>The farmers' faces for the turn tracker: only PNGs, and never more than a report may carry.</summary>
public sealed class PortraitWireTests
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static byte[] Png(int length)
    {
        var bytes = new byte[length];
        Signature.CopyTo(bytes, 0);
        return bytes;
    }

    [Fact]
    public void APngBecomesTheAddressTheTrackerAccepts()
    {
        string? address = PortraitWire.ToAddress(Png(300));
        Assert.NotNull(address);
        Assert.StartsWith("data:image/png;base64,iVBORw0KGgo", address);
        // Whole base64 groups, as the tracker requires.
        Assert.Equal(0, (address!.Length - PortraitWire.Prefix.Length) % 4);
    }

    [Fact]
    public void AnythingThatIsNotAPngOrIsTooLargeIsNotSent()
    {
        Assert.Null(PortraitWire.ToAddress(null));
        Assert.Null(PortraitWire.ToAddress(Array.Empty<byte>()));
        Assert.Null(PortraitWire.ToAddress(Signature));
        Assert.Null(PortraitWire.ToAddress(new byte[300]));
        // About 4.4 KB of PNG is the most one farmer may carry.
        Assert.NotNull(PortraitWire.ToAddress(Png(4400)));
        Assert.Null(PortraitWire.ToAddress(Png(4600)));
    }

    [Fact]
    public void AReportKeepsFacesInOrderUntilItsTotalIsUsedUp()
    {
        string face = PortraitWire.ToAddress(Png(4400))!;
        var all = Enumerable.Repeat<string?>(face, 12).ToList();
        all[2] = null;
        var kept = PortraitWire.Fit(all);
        Assert.Equal(12, kept.Count);
        int fits = PortraitWire.TotalMaxChars / face.Length;
        Assert.True(fits is > 1 and < 11);
        Assert.Equal(fits, kept.Count(k => k is not null));
        Assert.Null(kept[2]);
        // The first farmers keep theirs; the ones past the total go without.
        Assert.NotNull(kept[0]);
        Assert.Null(kept[11]);
        Assert.True(kept.Where(k => k is not null).Sum(k => k!.Length) <= PortraitWire.TotalMaxChars);
    }

    [Fact]
    public void SmallFacesAllFit()
    {
        string face = PortraitWire.ToAddress(Png(600))!;
        var kept = PortraitWire.Fit(Enumerable.Repeat<string?>(face, 32).ToList());
        Assert.All(kept, k => Assert.NotNull(k));
    }
}

using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

public class HostSeatKeyTests
{
    [Fact]
    public void A_stray_press_on_an_empty_seat_is_ignored() =>
        Assert.Equal(HostSeatPress.Ignore, HostSeatKey.Decide(seated: false, isSeatKey: false));

    [Fact]
    public void The_seat_key_sits_somebody_down() =>
        Assert.Equal(HostSeatPress.Sit, HostSeatKey.Decide(seated: false, isSeatKey: true));

    [Fact]
    public void Playing_keeps_the_seat() =>
        Assert.Equal(HostSeatPress.Stay, HostSeatKey.Decide(seated: true, isSeatKey: false));

    [Fact]
    public void The_seat_key_again_gets_up() =>
        Assert.Equal(HostSeatPress.Leave, HostSeatKey.Decide(seated: true, isSeatKey: true));
}

using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

public sealed class HostSeatTests
{
    private static FarmerSnapshot Host(FarmerStatus status, bool online, bool away = false) => new(1, "Pat", status, online, online, 0, away);

    [Fact]
    public void AnEmptySeatStaysOutOfTheTurns()
    {
        Assert.Equal(HostSeatAction.None, HostSeat.Decide(false, present: false, dayOpen: true, farmer: null));
    }

    [Fact]
    public void SomebodySittingDownBecomesAPlayerForTheDay()
    {
        Assert.Equal(HostSeatAction.AddAndJoin, HostSeat.Decide(false, true, true, null));
        // After a wake in the middle of a day they had started: straight back in.
        Assert.Equal(HostSeatAction.Join, HostSeat.Decide(false, true, true, Host(FarmerStatus.Pending, online: false)));
        Assert.Equal(HostSeatAction.None, HostSeat.Decide(false, true, true, Host(FarmerStatus.Pending, online: true)));
    }

    [Fact]
    public void WalkingAwayUnfinishedStopsTheFarmWaitingAndComingBackResumes()
    {
        Assert.Equal(HostSeatAction.StepOut, HostSeat.Decide(false, false, true, Host(FarmerStatus.Pending, online: true)));
        Assert.Equal(HostSeatAction.None, HostSeat.Decide(false, false, true, Host(FarmerStatus.Skipped, online: false, away: true)));
        Assert.Equal(HostSeatAction.BringBack, HostSeat.Decide(false, true, true, Host(FarmerStatus.Skipped, online: false, away: true)));
    }

    [Fact]
    public void AFinishedTurnAndADeliberateSkipAreLeftAlone()
    {
        Assert.Equal(HostSeatAction.None, HostSeat.Decide(false, false, true, Host(FarmerStatus.Completed, online: true)));
        Assert.Equal(HostSeatAction.None, HostSeat.Decide(false, true, true, Host(FarmerStatus.Completed, online: true)));
        // The others voted this farmer off the day: sitting back down does not undo their vote.
        Assert.Equal(HostSeatAction.None, HostSeat.Decide(false, true, true, Host(FarmerStatus.Skipped, online: false)));
    }

    [Fact]
    public void NothingChangesAtNightOrOnAPcThatIsAlwaysAPlayers()
    {
        Assert.Equal(HostSeatAction.None, HostSeat.Decide(false, true, dayOpen: false, null));
        Assert.Equal(HostSeatAction.None, HostSeat.Decide(includeHost: true, false, true, Host(FarmerStatus.Pending, online: true)));
        Assert.Equal(HostSeatAction.None, HostSeat.Decide(includeHost: true, true, true, null));
    }

    [Fact]
    public void AHostPcThatOnlyHostsIsNotOneOfTheFarmsPlayers()
    {
        // What another mod's list of a farm's people is told (Many Farms hid "HostPC" on this).
        Assert.False(HostSeat.Plays(includeHost: false, present: false, onTodaysRoster: false));
    }

    [Fact]
    public void AHostWhoReallyPlaysIsOneOfThem()
    {
        Assert.True(HostSeat.Plays(includeHost: true, present: false, onTodaysRoster: false));
        // Somebody is sitting at the host PC now.
        Assert.True(HostSeat.Plays(false, present: true, onTodaysRoster: false));
        // They sat there earlier today and got up: still today's player, not a face that comes and goes.
        Assert.True(HostSeat.Plays(false, present: false, onTodaysRoster: true));
    }
}

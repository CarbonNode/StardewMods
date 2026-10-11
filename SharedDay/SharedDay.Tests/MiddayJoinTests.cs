using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>
/// What a player is told before they join a farm part way through someone's turn, and the
/// arithmetic under it: a game day runs 6:00 AM to 2:00 AM, 20 hours, and the game writes the
/// hours past midnight as 24, 25 and 26.
/// </summary>
public sealed class MiddayJoinTests
{
    private static readonly TimeSpan Now = TimeSpan.FromSeconds(20);

    private static HubFarmer Farmer(string name, string status, bool online, bool inTurn, string? player = null)
        => new(name, status, online, player) { InTurn = inTurn };

    private static HubFarm Farm(int? time, params HubFarmer[] farmers)
        => new("Squeet Farm", "Spring 12", 1, "Relay", "Open", null, true, true, Now, farmers) { Time = time };

    // ---- the clock ----

    [Theory]
    [InlineData(600, 0)]
    [InlineData(610, 10)]
    [InlineData(1410, 490)]
    [InlineData(2350, 1070)]
    [InlineData(2400, 1080)]
    [InlineData(2530, 1170)]
    [InlineData(2600, 1200)]
    public void MinutesGoneCountFromSixInTheMorning(int hhmm, int minutes) => Assert.Equal(minutes, DayClock.MinutesGone(hhmm));

    [Theory]
    [InlineData(0)]
    [InlineData(550)]
    [InlineData(-100)]
    public void BeforeTheDayOpensNothingHasGone(int hhmm) => Assert.Equal(0, DayClock.MinutesGone(hhmm));

    [Theory]
    [InlineData(2610)]
    [InlineData(2700)]
    [InlineData(9999)]
    public void PastTwoInTheMorningTheWholeDayHasGoneAndNoMore(int hhmm) => Assert.Equal(DayClock.MinutesInDay, DayClock.MinutesGone(hhmm));

    [Fact]
    public void AGameDayIsTwentyHours()
    {
        Assert.Equal(20 * 60, DayClock.MinutesInDay);
        Assert.Equal(DayClock.MinutesInDay, DayClock.MinutesGone(DayClock.Closes) - DayClock.MinutesGone(DayClock.Opens));
    }

    [Theory]
    [InlineData(600, "6:00 AM")]
    [InlineData(1150, "11:50 AM")]
    [InlineData(1200, "12:00 PM")]
    [InlineData(1410, "2:10 PM")]
    [InlineData(2350, "11:50 PM")]
    [InlineData(2400, "12:00 AM")]
    [InlineData(2530, "1:30 AM")]
    [InlineData(2600, "2:00 AM")]
    public void TheClockIsWordedAsAPlayerReadsIt(int hhmm, string words) => Assert.Equal(words, DayClock.Words(hhmm));

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(40, "40 min")]
    [InlineData(60, "1 h")]
    [InlineData(490, "8 h 10 min")]
    [InlineData(1200, "20 h")]
    [InlineData(-5, "0 min")]
    public void ASpanIsHoursAndMinutes(int minutes, string words) => Assert.Equal(words, DayClock.Span(minutes));

    [Theory]
    [InlineData(600, true)]
    [InlineData(2600, true)]
    [InlineData(2559, true)]
    [InlineData(590, false)]
    [InlineData(2610, false)]
    [InlineData(1260, false)]
    [InlineData(1299, false)]
    public void OnlyARealGameClockIsBelieved(int hhmm, bool valid) => Assert.Equal(valid, DayClock.IsValid(hhmm));

    // ---- before the join ----

    [Fact]
    public void JoiningWhileSomeoneIsMidTurnIsWarnedAboutWithTheRealNumbers()
    {
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat"));
        var advice = MiddayJoin.Before(farm, "Pat", false, Now);
        Assert.Equal(JoinOutlook.Warn, advice.Kind);
        Assert.Equal("Alice is playing now and it is about 2:10 PM on the farm. Join now and your day starts there: about 8 h 10 min of today's 20 hours are already gone. Wait until Alice has finished and you start at 6:00 AM.", advice.Text);
    }

    [Fact]
    public void ThePlayersNameIsMatchedWhateverItsCase()
        => Assert.Equal(JoinOutlook.Warn, MiddayJoin.Before(Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat")), " pat ", false, Now).Kind);

    [Fact]
    public void PastMidnightTheHoursStillAddUp()
    {
        var farm = Farm(2530, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat"));
        Assert.Contains("it is about 1:30 AM on the farm. Join now and your day starts there: about 19 h 30 min of today's 20 hours", MiddayJoin.Before(farm, "Pat", false, Now).Text);
    }

    [Fact]
    public void AnOldReportIsNeverShownAsTheTimeNow()
    {
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat"));
        var advice = MiddayJoin.Before(farm, "Pat", false, TimeSpan.FromMinutes(4));
        Assert.Equal(JoinOutlook.Warn, advice.Kind);
        Assert.Equal("Alice is playing now. The farm last said it was 2:10 PM, 4 min ago, and it is later than that now. Join now and your day starts at the farm's time, not at 6:00 AM: at least 8 h 10 min of today's 20 hours are already gone. Wait until Alice has finished and you start at 6:00 AM.", advice.Text);
        Assert.DoesNotContain("about", advice.Text);
    }

    [Fact]
    public void AReportOfUnknownAgeIsTreatedAsOld()
        => Assert.Contains("a while ago", MiddayJoin.Before(Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat")), "Pat", false, null).Text);

    [Fact]
    public void AStaleReportOrAnOfflineHostSaysNothing()
    {
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat"));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(farm, "Pat", false, TimeSpan.FromMinutes(13)));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(farm with { HostOnline = false }, "Pat", false, Now));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(farm with { Reported = false }, "Pat", false, Now));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(null, "Pat", false, Now));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(farm, "Pat", false, TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void TogetherModeIsNeverWarnedAbout()
        => Assert.Equal(JoinAdvice.None, MiddayJoin.Before(Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat")) with { Mode = "Together" }, "Pat", false, Now));

    [Fact]
    public void TheFarmsOwnHostIsNeverWarned()
        => Assert.Equal(JoinAdvice.None, MiddayJoin.Before(Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat")), "Pat", true, Now));

    [Fact]
    public void APausedFarmOrANightBeingSavedIsNotATurnToJoin()
    {
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat"));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(farm with { Paused = "Shared Day has paused the farm." }, "Pat", false, Now));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(farm with { Phase = "Advancing" }, "Pat", false, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(600)]
    [InlineData(1275)]
    [InlineData(2700)]
    public void NoClockOrNoTimeGoneSaysNothing(int? time)
        => Assert.Equal(JoinAdvice.None, MiddayJoin.Before(Farm(time, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat")), "Pat", false, Now));

    [Fact]
    public void WhenNobodyInTheTurnIsUnfinishedTheJoinerGetsAFreshMorning()
    {
        // Alice finished her turn and stayed to watch: the next farmer starts a turn of their own.
        var farm = Farm(1410, Farmer("Alice", "Completed", true, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat"));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(farm, "Pat", false, Now));
        // Nobody has played yet.
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(Farm(900, Farmer("Bob", "Pending", false, false, "Pat")), "Pat", false, Now));
    }

    [Fact]
    public void AFarmerWhoLeftUnfinishedHoldsNobodyElsesClock()
    {
        // 0.1.28 warned here: the leaver's turn kept the clock for everyone. Since 0.1.29 the leaver
        // keeps a clock of their own (DayCoordinator.Join: ResumeOwnClock), so a joiner gets a
        // morning of their own and there is nothing to warn about.
        var farm = Farm(1410, Farmer("Alice", "Pending", false, true, "Ali"), Farmer("Bob", "Pending", false, false, "Pat"));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(farm, "Pat", false, TimeSpan.FromMinutes(9)));
    }

    [Fact]
    public void EveryUnfinishedFarmerOfTheTurnHasToFinishBeforeAFreshMorning()
    {
        // Cleo left the turn unfinished: she keeps her own clock and is not waited for (0.1.29).
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Cleo", "Pending", false, true, "Cee"), Farmer("Bob", "Pending", false, false, "Pat"));
        var advice = MiddayJoin.Before(farm, "Pat", false, Now);
        Assert.StartsWith("Alice is playing now and it is about 2:10 PM", advice.Text);
        Assert.EndsWith("Wait until Alice has finished and you start at 6:00 AM.", advice.Text);
    }

    [Fact]
    public void TwoPlayingAreNamedTogether()
    {
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Cleo", "Pending", true, true, "Cee"), Farmer("Bob", "Pending", false, false, "Pat"));
        Assert.StartsWith("Alice and Cleo are playing now and it is about 2:10 PM on the farm.", MiddayJoin.Before(farm, "Pat", false, Now).Text);
    }

    [Fact]
    public void APlayerWithAnUnfinishedTurnIsToldTheyResume()
    {
        // Somebody else is playing: the farm will ask them, on the farm, whether to join that turn
        // or wait for it; the tracker does not know their own parked clock, so no time is named.
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Pending", false, true, "Pat"));
        var advice = MiddayJoin.Before(farm, "Pat", false, Now);
        Assert.Equal(JoinOutlook.Resume, advice.Kind);
        Assert.Equal("Your turn is still open. Alice is playing now: the farm asks whether you join that turn at its clock or wait and carry on where you left yours.", advice.Text);
        // Nobody playing: they carry on where they left it, whatever the report's age.
        var alone = Farm(1410, Farmer("Bob", "Pending", false, true, "Pat"));
        Assert.Equal("Your turn is still open. Join and you carry on where you left it.", MiddayJoin.Before(alone, "Pat", false, TimeSpan.FromMinutes(8)).Text);
        Assert.Equal("Your turn is still open. Join and you carry on where you left it.",
            MiddayJoin.Before(Farm(1410, Farmer("Alice", "Completed", false, true, "Ali"), Farmer("Bob", "Pending", false, true, "Pat")), "Pat", false, TimeSpan.FromMinutes(3)).Text);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Skipped")]
    public void APlayerWhoHasPlayedOrSitsOutLosesNothingAndIsNotWarned(string status)
        => Assert.Equal(JoinAdvice.None, MiddayJoin.Before(Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", status, false, false, "Pat")), "Pat", false, Now));

    [Fact]
    public void AFarmerTheFarmStoppedWaitingForIsWarnedBecauseJoiningPutsThemBackIntoToday()
    {
        // Dana is Away (reported as Skipped with the away mark). Joining turns her Pending and
        // puts her into Alice's turn at its clock (DayCoordinator.Join), so she is warned first.
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Dana", "Skipped", false, false, "Pat") with { Away = true });
        var advice = MiddayJoin.Before(farm, "Pat", false, Now);
        Assert.Equal(JoinOutlook.Warn, advice.Kind);
        Assert.Equal("Alice is playing now and it is about 2:10 PM on the farm. Join now and your day starts there: about 8 h 10 min of today's 20 hours are already gone. Wait until Alice has finished and you start at 6:00 AM.", advice.Text);
        // Skipped on purpose (no away mark) still only watches.
        var skipped = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Dana", "Skipped", false, false, "Pat"));
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(skipped, "Pat", false, Now));
    }

    [Fact]
    public void TheAwayMarkIsReadFromTheReport()
    {
        var farm = HubFarm.Parse("""{"tracker_id":"t","host_online":true,"snapshot":{"mode":"Relay","phase":"Open","time":1410,"farmers":[{"id":"1","name":"Dana","status":"Skipped","online":false,"inCurrentTurn":false,"away":true},{"id":"2","name":"Bob","status":"Skipped","online":false}]}}""");
        Assert.NotNull(farm);
        Assert.True(farm!.Farmers[0].Away);
        Assert.True(farm.Farmers[0].StillToPlay);
        Assert.False(farm.Farmers[1].Away);
        Assert.False(farm.Farmers[1].StillToPlay);
    }

    [Fact]
    public void APlayerWithNoFarmerYetIsWarned()
    {
        // Every farmer belongs to somebody else, so this player is about to make one.
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Cleo", "Completed", false, false, "Cee"));
        var advice = MiddayJoin.Before(farm, "Pat", false, Now);
        Assert.Equal(JoinOutlook.Warn, advice.Kind);
        Assert.Contains("Join now and your day starts there", advice.Text);
    }

    [Fact]
    public void APlayerTheTrackerCannotPlaceIsWarnedWithAnIf()
    {
        // Bob is nobody's on the tracker and has played: this player may be Bob, and then loses nothing.
        var farm = Farm(1410, Farmer("Alice", "Pending", true, true, "Ali"), Farmer("Bob", "Completed", false, false));
        var advice = MiddayJoin.Before(farm, "Pat", false, Now);
        Assert.Equal(JoinOutlook.Warn, advice.Kind);
        Assert.Equal("Alice is playing now and it is about 2:10 PM on the farm. If you have not played today, joining now starts your day there: about 8 h 10 min of today's 20 hours are already gone. Wait until Alice has finished and you start at 6:00 AM.", advice.Text);
        // With no launcher name at all, the same.
        Assert.Contains("If you have not played today", MiddayJoin.Before(farm, null, false, Now).Text);
        // An unclaimed farmer who is in the farm right now is somebody else.
        var others = Farm(1410, Farmer("Alice", "Pending", true, true), Farmer("Bob", "Pending", false, false));
        Assert.Contains("Join now and your day starts there", MiddayJoin.Before(others, null, false, Now).Text);
    }

    [Fact]
    public void TheTrackersOwnAnswerCarriesTheClockAndWhoIsInTheTurn()
    {
        var farm = HubFarm.Parse("""
            { "tracker_id": "abc", "host_online": true, "updated_at": "2026-10-07T15:00:00Z", "server_time": "2026-10-07T15:00:20Z",
              "snapshot": { "mode": "Relay", "phase": "Open", "turn": 1, "time": 1410, "furthestTime": 1410, "paused": null,
                "farmers": [
                  { "id": "1", "name": "Alice", "status": "Pending", "online": true, "inCurrentTurn": true },
                  { "id": "2", "name": "Bob", "status": "Pending", "online": false, "inCurrentTurn": false } ] },
              "members": [ { "display_name": "Pat", "farmer_id": "2" }, { "display_name": "Ali", "farmer_id": "1" } ] }
            """)!;
        Assert.Equal(1410, farm.Time);
        Assert.True(farm.Farmers[0].InTurn);
        Assert.False(farm.Farmers[1].InTurn);
        Assert.Equal(JoinOutlook.Warn, MiddayJoin.Before(farm, "Pat", false, farm.Age).Kind);
        // Ali is the one playing: her own turn, nothing to warn about.
        Assert.Equal(JoinOutlook.Resume, MiddayJoin.Before(farm, "Ali", false, farm.Age).Kind);
    }

    [Fact]
    public void AReportWithoutAClockParsesAndSaysNothing()
    {
        var farm = HubFarm.Parse("""{ "tracker_id": "abc", "host_online": true, "snapshot": { "mode": "Relay", "phase": "Open", "farmers": [ { "name": "Alice", "status": "Pending", "online": true } ] } }""")!;
        Assert.Null(farm.Time);
        Assert.False(farm.Farmers[0].InTurn);
        Assert.Equal(JoinAdvice.None, MiddayJoin.Before(farm, null, false, Now));
    }

    [Fact]
    public void TheTwoAnswersPutWaitingFirst()
    {
        Assert.Equal("Wait", JoinAdvice.WaitAnswer);
        Assert.Equal("Join now", JoinAdvice.JoinAnswer);
    }

    // ---- after the join ----

    [Fact]
    public void AfterAMidDayJoinOneLineSaysWhenAndThatTheDayEndsWithEveryones()
    {
        Assert.Equal("You joined at 2:10 PM: 8 h 10 min of today's 20 hours had already gone. The clock is shared, so your day ends at 2:00 AM with everyone else's.",
            MiddayJoin.After(JoinDisposition.JoinCurrentSession, false, PlayMode.Relay, 1410));
        // Into a turn its farmer left unfinished: the same loss.
        Assert.StartsWith("You joined at 12:30 AM: 18 h 30 min", MiddayJoin.After(JoinDisposition.ResumeSession, false, PlayMode.Relay, 2430));
    }

    [Fact]
    public void ComingBackToYourOwnTurnSaysSo()
        => Assert.Equal("You are back in your turn. It is 2:10 PM, where the turn had got to.", MiddayJoin.After(JoinDisposition.ResumeSession, true, PlayMode.Relay, 1410));

    [Theory]
    [InlineData(JoinDisposition.StartSession, PlayMode.Relay, 600)]
    [InlineData(JoinDisposition.StartSession, PlayMode.Relay, 1410)]
    [InlineData(JoinDisposition.Observer, PlayMode.Relay, 1410)]
    [InlineData(JoinDisposition.AlreadyPresent, PlayMode.Relay, 1410)]
    [InlineData(JoinDisposition.JoinCurrentSession, PlayMode.Together, 1410)]
    [InlineData(JoinDisposition.JoinCurrentSession, PlayMode.Relay, 600)]
    [InlineData(JoinDisposition.JoinCurrentSession, PlayMode.Relay, 1275)]
    public void AFreshMorningAWatcherOrTogetherModeSaysNothing(JoinDisposition disposition, PlayMode mode, int time)
        => Assert.Null(MiddayJoin.After(disposition, false, mode, time));

    // ---- the coordinator's own rule, which the words above describe ----

    [Fact]
    public void TheCoordinatorGivesAFreshMorningOnceNobodyIsPlayingAndKeepsALeaversClockForThem()
    {
        var day = DayCoordinator.Create("farm", 11, new[] { new FarmerIdentity(1, "Alice"), new FarmerIdentity(2, "Bob"), new FarmerIdentity(3, "Cleo") });
        Assert.Equal(JoinDisposition.StartSession, day.Join(1).Disposition);
        // Bob joins while Alice plays: her clock.
        Assert.Equal(JoinDisposition.JoinCurrentSession, day.Join(2).Disposition);
        day.Disconnect(2, 1300);
        day.Disconnect(1, 1410);
        // Both left unfinished: Cleo is not held to their clock, she gets a morning of her own (0.1.29).
        Assert.Equal(JoinDisposition.StartSession, day.Join(3).Disposition);
        day.Disconnect(3, 900);
        // Alice comes back alone: her own clock, where she left it.
        var alice = day.Join(1);
        Assert.Equal(JoinDisposition.ResumeOwnClock, alice.Disposition);
        Assert.Equal(1410, day.Snapshot.Farmers.Single(f => f.Id == 1).TimeReached);
        // Bob comes back while Alice plays: he is asked, join her turn or wait for his own clock.
        Assert.Equal(JoinDisposition.ChooseJoinOrWait, day.Join(2).Disposition);
    }

    [Fact]
    public void AfterComingBackOntoYourOwnClockTheChatSaysSo()
        => Assert.Equal("You are back in your turn. It is 2:10 PM, where you left it.", MiddayJoin.After(JoinDisposition.ResumeOwnClock, false, PlayMode.Relay, 1410));

    [Fact]
    public void TheCoordinatorStartsTheNextTurnAtMorningWhenTheLastOneIsFinished()
    {
        var day = DayCoordinator.Create("farm", 11, new[] { new FarmerIdentity(1, "Alice"), new FarmerIdentity(2, "Bob") });
        day.Join(1);
        day.Complete(1);
        // Alice stays to watch; Bob still starts a turn of his own.
        var bob = day.Join(2);
        Assert.True(bob.StartsNewSession);
        Assert.Equal(2, bob.SessionNumber);
    }

    // ---- how often the host reports ----

    [Fact]
    public void AChangeIsReportedAtOnceButNeverTwiceInFiveSeconds()
    {
        Assert.False(TrackerCadence.Due(true, TimeSpan.FromSeconds(4), true, 700, 600));
        Assert.True(TrackerCadence.Due(true, TimeSpan.FromSeconds(5), false, 600, 600));
    }

    [Fact]
    public void WithNothingChangedTheHeartbeatIsFiveMinutes()
    {
        Assert.False(TrackerCadence.Due(false, TimeSpan.FromSeconds(299), false, 900, 600));
        Assert.True(TrackerCadence.Due(false, TimeSpan.FromMinutes(5), false, 600, 600));
    }

    [Fact]
    public void WhileATurnIsPlayedTheClockIsReportedEveryHalfHourOfGameTime()
    {
        Assert.False(TrackerCadence.Due(false, TimeSpan.FromSeconds(20), true, 620, 600));
        Assert.True(TrackerCadence.Due(false, TimeSpan.FromSeconds(21), true, 630, 600));
        // A new turn rewinds the clock to morning: that is a move too.
        Assert.True(TrackerCadence.Due(false, TimeSpan.FromSeconds(21), true, 0, 480));
        // Nobody playing: the clock stands still, and the heartbeat is enough.
        Assert.False(TrackerCadence.Due(false, TimeSpan.FromSeconds(60), false, 700, 600));
    }
}

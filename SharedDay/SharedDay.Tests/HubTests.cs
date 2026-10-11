using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>
/// The title-screen hub's knowledge of the farm: the launcher's link file, the player's own
/// config over it, and the tracker's answer reduced to what the hub shows.
/// </summary>
public sealed class HubTests
{
    private const string LinkFile = """
        {
          "schema": "modrelay.shared-day-link/1",
          "writtenUtc": "2026-10-05T21:00:00Z",
          "pack": { "id": "squeetvalley-turns", "name": "Squeet Valley Turns" },
          "player": "Pat",
          "group": { "id": "K757l13HI8uiuyG_", "name": "Squeet Farm" },
          "farm": { "trackerUrl": "https://modrelay.app/turns/Zm9yLXRlc3Rz", "pageUrl": "https://modrelay.app/turns/Zm9yLXRlc3Rz/page", "label": "Sunny" },
          "host": { "name": "HostPC", "state": "awake", "joinAddress": "192.0.2.10", "joinCode": null }
        }
        """;

    [Fact]
    public void TheLaunchersLinkFileNamesTheGroupTheFarmAndHowToJoin()
    {
        var link = HubLink.ParseFile(LinkFile)!;
        Assert.Equal("Sunny", link.Label);
        Assert.Equal("Squeet Farm", link.GroupName);
        Assert.Equal("Pat", link.Player);
        Assert.Equal("https://modrelay.app/turns/Zm9yLXRlc3Rz", link.TrackerUrl);
        Assert.Equal("192.0.2.10", link.JoinAddress);
        Assert.Null(link.InviteCode);
        Assert.Equal("HostPC", link.HostName);
        Assert.Equal("awake", link.HostState);
        Assert.True(link.CanJoin && link.HasFarm && link.Any);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "schema": "something-else/1", "group": { "name": "X" } }""")]
    public void AFileThatIsNotALinkIsIgnored(string json) => Assert.Null(HubLink.ParseFile(json));

    [Fact]
    public void APlayerInNoGroupHasNothingToShow()
    {
        var link = HubLink.ParseFile("""{ "schema": "modrelay.shared-day-link/1", "player": "Pat", "group": null, "farm": null, "host": null }""")!;
        Assert.False(link.Any);
        Assert.Null(link.Name);
    }

    [Fact]
    public void AHostAsleepLeavesTheGroupAndFarmButNoWayIn()
    {
        var link = HubLink.ParseFile(LinkFile.Replace("\"192.0.2.10\"", "null").Replace("\"awake\"", "\"asleep\""))!;
        Assert.False(link.CanJoin);
        Assert.True(link.HasFarm);
        Assert.Equal("asleep", link.HostState);
    }

    [Fact]
    public void OnlyWebAddressesAreEverFetchedOrOpened()
    {
        var link = HubLink.ParseFile(LinkFile.Replace("https://modrelay.app/turns/Zm9yLXRlc3Rz/page", "file:///C:/Windows/system.ini")
            .Replace("\"https://modrelay.app/turns/Zm9yLXRlc3Rz\"", "\"javascript:alert(1)\""))!;
        Assert.Null(link.TrackerUrl);
        Assert.Null(link.PageUrl);
        Assert.Null(HubLink.Merge(null, null, null, null, "ftp://example.com/turns/x").TrackerUrl);
    }

    [Fact]
    public void WhatAPersonTypedIntoConfigWinsOverTheLauncher()
    {
        var file = HubLink.ParseFile(LinkFile)!;
        var merged = HubLink.Merge(file, " Meadow ", null, "ABCDE-FGHJK", "https://example.org/turns/abc/");
        Assert.Equal("Meadow", merged.Label);
        // A typed join target replaces the launcher's as a pair.
        Assert.Null(merged.JoinAddress);
        Assert.Equal("ABCDE-FGHJK", merged.InviteCode);
        Assert.Equal("https://example.org/turns/abc/", merged.TrackerUrl);
        Assert.Equal("https://example.org/turns/abc/page", merged.PageUrl);
        Assert.Equal("Squeet Farm", merged.GroupName);
    }

    [Fact]
    public void BlankConfigLeavesTheLaunchersLinkAsItIs()
        => Assert.Equal(HubLink.ParseFile(LinkFile), HubLink.Merge(HubLink.ParseFile(LinkFile), "", "  ", null, ""));

    [Fact]
    public void ConfigAloneIsEnoughWithoutTheLauncher()
    {
        var link = HubLink.Merge(null, "Sunny", "10.0.0.5:24642", "", "https://modrelay.app/turns/abc");
        Assert.Equal("Sunny", link.Name);
        Assert.True(link.CanJoin && link.HasFarm);
        Assert.Equal("https://modrelay.app/turns/abc/page", link.PageUrl);
        Assert.False(HubLink.Merge(null, "", "", "", "").Any);
    }

    private const string Tracker = """
        {
          "tracker_id": "Zm9yLXRlc3Rz", "pack_id": "squeetvalley-turns", "label": "Sunny", "listed": false,
          "created_at": "2026-10-04T15:00:00.000Z", "updated_at": "2026-10-05T15:00:00.000Z", "server_time": "2026-10-05T15:04:30.000Z",
          "stale_after_ms": 720000, "host_online": true, "farm_id": "385772993", "group_id": "K757l13HI8uiuyG_",
          "snapshot": {
            "schema": "shared-day.turns/1", "sequence": 12, "sentUtc": "2026-10-05T15:00:00Z", "modVersion": "0.1.11",
            "farm": { "id": "385772993", "name": "Sunny Meadow" },
            "date": { "season": "spring", "day": 13, "year": 2, "absoluteDay": 124, "label": "Spring 13" },
            "mode": "Relay", "phase": "Open", "turn": 2, "time": 1230, "furthestTime": 2200, "paused": null,
            "farmers": [
              { "id": "-6154521813318541371", "name": "Alice", "status": "Completed", "online": false, "inCurrentTurn": false },
              { "id": "2283117480133497730", "name": "Bob", "status": "Pending", "online": true, "inCurrentTurn": true },
              { "id": "9223372036854775807", "name": "Cleo", "status": "Skipped", "online": false, "inCurrentTurn": false },
              { "id": "-42", "name": "Émilie 🌸 李", "status": "Pending", "online": false, "inCurrentTurn": false }
            ]
          },
          "members": [
            { "account_id": "a1", "display_name": "Ali", "avatar_url": null, "farmer_id": "-6154521813318541371", "joined_at": "x" },
            { "account_id": "a2", "display_name": "Pat", "avatar_url": null, "farmer_id": "2283117480133497730", "joined_at": "x" },
            { "account_id": "a3", "display_name": "Watcher", "avatar_url": null, "farmer_id": null, "joined_at": "x" }
          ]
        }
        """;

    [Fact]
    public void TheTrackersAnswerBecomesTheFarmTheHubShows()
    {
        var farm = HubFarm.Parse(Tracker)!;
        Assert.Equal("Sunny Meadow", farm.FarmName);
        Assert.Equal("Spring 13", farm.DateLabel);
        Assert.Equal(2, farm.Year);
        Assert.True(farm.HostOnline && farm.Reported);
        Assert.Equal(TimeSpan.FromSeconds(270), farm.Age);
        Assert.Equal("Waiting on Bob and Émilie 🌸 李", farm.Headline);
        Assert.Equal("Spring 13, waiting on Bob and Émilie 🌸 李", farm.Summary);
        Assert.Equal("The farm is up", farm.HostLine);
        // Still to play first, then played, then sitting out; 64-bit farmer IDs matched as text.
        Assert.Equal(new[] { "Bob", "Émilie 🌸 李", "Alice", "Cleo" }, farm.Sorted.Select(f => f.Name));
        Assert.Equal(new[] { "Pat", null, "Ali", null }, farm.Sorted.Select(f => f.Player));
        Assert.True(farm.Sorted[0].Online);
    }

    [Theory]
    [InlineData("\"paused\": null", "\"paused\": \"Bob's game closed mid-turn.\"", "Paused", "Spring 13, paused")]
    [InlineData("\"phase\": \"Open\"", "\"phase\": \"Advancing\"", "Saving overnight, the next day is coming", "Spring 13, saving the night")]
    [InlineData("\"mode\": \"Relay\"", "\"mode\": \"Together\"", "Playing together, waiting on Bob and Émilie 🌸 李", "Spring 13, waiting on Bob and Émilie 🌸 李")]
    [InlineData("\"status\": \"Pending\"", "\"status\": \"Completed\"", "Everyone has played, the day ends when the farm saves", "Spring 13, everyone has played")]
    public void TheHeadlineSaysWhatTheFarmIsWaitingFor(string from, string to, string headline, string summary)
    {
        var farm = HubFarm.Parse(Tracker.Replace(from, to))!;
        Assert.Equal(headline, farm.Headline);
        Assert.Equal(summary, farm.Summary);
    }

    [Fact]
    public void AFarmThatStoppedReportingSaysWhenItLastDid()
    {
        var farm = HubFarm.Parse(Tracker.Replace("\"host_online\": true", "\"host_online\": false").Replace("2026-10-05T15:04:30.000Z", "2026-10-05T18:04:30.000Z"))!;
        Assert.False(farm.HostOnline);
        Assert.Equal("The farm last reported 3 h ago", farm.HostLine);
    }

    [Fact]
    public void ATrackerWithNoReportYetSaysSo()
    {
        var farm = HubFarm.Parse("""{ "tracker_id": "abc", "label": "Sunny", "host_online": false, "updated_at": null, "server_time": "2026-10-05T15:00:00Z", "snapshot": null }""")!;
        Assert.False(farm.Reported);
        Assert.Equal("Sunny", farm.FarmName);
        Assert.Equal("No report from the farm yet", farm.Headline);
        Assert.Equal("No report yet", farm.Summary);
        Assert.Equal("The farm has not reported yet", farm.HostLine);
        Assert.Empty(farm.Farmers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>502</html>")]
    [InlineData("""{ "code": "not_found", "message": "No such tracker" }""")]
    public void AnAnswerThatIsNotATrackerIsNoFarm(string body) => Assert.Null(HubFarm.Parse(body));

    [Fact]
    public void AStatusFromANewerSharedDayIsShownAsItIs()
    {
        var farm = HubFarm.Parse(Tracker.Replace("\"Skipped\"", "\"Travelling\""))!;
        Assert.Equal("Travelling", HubFarm.StatusWords(farm.Sorted[^1].Status));
        Assert.Equal("Still to play", HubFarm.StatusWords("Pending"));
    }

    [Fact]
    public void LongListsOfNamesAreCutWithACountAsTheLauncherCutsThem()
    {
        Assert.Equal("A, B and 4 more", HubFarm.JoinNames(new[] { "A", "B", "C", "D", "E", "F" }));
        Assert.Equal("A, B and 2 more", HubFarm.JoinNames(new[] { "A", "B", "C", "D" }));
        Assert.Equal("A, B and C", HubFarm.JoinNames(new[] { "A", "B", "C" }));
        Assert.Equal("A and B", HubFarm.JoinNames(new[] { "A", "B" }));
        Assert.Equal("A and 2 more", HubFarm.JoinNames(new[] { "A", "B", "C" }, 2));
    }

    [Theory]
    [InlineData("\"year\": 2,", "\"year\": null,")]
    [InlineData("\"year\": 2,", "\"year\": \"two\",")]
    [InlineData("\"year\": 2,", "\"year\": 2.5,")]
    [InlineData("\"label\": \"Spring 13\" }", "\"label\": null }")]
    public void ADateTheServerCouldNotFillInStillShowsTheFarm(string from, string to)
    {
        Assert.Contains(from, Tracker);
        var farm = HubFarm.Parse(Tracker.Replace(from, to))!;
        Assert.True(farm.Reported);
        Assert.Equal(4, farm.Farmers.Count);
        Assert.Equal("Waiting on Bob and Émilie 🌸 李", farm.Headline);
    }

    [Fact]
    public void AFarmerWithNoNameYetStillHasATurn()
    {
        var farm = HubFarm.Parse(Tracker.Replace("\"name\": \"Bob\"", "\"name\": \"\"").Replace("\"name\": \"Émilie 🌸 李\", \"status\": \"Pending\"", "\"name\": \"Émilie\", \"status\": \"Completed\""))!;
        Assert.Equal("Waiting on A farmer", farm.Headline);
        Assert.Equal(4, farm.Farmers.Count);
    }

    [Theory]
    [InlineData("asleep")]
    [InlineData("starting")]
    [InlineData("offline")]
    public void AHostThatIsNotUpIsBelievedOverAnAddressItSharedEarlier(string state)
    {
        var link = HubLink.ParseFile(LinkFile.Replace("\"awake\"", "\"" + state + "\""))!;
        Assert.Equal("192.0.2.10", link.JoinAddress);
        Assert.False(link.CanJoin);
        // A target a person typed into config is theirs to try regardless.
        Assert.True(HubLink.Merge(link, null, "10.0.0.5", null, null).CanJoin);
    }

    [Fact]
    public void AFarmWithNoHostPcIsJoinedByWhateverWayInIsKnown()
        => Assert.True(HubLink.Merge(null, null, "10.0.0.5", null, null).CanJoin);

    [Theory]
    [InlineData("https://host.example/turns/abc?x=1", "https://host.example/turns/abc/page")]
    [InlineData("https://host.example/turns/abc/#top", "https://host.example/turns/abc/page")]
    [InlineData("http://192.0.2.10:8080/turns/abc", "http://192.0.2.10:8080/turns/abc/page")]
    public void TheTurnPageIsThePathPlusPageWhateverElseTheAddressCarries(string tracker, string page)
        => Assert.Equal(page, HubLink.Merge(null, null, null, null, tracker).PageUrl);

    [Fact]
    public void TheLinkFilesOwnPageAddressIsKept()
        => Assert.Equal("https://modrelay.app/turns/Zm9yLXRlc3Rz/page", HubLink.ParseFile(LinkFile)!.PageUrl);

    // Wake the farm from the hub (2026-10-08).

    [Theory]
    [InlineData("https://modrelay.app/turns/abc", "https://modrelay.app/turns/abc/wake")]
    [InlineData("https://host.example/turns/abc/?x=1#top", "https://host.example/turns/abc/wake")]
    [InlineData("http://192.0.2.10:8080/turns/abc", "http://192.0.2.10:8080/turns/abc/wake")]
    public void TheWakeIsPostedToTheTrackersAddressPlusWake(string tracker, string wake) => Assert.Equal(wake, HubLink.WakeFor(tracker));

    private const string GroupField = "\"group_id\": \"K757l13HI8uiuyG_\"";
    private const string HostAsleep = GroupField + ", \"host\": { \"name\": \"HostPC\", \"state\": \"asleep\", \"wake_pending\": false, \"wake_from_farm\": true }";

    [Fact]
    public void TheTrackerTellsTheHubAboutTheHostPc()
    {
        var farm = HubFarm.Parse(Tracker.Replace(GroupField, HostAsleep))!;
        Assert.Equal(new HubHost("HostPC", "asleep", false, true), farm.Host);
        // An older server, a link-only farm or a group with no host: no host, and the hub still shows the farm.
        Assert.Null(HubFarm.Parse(Tracker)!.Host);
        Assert.Null(HubFarm.Parse(Tracker.Replace(GroupField, "\"group_id\": null, \"host\": null"))!.Host);
        // A host with no readable name or state is no host.
        Assert.Null(HubFarm.Parse(Tracker.Replace(GroupField, "\"group_id\": \"x\", \"host\": { \"state\": \"asleep\" }"))!.Host);
        // The host rides along on a tracker with no report yet.
        var fresh = HubFarm.Parse("{ \"tracker_id\": \"abc\", \"label\": \"Sunny\", \"host_online\": false, \"updated_at\": null, \"server_time\": \"2026-10-05T15:00:00Z\", \"snapshot\": null, " + HostAsleep + " }")!;
        Assert.False(fresh.Reported);
        Assert.Equal("asleep", fresh.Host!.State);
    }

    private static HubFarm WithHost(string state, bool wakePending = false, bool wakeFromFarm = true)
        => HubFarm.Parse(Tracker)! with { Host = new HubHost("HostPC", state, wakePending, wakeFromFarm) };

    [Fact]
    public void WakeTheFarmIsLitWhenTheHostSleepsAndModRelayOffersAWakeFromTheGame()
    {
        var link = HubLink.ParseFile(LinkFile.Replace("\"awake\"", "\"asleep\""))!;
        Assert.Equal(new WakeOutlook(WakeOutlook.WakeLabel, true, null), WakeOutlook.For(link, WithHost("asleep"), hostsItself: false, justAsked: false));
        // A host PC that has not checked in lately can still be asked: its loop looks in every few minutes.
        Assert.True(WakeOutlook.For(link, WithHost("offline"), false, false).Enabled);
        // The tracker's word wins over the launcher's link file in either direction.
        Assert.Equal(WakeOutlook.UpLabel, WakeOutlook.For(link, WithHost("awake"), false, false).Label);
        Assert.True(WakeOutlook.For(HubLink.ParseFile(LinkFile)!, WithHost("asleep"), false, false).Enabled);
    }

    [Fact]
    public void WakeTheFarmIsNotLitWhileItIsUpWakingOrAlreadyAsked()
    {
        var link = HubLink.ParseFile(LinkFile.Replace("\"awake\"", "\"asleep\""))!;
        Assert.Equal(new WakeOutlook(WakeOutlook.WakingLabel, false, null), WakeOutlook.For(link, WithHost("starting"), false, false));
        Assert.Equal(WakeOutlook.WakingLabel, WakeOutlook.For(link, WithHost("asleep", wakePending: true), false, false).Label);
        Assert.Equal(WakeOutlook.WakingLabel, WakeOutlook.For(link, WithHost("asleep"), false, justAsked: true).Label);
        Assert.Equal(new WakeOutlook(WakeOutlook.UpLabel, false, null), WakeOutlook.For(HubLink.ParseFile(LinkFile)!, null, false, false));
        // Before the tracker has answered, the link file's "asleep" is not enough to send anything.
        var waiting = WakeOutlook.For(link, null, false, false);
        Assert.False(waiting.Enabled);
        Assert.Equal(WakeOutlook.AskingWords, waiting.Why);
        Assert.Equal(WakeOutlook.AskingWords, WakeOutlook.For(link, HubFarm.Parse(Tracker), false, false).Why);
    }

    [Fact]
    public void WakeTheFarmSaysWhyWhenItCannotBeDoneFromHere()
    {
        var link = HubLink.ParseFile(LinkFile.Replace("\"awake\"", "\"asleep\""))!;
        var noPassword = WakeOutlook.For(link, WithHost("asleep", wakeFromFarm: false), false, false);
        Assert.False(noPassword.Enabled);
        Assert.Equal(WakeOutlook.NoPasswordWords, noPassword.Why);
        Assert.Contains("The group's owner sets one in ModRelay", noPassword.Why);
        var host = WakeOutlook.For(link, WithHost("asleep"), hostsItself: true, justAsked: false);
        Assert.False(host.Enabled);
        Assert.Contains("This PC hosts the farm", host.Why);
        // No farm on a tracker: nothing to wake, and the no-farm line above the buttons says what to do.
        Assert.Equal(new WakeOutlook(WakeOutlook.WakeLabel, false, null), WakeOutlook.For(HubLink.Merge(null, null, null, null, null), null, false, false));
        // A tracker typed into config, with no host PC known anywhere: said plainly.
        Assert.Equal("This farm has no host PC in ModRelay to wake.", WakeOutlook.For(HubLink.Merge(null, null, null, null, "https://modrelay.app/turns/abc"), null, false, false).Why);
    }

    [Theory]
    [InlineData(200, "{ \"ok\": true, \"state\": \"asleep\", \"wake_pending\": true }", WakeResult.Woken, WakeOutlook.WokenWords)]
    [InlineData(200, "{ \"ok\": true, \"state\": \"offline\", \"wake_pending\": true }", WakeResult.Woken, WakeOutlook.WokenWords)]
    [InlineData(200, "{ \"ok\": true, \"state\": \"awake\", \"wake_pending\": false }", WakeResult.AlreadyUp, "The farm is already up.")]
    [InlineData(200, "{ \"ok\": true, \"state\": \"starting\", \"wake_pending\": false }", WakeResult.AlreadyUp, "The farm is already waking up.")]
    [InlineData(403, "{ \"code\": \"wrong_password\", \"message\": \"That is not the wake password\" }", WakeResult.WrongPassword, "That is not the wake password.")]
    [InlineData(403, "{ \"code\": \"password_required\", \"message\": \"This farm asks for its wake password\" }", WakeResult.PasswordRequired, "Type the farm's wake password.")]
    [InlineData(429, "{ \"code\": \"too_many_attempts\" }", WakeResult.Throttled, "Too many wrong passwords. Try again in a few minutes.")]
    [InlineData(409, "{ \"code\": \"no_wake_password\" }", WakeResult.NoPassword, WakeOutlook.NoPasswordWords)]
    [InlineData(404, "{ \"code\": \"not_found\" }", WakeResult.Gone, "ModRelay has no host PC for this farm any more.")]
    [InlineData(502, "<html>bad gateway</html>", WakeResult.Failed, "ModRelay did not answer. Try again in a moment.")]
    [InlineData(500, "", WakeResult.Failed, "ModRelay did not answer. Try again in a moment.")]
    public void ModRelaysAnswerToAWakeIsPutInThePlayersWords(int status, string body, WakeResult kind, string message)
    {
        var answer = HubFarm.ParseWakeAnswer(status, body);
        Assert.Equal(kind, answer.Kind);
        Assert.Equal(message, answer.Message);
        // Only a wrong or missing password keeps the box open for another go.
        Assert.Equal(kind is WakeResult.WrongPassword or WakeResult.PasswordRequired, answer.AskAgain);
    }

    [Fact]
    public void WhileTheFarmWakesTheHubSaysWhatTheHostIsDoingAndHowLongItHasBeen()
    {
        Assert.Equal("Starting the game and loading the farm. 1:42 so far; it usually takes 1 to 3 minutes. Join lights up by itself when it is ready.",
            WakeProgress.Line("Starting the game and loading the farm", TimeSpan.FromSeconds(102)));
        Assert.Equal("Bringing the mods up to the pack. 0:07 so far; it usually takes 1 to 3 minutes. Join lights up by itself when it is ready.",
            WakeProgress.Line("Bringing the mods up to the pack.", TimeSpan.FromSeconds(7)));
        // Nothing said yet, and no clock: the wake is on its way to the host PC.
        Assert.Equal("Waking the host PC. It usually takes 1 to 3 minutes. Join lights up by itself when it is ready.", WakeProgress.Line(null, null));
        Assert.Equal("Waking the host PC. 12:05 so far; it usually takes 1 to 3 minutes. Join lights up by itself when it is ready.", WakeProgress.Line("  ", TimeSpan.FromSeconds(725)));
        Assert.Equal("The farm is waking up, 1:42 so far", WakeProgress.Short(TimeSpan.FromSeconds(102)));
        Assert.Equal("The farm is waking up", WakeProgress.Short(null));
        Assert.Equal("0:00", WakeProgress.Clock(TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void TheHostsNoteRidesOnTheTrackersHost()
    {
        var farm = HubFarm.Parse(Tracker.Replace(GroupField, GroupField + ", \"host\": { \"name\": \"HostPC\", \"state\": \"starting\", \"note\": \"Starting the game and loading the farm\", \"wake_pending\": false, \"wake_from_farm\": true }"))!;
        Assert.Equal("Starting the game and loading the farm", farm.Host!.Note);
        Assert.Null(HubFarm.Parse(Tracker.Replace(GroupField, HostAsleep))!.Host!.Note);
    }
}

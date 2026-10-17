using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>0.1.37: who a joining player is (the launcher's ticket) and whose farmer is whose.</summary>
public sealed class OwnershipTests
{
    private const string Tracker = "Zm9yLXRlc3Rz";
    private const string Token = "sdt_0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ATicketChecksOutForItsFarmAccountAndNameUntilItExpires()
    {
        string ticket = JoinTicket.Make(Tracker, Token, "acct-pat", "Pat", Now.AddHours(24));
        Assert.True(JoinTicket.Verify(Tracker, Token, "acct-pat", "Pat", ticket, Now));
        Assert.True(JoinTicket.Verify(Tracker, Token, "acct-pat", "Pat", ticket, Now.AddHours(23)));
        Assert.False(JoinTicket.Verify(Tracker, Token, "acct-pat", "Pat", ticket, Now.AddHours(25)));
        Assert.False(JoinTicket.Verify(Tracker, Token, "acct-casey", "Pat", ticket, Now));
        Assert.False(JoinTicket.Verify(Tracker, Token, "acct-pat", "Patrick", ticket, Now));
        Assert.False(JoinTicket.Verify("another-tracker", Token, "acct-pat", "Pat", ticket, Now));
        Assert.False(JoinTicket.Verify(Tracker, "sdt_other", "acct-pat", "Pat", ticket, Now));
        Assert.False(JoinTicket.Verify(Tracker, Token, "acct-pat", "Pat", ticket + "x", Now));
        Assert.False(JoinTicket.Verify(Tracker, Token, "acct-pat", "Pat", "nonsense", Now));
        Assert.False(JoinTicket.Verify(Tracker, Token, "acct-pat", "Pat", "123.", Now));
        Assert.False(JoinTicket.Verify(Tracker, Token, "acct-pat", "Pat", null, Now));
        Assert.False(JoinTicket.Verify(Tracker, null, "acct-pat", "Pat", ticket, Now));
    }

    [Fact]
    public void TheTicketIsSignedTheWayTheServerSignsIt()
    {
        // The server's reference: HMAC-SHA256 over "modrelay.join-ticket/1\n<tracker>\n<account>\n<name>\n<exp>" with key SHA-256(token), base64url, no padding.
        string ticket = JoinTicket.Make("t1", "tok", "a1", "Ann", DateTimeOffset.FromUnixTimeSeconds(1800000000));
        Assert.StartsWith("1800000000.", ticket);
        Assert.DoesNotContain("=", ticket);
        Assert.DoesNotContain("+", ticket);
        Assert.DoesNotContain("/", ticket);
        var key = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("tok"));
        var mac = System.Security.Cryptography.HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes("modrelay.join-ticket/1\nt1\na1\nAnn\n1800000000"));
        Assert.Equal("1800000000." + Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_'), ticket);
    }

    [Fact]
    public void TheTrackerIdComesFromItsUrl()
    {
        Assert.Equal(Tracker, JoinTicket.TrackerIdOf("https://modrelay.app/turns/" + Tracker));
        Assert.Equal(Tracker, JoinTicket.TrackerIdOf("https://modrelay.app/turns/" + Tracker + "/"));
        Assert.Null(JoinTicket.TrackerIdOf("https://modrelay.app/packs/x"));
        Assert.Null(JoinTicket.TrackerIdOf(""));
        Assert.Null(JoinTicket.TrackerIdOf(null));
    }

    [Fact]
    public void AnotherAccountsFarmerIsRefusedByName()
    {
        var pat = new JoinAccount("acct-pat", "Pat");
        Assert.Equal("MOSS belongs to Casey", FarmerOwnership.Refusal("MOSS", "acct-casey", "Casey", pat, false, null));
        Assert.Equal("MOSS belongs to another player", FarmerOwnership.Refusal("MOSS", "acct-casey", null, pat, false, null));
        Assert.Equal("MOSS belongs to Casey", FarmerOwnership.Refusal("MOSS", "acct-casey", "Casey", null, false, null));
        Assert.Null(FarmerOwnership.Refusal("MOSS", "acct-casey", "Casey", new JoinAccount("acct-casey", "Casey"), true, new[] { "acct-casey" }));
    }

    [Fact]
    public void AGroupsFarmTakesOnlyAccountsInTheGroup()
    {
        var group = new[] { "acct-pat", "acct-casey" };
        Assert.Equal(FarmerOwnership.PrivateFarm, FarmerOwnership.Refusal("New Farmer", null, null, null, true, group));
        Assert.Equal("Stranger is not in this farm's group", FarmerOwnership.Refusal("New Farmer", null, null, new JoinAccount("acct-x", "Stranger"), true, group));
        Assert.Null(FarmerOwnership.Refusal("New Farmer", null, null, new JoinAccount("acct-pat", "Pat"), true, group));
        // The group not yet known: an account is enough.
        Assert.Null(FarmerOwnership.Refusal("New Farmer", null, null, new JoinAccount("acct-x", "Stranger"), true, null));
        // Not a group's farm: anyone may make a farmer, or play one nobody owns.
        Assert.Null(FarmerOwnership.Refusal("New Farmer", null, null, null, false, group));
        Assert.Null(FarmerOwnership.Refusal("MOSS", null, null, null, false, null));
    }

    [Fact]
    public void TheLinkFileCarriesTheAccountAndTheHubTheOwners()
    {
        var link = HubLink.ParseFile("""{"schema":"modrelay.shared-day-link/1","pack":{"id":"p","name":"P"},"player":"Pat","account":{"id":"acct-pat","name":"Pat","ticket":"1.abc"}}""")!;
        Assert.Equal("acct-pat", link.AccountId);
        Assert.Equal("Pat", link.AccountName);
        Assert.Equal("1.abc", link.Ticket);
        Assert.Null(HubLink.ParseFile("""{"schema":"modrelay.shared-day-link/1","pack":{"id":"p","name":"P"},"player":null}""")!.AccountId);

        var farm = HubFarm.Parse("""
            {"tracker_id":"t","host_online":true,
             "members":[{"account_id":"acct-casey","display_name":"Casey","farmer_id":"2"},{"account_id":"acct-pat","display_name":"Pat","farmer_id":null}],
             "snapshot":{"farm":{"name":"Squeet Farm"},"date":{"label":"Spring 1"},"farmers":[
               {"id":"1","name":"Old","status":"Pending","online":false,"inCurrentTurn":false,"owner_id":"acct-pat","owner_name":"Pat"},
               {"id":"2","name":"MOSS","status":"Pending","online":true,"inCurrentTurn":true},
               {"id":"3","name":"Nobody's","status":"Pending","online":false,"inCurrentTurn":false},
               {"id":"-9","name":"Pat","status":"Pending","online":false,"inCurrentTurn":false,"placeholder":true,"account_id":"acct-pat"}]}}
            """)!;
        Assert.Equal(new[] { "acct-pat", "acct-casey", null, null }, farm.Farmers.Select(f => f.OwnerId));
        Assert.Equal(new[] { "Pat", "Casey", null, null }, farm.Farmers.Select(f => f.OwnerName));
        Assert.Equal(new[] { "1", "2", "3", "-9" }, farm.Farmers.Select(f => f.Id));
        Assert.True(farm.Farmers[3].Placeholder);
        Assert.False(farm.Farmers[0].Placeholder);
    }
}

/// <summary>
/// 0.1.41: a broken way of saying who you are must never lock a group out of its own farm.
/// The owner, 2026-10-15: "just be sure this isnt going to be an issue again."
/// </summary>
public sealed class UnprovenTicketTests
{
    private static readonly string[] Group = { "acct-pat", "acct-casey" };

    [Fact]
    public void BeforeAnyTicketHasWorkedNobodyIsTurnedAwayForFailingToProveIt()
    {
        // The owner of a farmer joining with a launcher that could not get a ticket: let in.
        Assert.Null(FarmerOwnership.Refusal("MOSS", "acct-casey", "Casey", null, true, Group, ticketsProven: false));
        // A player with no account at all on a group's farm: let in.
        Assert.Null(FarmerOwnership.Refusal("New Farmer", null, null, null, true, Group, ticketsProven: false));
        // But somebody who DOES say who they are is still held to it.
        Assert.Equal("MOSS belongs to Casey",
            FarmerOwnership.Refusal("MOSS", "acct-casey", "Casey", new JoinAccount("acct-pat", "Pat"), true, Group, ticketsProven: false));
        Assert.Equal("Stranger is not in this farm's group",
            FarmerOwnership.Refusal("New Farmer", null, null, new JoinAccount("acct-x", "Stranger"), true, Group, ticketsProven: false));
    }

    [Fact]
    public void OnceATicketHasWorkedTheFarmHoldsEveryoneToIt()
    {
        Assert.Equal("MOSS belongs to Casey", FarmerOwnership.Refusal("MOSS", "acct-casey", "Casey", null, true, Group, ticketsProven: true));
        Assert.Equal(FarmerOwnership.PrivateFarm, FarmerOwnership.Refusal("New Farmer", null, null, null, true, Group, ticketsProven: true));
        // The default is the proven rule, so nothing that was enforced before is softened by mistake.
        Assert.Equal(FarmerOwnership.PrivateFarm, FarmerOwnership.Refusal("New Farmer", null, null, null, true, Group));
    }

    [Fact]
    public void TheLogSaysWhatToFix()
    {
        string said = FarmerOwnership.NotProven("Pat");
        Assert.Contains("Pat", said);
        Assert.Contains("0.0.543", said);
        Assert.Contains("RequireGroupAccount", said);
    }
}

/// <summary>0.1.43: saying who is who by hand. The owner: "fix or allow combining in the UI in game?"</summary>
public sealed class WhoIsWhoTests
{
    private static FarmerSnapshot Farmer(long id, string name) => new(id, name, FarmerStatus.Pending, false, false);
    private static FarmerSnapshot Placeholder(string account, string name) =>
        new(DayCoordinator.PlaceholderId(account), DayCoordinator.PlaceholderLabel(name), FarmerStatus.Pending, false, false, MemberId: account, MemberName: name);

    private static readonly FarmerSnapshot[] Roster =
    {
        Farmer(1, "MOSS"), Farmer(2, "Swegulon"), Placeholder("acct-pat", "Pat"), Placeholder("acct-cleo", "Cleo"),
    };

    [Fact]
    public void TheMembersWaitingWithNoFarmerAreOfferedByTheirOwnName()
    {
        var waiting = WhoIsWho.Waiting(Roster);
        Assert.Equal(new[] { "Pat", "Cleo" }, waiting.Select(w => w.Name));
        Assert.Equal(new[] { "acct-pat", "acct-cleo" }, waiting.Select(w => w.MemberId));
        Assert.Empty(WhoIsWho.Waiting(new[] { Farmer(1, "MOSS") }));
    }

    [Fact]
    public void EveryRealFarmerCanBeAttached_NobodysFirst()
    {
        var owners = new Dictionary<long, string> { [2] = "Casey" };
        var candidates = WhoIsWho.CandidatesFor(Roster, id => owners.GetValueOrDefault(id));
        Assert.Equal(new long[] { 1, 2 }, candidates.Select(c => c.Id));
        Assert.Equal("MOSS", WhoIsWho.Label(candidates[0]));
        Assert.Equal("Swegulon (said to be Casey's)", WhoIsWho.Label(candidates[1]));
        // A placeholder is never a farmer to attach, and the host's own farmer can be left out.
        Assert.DoesNotContain(candidates, c => c.Id == DayCoordinator.PlaceholderId("acct-pat"));
        Assert.Single(WhoIsWho.CandidatesFor(Roster, _ => null, hostFarmerToSkip: 1));
    }

    [Fact]
    public void TheWordsSayWhatItDoesAndThatNothingIsLost()
    {
        string ask = WhoIsWho.Confirm("Pat", "MOSS");
        Assert.Contains("Pat", ask);
        Assert.Contains("MOSS", ask);
        Assert.Contains("Nothing anyone owns or did changes", ask);
        Assert.Equal("MOSS is Pat's farmer. The farm waits for them once from now on.", WhoIsWho.Said("Pat", "MOSS"));
    }
}

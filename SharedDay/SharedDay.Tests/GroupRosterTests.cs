using System.Text.Json;
using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>
/// 0.1.37: a group member with no farmer on the farm yet stands on the roster as a placeholder,
/// so a day cannot end while the farm has fewer farmers than the group has members (Squeet Farm,
/// 2026-10-15: one farmer, two members, Spring 1 to Spring 3 in two minutes).
/// </summary>
public sealed class GroupRosterTests
{
    private const long Moss = 2283117480133497730;
    private static readonly FarmerIdentity[] OneFarmer = { new(Moss, "MOSS") };
    private static readonly GroupMember Pat = new("acct-pat", "Pat");
    private static readonly GroupMember Casey = new("acct-casey", "Casey", Moss);
    private static long PatId => DayCoordinator.PlaceholderId("acct-pat");

    private static DayCoordinator SqueetFarm(params GroupMember[] group)
    {
        var day = DayCoordinator.Create("squeet", 0, OneFarmer);
        day.SetGroup(group);
        return day;
    }

    private static FarmerSnapshot Farmer(DayCoordinator day, long id) => day.Snapshot.Farmers.Single(f => f.Id == id);

    [Fact]
    public void NoGroupKeepsTodaysBehaviour()
    {
        var day = DayCoordinator.Create("squeet", 0, OneFarmer);
        Assert.False(day.SetGroup(null));
        Assert.False(day.SetGroup(Array.Empty<GroupMember>()));
        day.Join(Moss);
        day.Complete(Moss);
        Assert.True(day.IsReadyToAdvance);
        Assert.Single(day.Snapshot.Farmers);
    }

    [Fact]
    public void AMemberWithNoFarmerBlocksTheNight()
    {
        var day = SqueetFarm(Pat, Casey);
        var placeholder = Farmer(day, PatId);
        Assert.True(placeholder.IsPlaceholder);
        Assert.Equal("Pat (no farmer yet)", placeholder.Name);
        Assert.Equal("Pat", placeholder.MemberName);
        Assert.Equal("acct-pat", placeholder.MemberId);
        Assert.Equal(FarmerStatus.Pending, placeholder.Status);
        Assert.False(placeholder.IsOnline);
        day.Join(Moss);
        day.Complete(Moss);
        Assert.False(day.IsReadyToAdvance);
        Assert.Throws<DayCoordinationException>(() => day.BeginAdvance());
    }

    [Fact]
    public void SkippingThePlaceholderLetsTheDayEnd()
    {
        var day = SqueetFarm(Pat, Casey);
        day.Join(Moss);
        day.Complete(Moss);
        Assert.True(day.Skip(PatId));
        Assert.Equal(FarmerStatus.Skipped, Farmer(day, PatId).Status);
        Assert.True(day.IsReadyToAdvance);
        day.BeginAdvance();
        day.AcknowledgeSavedNextDay(day.Snapshot.AdvanceToken!, 1, OneFarmer);
        // Back tomorrow, still to play, nothing kept.
        var tomorrow = Farmer(day, PatId);
        Assert.Equal(FarmerStatus.Pending, tomorrow.Status);
        Assert.True(tomorrow.IsPlaceholder);
        Assert.Equal(1, tomorrow.AbsentDays);
        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void ANewFarmerResolvesThePlaceholderAndPlaysToday()
    {
        var day = SqueetFarm(Pat, Casey);
        day.Join(Moss);
        day.Complete(Moss);
        Assert.True(day.AddFarmer(new FarmerIdentity(777, "Pat")));
        Assert.DoesNotContain(day.Snapshot.Farmers, f => f.IsPlaceholder);
        Assert.Equal(FarmerStatus.Pending, Farmer(day, 777).Status);
        Assert.False(day.IsReadyToAdvance);
        Assert.Equal(JoinDisposition.StartSession, day.Join(777).Disposition);
        day.Complete(777);
        Assert.True(day.IsReadyToAdvance);
    }

    [Fact]
    public void ANewFarmerWithAnotherNameStillCoversTheMissingMemberByCount()
    {
        var day = SqueetFarm(Pat, Casey);
        day.AddFarmer(new FarmerIdentity(777, "Farmer Bob"));
        Assert.DoesNotContain(day.Snapshot.Farmers, f => f.IsPlaceholder);
    }

    [Fact]
    public void WithoutPicksTheCountRuleCoversAsManyMembersAsThereAreFarmers()
    {
        // Nobody picked a farmer and no name matches: one farmer covers one member, the other member has none.
        var day = SqueetFarm(new GroupMember("acct-pat", "Pat"), new GroupMember("acct-casey", "Casey"));
        var placeholders = day.Snapshot.Farmers.Where(f => f.IsPlaceholder).ToArray();
        Assert.Single(placeholders);
        Assert.Equal("Casey (no farmer yet)", placeholders[0].Name);
        // A pick settles it: Casey plays MOSS, so Pat is the one with no farmer.
        Assert.True(day.SetGroup(new[] { new GroupMember("acct-pat", "Pat"), new GroupMember("acct-casey", "Casey", Moss) }));
        placeholders = day.Snapshot.Farmers.Where(f => f.IsPlaceholder).ToArray();
        Assert.Single(placeholders);
        Assert.Equal("Pat (no farmer yet)", placeholders[0].Name);
    }

    [Fact]
    public void ANameMatchCountsAsAFarmer()
    {
        var day = DayCoordinator.Create("squeet", 0, new FarmerIdentity[] { new(1, "MOSS"), new(2, "pat") });
        day.SetGroup(new[] { new GroupMember("acct-pat", "Pat"), new GroupMember("acct-casey", "Casey"), new GroupMember("acct-c", "Charlie") });
        var placeholders = day.Snapshot.Farmers.Where(f => f.IsPlaceholder).ToArray();
        Assert.Single(placeholders);
        Assert.Equal("Charlie (no farmer yet)", placeholders[0].Name);
    }

    [Fact]
    public void AMemberWhoLeavesTheGroupLeavesTheRoster()
    {
        var day = SqueetFarm(Pat, Casey);
        Assert.Contains(day.Snapshot.Farmers, f => f.IsPlaceholder);
        Assert.True(day.SetGroup(new[] { Casey }));
        Assert.DoesNotContain(day.Snapshot.Farmers, f => f.IsPlaceholder);
        day.Join(Moss);
        day.Complete(Moss);
        Assert.True(day.IsReadyToAdvance);
    }

    [Fact]
    public void TheSameGroupAgainChangesNothing()
    {
        var day = SqueetFarm(Pat, Casey);
        long revision = day.Snapshot.Revision;
        Assert.False(day.SetGroup(new[] { Pat, Casey }));
        Assert.Equal(revision, day.Snapshot.Revision);
    }

    [Fact]
    public void APlaceholderCannotEndADayByItself()
    {
        // A farm with members but no farmers at all: skipping every placeholder is not a day played.
        var day = DayCoordinator.Create("fresh", 0, Array.Empty<FarmerIdentity>());
        day.SetGroup(new[] { Pat, new GroupMember("acct-casey", "Casey") });
        foreach (var f in day.Snapshot.Farmers) day.Skip(f.Id);
        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void APlaceholderHasNoVote()
    {
        var day = SqueetFarm(Pat, Casey, new GroupMember("acct-c", "Charlie"));
        long charlie = DayCoordinator.PlaceholderId("acct-c");
        var roster = day.Snapshot.Farmers;
        Assert.Equal(new[] { Moss }, SkipVoteBook.Electorate(roster, PatId));
        var book = new SkipVoteBook();
        Assert.Equal(SkipVoteResult.Passed, book.Cast(0, Moss, PatId, roster));
        Assert.Equal(SkipVoteResult.Passed, book.Cast(0, Moss, charlie, roster));
    }

    [Fact]
    public void TheAbsenceRuleStopsWaitingForAMemberWhoNeverComes()
    {
        var day = SqueetFarm(Pat, Casey);
        day.AbsenceLimit = 2;
        for (int d = 0; d < 2; d++)
        {
            day.Join(Moss);
            day.Complete(Moss);
            day.Skip(PatId);
            day.BeginAdvance();
            day.AcknowledgeSavedNextDay(day.Snapshot.AdvanceToken!, d + 1, OneFarmer);
            day.Disconnect(Moss);
        }
        var placeholder = Farmer(day, PatId);
        Assert.True(placeholder.Away);
        Assert.Equal(FarmerStatus.Skipped, placeholder.Status);
        day.Join(Moss);
        day.Complete(Moss);
        Assert.True(day.IsReadyToAdvance);
        // A farmer of theirs still takes the placeholder's place.
        day.AddFarmer(new FarmerIdentity(777, "Pat"));
        Assert.DoesNotContain(day.Snapshot.Farmers, f => f.IsPlaceholder);
        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void TheGroupAndItsPlaceholdersSurviveTheJournal()
    {
        var day = SqueetFarm(Pat, Casey);
        day.Skip(PatId);
        var json = JsonSerializer.Serialize(day.ExportJournal());
        var restored = DayCoordinator.Restore(JsonSerializer.Deserialize<DayJournal>(json)!);
        Assert.Equal(2, restored.Group.Count);
        var placeholder = Farmer(restored, PatId);
        Assert.True(placeholder.IsPlaceholder);
        Assert.Equal(FarmerStatus.Skipped, placeholder.Status);
        Assert.Equal("Pat (no farmer yet)", placeholder.Name);
        Assert.Equal(ReconciliationResult.SameDay, restored.ReconcileSavedDay(0, OneFarmer));
        Assert.Equal(FarmerStatus.Skipped, Farmer(restored, PatId).Status);
    }

    [Fact]
    public void AJournalFromBeforeTheGroupStillLoads()
    {
        var journal = JsonSerializer.Deserialize<DayJournal>("""{"SchemaVersion":1,"SaveId":"squeet","AbsoluteDay":0,"Revision":1,"Mode":0,"Phase":0,"SessionNumber":0,"AdvanceToken":null,"Farmers":[{"Id":1,"Name":"MOSS","Status":0}],"CurrentSessionFarmerIds":[]}""")!;
        var day = DayCoordinator.Restore(journal);
        Assert.Empty(day.Group);
        Assert.DoesNotContain(day.Snapshot.Farmers, f => f.IsPlaceholder);
    }

    [Fact]
    public void DuringTheNightOnlyTheStoredGroupChanges()
    {
        var day = SqueetFarm(Casey);
        day.Join(Moss);
        day.Complete(Moss);
        day.BeginAdvance();
        Assert.True(day.SetGroup(new[] { Pat, Casey }));
        Assert.DoesNotContain(day.Snapshot.Farmers, f => f.IsPlaceholder);
        day.AcknowledgeSavedNextDay(day.Snapshot.AdvanceToken!, 1, OneFarmer);
        Assert.Contains(day.Snapshot.Farmers, f => f.IsPlaceholder && f.Status == FarmerStatus.Pending);
    }

    [Fact]
    public void OrdersCarryTheGroup()
    {
        var orders = TrackerOrders.Parse("""{"barred":[],"new_farmers":true,"members":[{"id":"acct-pat","name":"Pat","farmer_id":null},{"id":"acct-casey","name":"Casey","farmer_id":"2283117480133497730"},{"id":"","name":"nobody"}]}""")!;
        Assert.NotNull(orders.Members);
        Assert.Equal(2, orders.Members!.Count);
        Assert.Equal(new GroupMember("acct-casey", "Casey", Moss), orders.Members[1]);
        Assert.Null(TrackerOrders.Parse("""{"barred":[],"new_farmers":true}""")!.Members);
        Assert.Empty(TrackerOrders.Parse("""{"members":[]}""")!.Members!);
    }

    [Fact]
    public void PlaceholderIdsAreStableAndDistinct()
    {
        Assert.Equal(DayCoordinator.PlaceholderId("acct-pat"), DayCoordinator.PlaceholderId("acct-pat"));
        Assert.NotEqual(DayCoordinator.PlaceholderId("acct-pat"), DayCoordinator.PlaceholderId("acct-casey"));
    }
}

public sealed class GroupOwnersTests
{
    [Fact]
    public void AMemberWhoOwnsAFarmerHereIsMatchedToItWithoutAPick()
    {
        var members = new[] { new GroupMember("acct-pat", "Pat"), new GroupMember("acct-casey", "Casey", 5) };
        var filled = GroupMember.WithOwnedFarmers(members, new[] { (777L, "acct-pat"), (5L, "acct-x"), (9L, "acct-pat") });
        Assert.Equal(777, filled[0].FarmerId);
        // A pick stands, whatever the farm says about owners.
        Assert.Equal(5, filled[1].FarmerId);
        var day = DayCoordinator.Create("squeet", 0, new FarmerIdentity[] { new(5, "MOSS"), new(777, "Farmer Bob") });
        day.SetGroup(filled);
        Assert.DoesNotContain(day.Snapshot.Farmers, f => f.IsPlaceholder);
    }
}

public sealed class SkipOrderTests
{
    [Fact]
    public void OrdersCarrySkipsForADay()
    {
        var orders = TrackerOrders.Parse("""{"barred":[],"new_farmers":true,"skips":[{"farmer_id":"-1234567890123456789","day":3},{"farmer_id":"x","day":3},{"farmer_id":"5"}]}""")!;
        Assert.Equal(new[] { new SkipOrder(-1234567890123456789, 3) }, orders.Skips);
        Assert.Null(TrackerOrders.Parse("""{"barred":[],"new_farmers":true}""")!.Skips);
        Assert.NotNull(TrackerOrders.Parse("""{"skips":[]}"""));
    }
}

public sealed class HostAccountTests
{
    [Fact]
    public void TheHostPcsAccountIsMarkedAndKeepsItsPlace()
    {
        var orders = TrackerOrders.Parse("""{"members":[{"id":"acct-box","name":"HostPC","farmer_id":null,"host":true},{"id":"acct-pat","name":"Patrick Sample","farmer_id":null}]}""")!;
        Assert.True(orders.Members![0].Host);
        Assert.False(orders.Members[1].Host);
        // Left in by a host whose own farmer plays: a placeholder like anyone's. The mod drops it otherwise.
        var day = DayCoordinator.Create("squeet", 0, new FarmerIdentity[] { new(1, "MOSS") });
        day.SetGroup(orders.Members);
        Assert.Equal(2, day.Group.Count);
        Assert.True(day.Group[0].Host);
        // With the host account counted, the one farmer went to it and both real members stood as placeholders;
        // without it, the farmer covers one member and only the other has none.
        var counted = DayCoordinator.Create("squeet", 0, new FarmerIdentity[] { new(1, "MOSS") });
        counted.SetGroup(orders.Members.Append(new GroupMember("acct-casey", "M0SSLING")).ToList());
        Assert.Equal(2, counted.Snapshot.Farmers.Count(f => f.IsPlaceholder));
        var players = DayCoordinator.Create("squeet", 0, new FarmerIdentity[] { new(1, "MOSS") });
        players.SetGroup(orders.Members.Where(m => !m.Host).Append(new GroupMember("acct-casey", "M0SSLING")).ToList());
        Assert.Single(players.Snapshot.Farmers.Where(f => f.IsPlaceholder));
    }
}

/// <summary>0.1.41: a group that came back empty is a broken answer, never an emptied group.</summary>
public sealed class EmptyGroupTests
{
    private const long Moss = 2283117480133497730;

    [Fact]
    public void AnEmptyGroupIsRefusedAndThePlaceholdersStand()
    {
        var day = DayCoordinator.Create("squeet", 0, new FarmerIdentity[] { new(Moss, "MOSS") });
        day.SetGroup(new[] { new GroupMember("acct-pat", "Pat"), new GroupMember("acct-casey", "Casey", Moss) });
        Assert.Single(day.Snapshot.Farmers.Where(f => f.IsPlaceholder));

        Assert.Throws<EmptyGroupException>(() => day.SetGroup(Array.Empty<GroupMember>()));
        Assert.Throws<EmptyGroupException>(() => day.SetGroup(new GroupMember[] { new("", "") }));
        // Nothing moved: the day still waits for Pat.
        Assert.Single(day.Snapshot.Farmers.Where(f => f.IsPlaceholder));
        Assert.Equal(2, day.Group.Count);
        day.Join(Moss);
        day.Complete(Moss);
        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void AFarmThatNeverHadAGroupTakesAnEmptyOneQuietly()
    {
        var day = DayCoordinator.Create("squeet", 0, new FarmerIdentity[] { new(Moss, "MOSS") });
        Assert.False(day.SetGroup(Array.Empty<GroupMember>()));
        Assert.False(day.SetGroup(null));
        Assert.Empty(day.Group);
    }
}

/// <summary>
/// 0.1.42: one person never holds the day twice. The owner, 2026-10-15: "and what about duplicates
/// of same person?" — the farm waited on MOSS and on M0SSLING, who are one man.
/// </summary>
public sealed class DuplicatePersonTests
{
    private static DayCoordinator TwoFarmersOneAccount()
    {
        var day = DayCoordinator.Create("squeet", 0, new FarmerIdentity[] { new(1, "MOSS"), new(2, "Swegulon"), new(3, "Alice") });
        day.FarmerOwners[1] = "acct-casey";
        day.FarmerOwners[2] = "acct-casey";
        day.FarmerOwners[3] = "acct-alice";
        return day;
    }

    [Fact]
    public void TheSpareFarmersOfOneAccountAreNamed()
    {
        var day = TwoFarmersOneAccount();
        // Nobody on the farm and nobody played: the furthest through the day keeps the place.
        Assert.Equal(new long[] { 2 }, day.SpareFarmersOf("acct-casey"));
        // Somebody with one farmer has no spare, and an account with none is not a problem.
        Assert.Empty(day.SpareFarmersOf("acct-alice"));
        Assert.Empty(day.SpareFarmersOf("acct-nobody"));
    }

    [Fact]
    public void TheFarmerTheyArePlayingIsTheOneThatCounts()
    {
        var day = TwoFarmersOneAccount();
        day.Join(2);
        Assert.Equal(new long[] { 1 }, day.SpareFarmersOf("acct-casey"));
        // And once one of them has played, that is the one that counts, online or not.
        day.Complete(2);
        day.Disconnect(2);
        Assert.Equal(new long[] { 1 }, day.SpareFarmersOf("acct-casey"));
    }

    [Fact]
    public void SittingTheSpareOutLetsTheDayEndOnTheOnePersonPlayingIt()
    {
        var day = TwoFarmersOneAccount();
        day.Join(2);
        day.Complete(2);
        day.Join(3);
        day.Complete(3);
        // Casey's other farmer still holds it...
        Assert.False(day.IsReadyToAdvance);
        foreach (long spare in day.SpareFarmersOf("acct-casey")) day.StepAway(spare);
        Assert.True(day.IsReadyToAdvance);
        // ...and it is back in the day the moment somebody is on it, like any farmer stepped away from.
        Assert.Equal(FarmerStatus.Skipped, day.Snapshot.Farmers.Single(f => f.Id == 1).Status);
        day.Join(1);
        Assert.Equal(FarmerStatus.Pending, day.Snapshot.Farmers.Single(f => f.Id == 1).Status);
        Assert.False(day.IsReadyToAdvance);
    }

    [Fact]
    public void APlaceholderIsNeverCountedAsSomebodysSpareFarmer()
    {
        var day = DayCoordinator.Create("squeet", 0, new FarmerIdentity[] { new(1, "MOSS") });
        day.SetGroup(new[] { new GroupMember("acct-casey", "Casey", 1), new GroupMember("acct-pat", "Pat") });
        day.FarmerOwners[1] = "acct-casey";
        long placeholder = DayCoordinator.PlaceholderId("acct-pat");
        day.FarmerOwners[placeholder] = "acct-casey";  // even if the adapter were confused
        Assert.Empty(day.SpareFarmersOf("acct-casey"));
    }
}

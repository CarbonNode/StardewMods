using System.Text.Json;
using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>
/// The money rules of a wallet change. Squeet Farm's host PC has a farmer of its own
/// ("HostPC") that nobody plays; the game's own split would hand it a share nobody can spend.
/// Every case here checks the same thing first: no coin is made and none is lost.
/// </summary>
public sealed class WalletPlanTests
{
    private static WalletFarmer Host(int money = 0) => new(1, "HostPC", WalletRole.Host, money, IsHostFarmer: true);
    private static WalletFarmer PlayingHost(int money = 0) => new(1, "Pat", WalletRole.Player, money, IsHostFarmer: true);
    private static WalletFarmer Player(long id, string name, int money = 0) => new(id, name, WalletRole.Player, money);
    private static WalletFarmer Away(long id, string name, int money = 0) => new(id, name, WalletRole.Away, money);

    private static int After(WalletPlan plan, long id) => plan.Farmers.Single(f => f.Id == id).After;

    /// <summary>The farm after a plan is applied, as the next start would read it.</summary>
    private static (WalletMode Mode, int Pot, List<WalletFarmer> Farmers) Apply(WalletPlan plan, IReadOnlyList<WalletFarmer> farmers)
    {
        Assert.True(plan.Changes);
        Assert.True(plan.Conserves);
        return (plan.To, plan.To == WalletMode.Shared ? plan.Pot : 0, farmers.Select(f => f with { Money = After(plan, f.Id) }).ToList());
    }

    // ---- one shared wallet to a wallet each ----

    [Fact]
    public void ASplitSharesThePotAmongThePlayersAndTheHostFarmerHoldsOnlyTheRemainder()
    {
        var farmers = new[] { Host(), Player(2, "Alice"), Player(3, "Bob") };
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 12631, farmers);
        Assert.Null(plan.Held);
        Assert.True(plan.Changes);
        Assert.Equal(6315, After(plan, 2));
        Assert.Equal(6315, After(plan, 3));
        Assert.Equal(1, After(plan, 1));
        Assert.Equal(1, plan.Leftover);
        Assert.Equal(12631, plan.Pot);
        Assert.Equal(12631, plan.TotalBefore);
        Assert.Equal(12631, plan.TotalAfter);
    }

    [Fact]
    public void ThePotThatDividesEvenlyLeavesTheHostFarmerNothing()
    {
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 12630, new[] { Host(), Player(2, "Alice"), Player(3, "Bob"), Player(4, "Cleo") });
        Assert.Equal(new[] { 0, 4210, 4210, 4210 }, plan.Farmers.Select(f => f.After));
        Assert.Equal(0, plan.Leftover);
        Assert.True(plan.Conserves);
    }

    [Fact]
    public void TheGamesOwnSplitWouldHaveGivenTheHostFarmerAShareAndLostACoin()
    {
        // ManorHouse.SeparateWallets: pot / every claimed farmer, remainder dropped.
        int pot = 12631, everyone = 3, gamesShare = pot / everyone;
        Assert.Equal(4210, gamesShare);
        Assert.Equal(1, pot - gamesShare * everyone);
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, pot, new[] { Host(), Player(2, "Alice"), Player(3, "Bob") });
        Assert.True(After(plan, 2) > gamesShare);
        Assert.Equal(pot, plan.TotalAfter);
    }

    [Fact]
    public void AHostWhoPlaysIsAPlayerAndKeepsTheRemainder()
    {
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 1000, new[] { PlayingHost(), Player(2, "Alice"), Player(3, "Bob") });
        Assert.Equal(334, After(plan, 1));
        Assert.Equal(333, After(plan, 2));
        Assert.Equal(333, After(plan, 3));
        // Nothing is waiting anywhere: the host's coin is the host's.
        Assert.Equal(0, plan.Leftover);
        Assert.True(plan.Conserves);
    }

    [Fact]
    public void AnAwayFarmerGetsNothingAtASplitAndIsStillListed()
    {
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 9000, new[] { Host(), Player(2, "Alice"), Away(3, "Dana"), Player(4, "Bob") });
        Assert.Equal(4500, After(plan, 2));
        Assert.Equal(4500, After(plan, 4));
        Assert.Equal(0, After(plan, 3));
        Assert.Contains(plan.Farmers, f => f.Name == "Dana" && f.Role == WalletRole.Away);
        Assert.True(plan.Conserves);
    }

    [Fact]
    public void AStaleNumberInAFarmersWalletIsNotMoneyWhileThePotIsShared()
    {
        // With one shared wallet the game leaves a copy of the pot in every farmer's own entry.
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 1000, new[] { Host(1000), Player(2, "Alice", 1000), Player(3, "Bob", 1000) });
        Assert.Equal(1000, plan.TotalBefore);
        Assert.Equal(1000, plan.TotalAfter);
        Assert.All(plan.Farmers, f => Assert.Equal(0, f.Before));
    }

    [Fact]
    public void OnePlayerGetsTheWholePot()
    {
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 777, new[] { Host(), Player(2, "Alice") });
        Assert.Equal(777, After(plan, 2));
        Assert.Equal(0, After(plan, 1));
        Assert.Equal(0, plan.Leftover);
    }

    [Fact]
    public void WithNoPlayersASplitIsHeldAndNothingMoves()
    {
        foreach (var farmers in new[] { new[] { Host() }, new[] { Host(), Away(2, "Dana") }, Array.Empty<WalletFarmer>() })
        {
            var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 5000, farmers);
            Assert.Equal(WalletPlan.HeldPlayers, plan.Held);
            Assert.False(plan.Changes);
            Assert.Equal(WalletMode.Shared, plan.From);
            Assert.Equal(5000, plan.TotalBefore);
            Assert.Equal(5000, plan.TotalAfter);
        }
    }

    [Fact]
    public void AnEmptyPotSplitsIntoEmptyWallets()
    {
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 0, new[] { Host(), Player(2, "Alice"), Player(3, "Bob") });
        Assert.True(plan.Changes);
        Assert.All(plan.Farmers, f => Assert.Equal(0, f.After));
    }

    [Fact]
    public void FewerCoinsThanPlayersAllStayWithTheHostFarmer()
    {
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 2, new[] { Host(), Player(2, "Alice"), Player(3, "Bob"), Player(4, "Cleo") });
        Assert.Equal(2, After(plan, 1));
        Assert.Equal(2, plan.Leftover);
        Assert.True(plan.Conserves);
    }

    [Fact]
    public void TheBiggestPotTheGameCanHoldStillSplitsExactly()
    {
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, int.MaxValue, new[] { Host(), Player(2, "Alice"), Player(3, "Bob") });
        Assert.Equal(int.MaxValue, plan.TotalAfter);
        Assert.Equal(1, plan.Leftover);
    }

    // ---- a wallet each to one shared wallet ----

    [Fact]
    public void AMergePutsInEveryFarmersMoneyTheHostFarmersAndAnAwayFarmersToo()
    {
        var farmers = new[] { Host(7), Player(2, "Alice", 4210), Away(3, "Dana", 300), Player(4, "Bob", 113) };
        var plan = WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 999999, farmers);
        Assert.Null(plan.Held);
        Assert.Equal(4630, plan.Pot);
        Assert.Equal(4630, plan.TotalBefore);
        Assert.Equal(4630, plan.TotalAfter);
        Assert.All(plan.Farmers, f => Assert.Equal(0, f.After));
        Assert.Equal(new[] { 7, 4210, 300, 113 }, plan.Farmers.Select(f => f.Before));
        Assert.Equal(0, plan.Leftover);
    }

    [Fact]
    public void TheStalePotIsNotMoneyWhileEveryoneHasAWallet()
        => Assert.Equal(300, WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 50000, new[] { Host(), Player(2, "Alice", 300) }).Pot);

    [Fact]
    public void AMergeTooBigForOneWalletIsHeldWhereTheGameWouldOverflow()
    {
        var farmers = new[] { Host(), Player(2, "Alice", int.MaxValue), Player(3, "Bob", 1) };
        var plan = WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 0, farmers);
        Assert.Equal(WalletPlan.HeldTooMuch, plan.Held);
        Assert.False(plan.Changes);
        Assert.Equal(WalletMode.Separate, plan.From);
        Assert.Equal(new[] { 0, int.MaxValue, 1 }, plan.Farmers.Select(f => f.After));
        // Exactly at the limit is fine.
        Assert.Null(WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 0, new[] { Host(), Player(2, "Alice", int.MaxValue - 1), Player(3, "Bob", 1) }).Held);
    }

    [Fact]
    public void AMergeNeedsNoPlayers()
    {
        var plan = WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 0, new[] { Host(40), Away(2, "Dana", 60) });
        Assert.Null(plan.Held);
        Assert.Equal(100, plan.Pot);
    }

    // ---- already as wished, and applying twice ----

    [Theory]
    [InlineData(WalletMode.Shared)]
    [InlineData(WalletMode.Separate)]
    public void NoWishChangesNothingWhateverTheFarmLooksLike(WalletMode inForce)
    {
        var farmers = new[] { Host(900), Player(2, "Alice", 4210), Away(3, "Dana", 300) };
        var plan = WalletRules.Plan(inForce, null, 12630, farmers);
        Assert.False(plan.Changes);
        Assert.Null(plan.Held);
        Assert.Equal(inForce, plan.To);
        Assert.Equal(plan.TotalBefore, plan.TotalAfter);
        Assert.All(plan.Farmers, f => Assert.Equal(f.Before, f.After));
    }

    [Fact]
    public void AWishAlreadyInForceChangesNothing()
    {
        Assert.False(WalletRules.Plan(WalletMode.Shared, WalletMode.Shared, 12630, new[] { Host(), Player(2, "Alice") }).Changes);
        // A wallet each, and the host farmer holds nothing worth sharing.
        var plan = WalletRules.Plan(WalletMode.Separate, WalletMode.Separate, 0, new[] { Host(1), Player(2, "Alice", 10), Player(3, "Bob", 20) });
        Assert.False(plan.Changes);
        Assert.Null(plan.Held);
        Assert.Equal(1, plan.Leftover);
    }

    [Fact]
    public void ApplyingTheSameWishTwiceMovesNothingTheSecondTime()
    {
        var farmers = new List<WalletFarmer> { Host(), Player(2, "Alice"), Player(3, "Bob"), Player(4, "Cleo") };
        var first = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 12632, farmers);
        var (mode, pot, after) = Apply(first, farmers);
        Assert.Equal(2, after.Single(f => f.IsHostFarmer).Money);
        var second = WalletRules.Plan(mode, WalletMode.Separate, pot, after);
        Assert.False(second.Changes);
        Assert.Null(second.Held);
        Assert.Equal(after.Select(f => f.Money), second.Farmers.Select(f => f.After));

        var merged = Apply(WalletRules.Plan(mode, WalletMode.Shared, pot, after), after);
        Assert.False(WalletRules.Plan(merged.Mode, WalletMode.Shared, merged.Pot, merged.Farmers).Changes);
    }

    // ---- switching back ----

    [Fact]
    public void SplittingAndMergingAgainGivesBackExactlyThePot()
    {
        var farmers = new List<WalletFarmer> { Host(), Player(2, "Alice"), Away(3, "Dana"), Player(4, "Bob"), Player(5, "Cleo") };
        var split = Apply(WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 100003, farmers), farmers);
        var merged = WalletRules.Plan(split.Mode, WalletMode.Shared, split.Pot, split.Farmers);
        Assert.Equal(100003, merged.Pot);
        var back = Apply(merged, split.Farmers);
        // And once more: the same split as the first time.
        var again = WalletRules.Plan(back.Mode, WalletMode.Separate, back.Pot, back.Farmers);
        Assert.Equal(33334, After(again, 2));
        Assert.Equal(1, again.Leftover);
        Assert.Equal(100003, again.TotalAfter);
    }

    [Fact]
    public void SwitchingBackAfterPlayersSpentAndEarnedMergesWhatIsThereNow()
    {
        var farmers = new List<WalletFarmer> { Host(), Player(2, "Alice"), Player(3, "Bob") };
        var split = Apply(WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 1001, farmers), farmers);
        var later = split.Farmers.Select(f => f.Id == 2 ? f with { Money = f.Money - 400 } : f.Id == 3 ? f with { Money = f.Money + 250 } : f).ToList();
        var merged = WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 0, later);
        Assert.Equal(1001 - 400 + 250, merged.Pot);
        Assert.True(merged.Conserves);
    }

    // ---- a farmer made later ----

    [Fact]
    public void AFarmerMadeAfterASplitBringsTheirOwnStartingMoneyAndTakesNobodyElses()
    {
        var farmers = new List<WalletFarmer> { Host(), Player(2, "Alice"), Player(3, "Bob") };
        var split = Apply(WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 10001, farmers), farmers);
        // The game gives a new farmer 500g of their own when everyone has a wallet.
        var withNew = split.Farmers.Append(Player(9, "Newcomer", 500)).ToList();
        var next = WalletRules.Plan(WalletMode.Separate, WalletMode.Separate, 0, withNew);
        Assert.False(next.Changes);
        Assert.Equal(new[] { 1, 5000, 5000, 500 }, next.Farmers.Select(f => f.After));
        // Switching back counts their 500g like anyone's.
        Assert.Equal(10501, WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 0, withNew).Pot);
    }

    [Fact]
    public void AFarmerMadeAfterAMergeSharesThePotAndTheNextSplitCountsThem()
    {
        var farmers = new List<WalletFarmer> { Host(3), Player(2, "Alice", 600), Player(3, "Bob", 400) };
        var merged = Apply(WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 0, farmers), farmers);
        var withNew = merged.Farmers.Append(Player(9, "Newcomer")).ToList();
        Assert.False(WalletRules.Plan(WalletMode.Shared, WalletMode.Shared, merged.Pot, withNew).Changes);
        var split = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, merged.Pot, withNew);
        Assert.Equal(334, After(split, 9));
        Assert.Equal(1, split.Leftover);
        Assert.Equal(1003, split.TotalAfter);
    }

    // ---- money that lands on the host farmer while everyone has a wallet ----

    [Fact]
    public void MoneyOnTheUnplayedHostFarmerStaysThereAndIsNeverSharedOutByItself()
    {
        // Nothing moves between wallets unless the owner chose a change and read who gets what.
        var farmers = new[] { Host(301), Player(2, "Alice", 10), Away(3, "Dana", 77), Player(4, "Bob", 20) };
        var plan = WalletRules.Plan(WalletMode.Separate, WalletMode.Separate, 0, farmers);
        Assert.False(plan.Changes);
        Assert.Null(plan.Held);
        Assert.Equal(301, plan.Leftover);
        Assert.Equal(new[] { 301, 10, 77, 20 }, plan.Farmers.Select(f => f.After));
        Assert.Equal(408, plan.TotalBefore);
        Assert.Equal(408, plan.TotalAfter);
    }

    [Fact]
    public void AHostWhoPlaysKeepsTheirOwnWallet()
        => Assert.False(WalletRules.Plan(WalletMode.Separate, WalletMode.Separate, 0, new[] { PlayingHost(5000), Player(2, "Alice", 10) }).Changes);

    [Fact]
    public void NothingMovesWithoutAWish()
        => Assert.False(WalletRules.Plan(WalletMode.Separate, null, 0, new[] { Host(5000), Player(2, "Alice", 10) }).Changes);

    [Fact]
    public void AWalletEachWithAnOverfullWalletIsStillLeftAlone()
    {
        var plan = WalletRules.Plan(WalletMode.Separate, WalletMode.Separate, 0, new[] { Host(10), Player(2, "Alice", int.MaxValue - 3) });
        Assert.Null(plan.Held);
        Assert.False(plan.Changes);
    }

    [Fact]
    public void TheHostFarmersMoneyGoesIntoTheNextMerge()
    {
        var plan = WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 0, new[] { Host(301), Player(2, "Alice", 10) });
        Assert.True(plan.Changes);
        Assert.Equal(311, plan.Pot);
    }

    // ---- a farm that cannot be counted is left alone ----

    [Fact]
    public void AFarmThatDoesNotAddUpIsLeftExactlyAsItIs()
    {
        Assert.Equal(WalletPlan.HeldFailed, WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, -5, new[] { Host(), Player(2, "Alice") }).Held);
        Assert.Equal(WalletPlan.HeldFailed, WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 0, new[] { Host(), Player(2, "Alice", -1) }).Held);
        Assert.Equal(WalletPlan.HeldFailed, WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 100, new[] { Host(), Player(2, "Alice"), Player(2, "Alice again") }).Held);
        Assert.Equal(WalletPlan.HeldFailed, WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 100, new[] { Host(), Host() with { Id = 5 }, Player(2, "Alice") }).Held);
        // Without a wish even that farm is only reported.
        var plan = WalletRules.Plan(WalletMode.Shared, null, -5, new[] { Host(), Player(2, "Alice") });
        Assert.Null(plan.Held);
        Assert.False(plan.Changes);
    }

    [Fact]
    public void AListWithoutAHostFarmerStillLosesNoCoin()
    {
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 11, new[] { Player(2, "Alice"), Player(3, "Bob"), Player(4, "Cleo") });
        Assert.Equal(11, plan.TotalAfter);
        Assert.Equal(5, After(plan, 2));
    }

    // ---- every coin, for any farm ----

    [Fact]
    public void NoCoinIsEverMadeOrLost()
    {
        var random = new Random(20261007);
        for (int round = 0; round < 5000; round++)
        {
            var farmers = new List<WalletFarmer>();
            bool hostPlays = random.Next(4) == 0;
            farmers.Add(new(1, "Host", hostPlays ? WalletRole.Player : WalletRole.Host, random.Next(0, 2_000_000), IsHostFarmer: true));
            int others = random.Next(0, 9);
            for (int i = 0; i < others; i++)
                farmers.Add(new(10 + i, "F" + i, random.Next(4) == 0 ? WalletRole.Away : WalletRole.Player, random.Next(0, random.Next(2) == 0 ? 50 : 300_000_000)));
            int pot = random.Next(0, random.Next(2) == 0 ? 20 : int.MaxValue);
            foreach (var inForce in new[] { WalletMode.Shared, WalletMode.Separate })
            foreach (WalletMode? wish in new WalletMode?[] { null, WalletMode.Shared, WalletMode.Separate })
            {
                var plan = WalletRules.Plan(inForce, wish, pot, farmers);
                Assert.True(plan.Conserves, $"round {round}: {inForce} to {wish}");
                Assert.Equal(inForce == WalletMode.Shared ? pot : farmers.Sum(f => (long)f.Money), plan.TotalBefore);
                Assert.Equal(farmers.Count, plan.Farmers.Count);
                Assert.All(plan.Farmers, f => Assert.True(f.After >= 0));
                long after = plan.To == WalletMode.Shared && plan.Changes ? plan.Pot
                    : plan.Changes || inForce == WalletMode.Separate ? plan.Farmers.Sum(f => (long)f.After) : pot;
                Assert.Equal(plan.TotalBefore, after);
                if (plan.Held is not null) Assert.False(plan.Changes);
                if (!plan.Changes) Assert.All(plan.Farmers, f => Assert.Equal(f.Before, f.After));
                if (plan.Changes && plan.From == WalletMode.Shared)
                {
                    var players = plan.Farmers.Where(f => f.Role == WalletRole.Player).ToList();
                    int share = pot / players.Count;
                    Assert.All(plan.Farmers.Where(f => f.Role == WalletRole.Away), f => Assert.Equal(0, f.After));
                    Assert.All(players.Where(f => f.Id != 1), f => Assert.Equal(share, f.After));
                    Assert.True(plan.Leftover < players.Count);
                    if (!hostPlays) Assert.Equal(plan.Leftover, After(plan, 1));
                }
                // The plan applied, then asked for again, is done.
                if (plan.Changes)
                {
                    var next = Apply(plan, farmers);
                    Assert.False(WalletRules.Plan(next.Mode, wish, next.Pot, next.Farmers).Changes, $"round {round}: applied twice");
                }
            }
        }
    }

    // ---- the wish as it is handed over ----

    [Theory]
    [InlineData("shared", WalletMode.Shared)]
    [InlineData("separate", WalletMode.Separate)]
    [InlineData(" separate ", WalletMode.Separate)]
    public void TheTwoWordsAreTheOnlyWishes(string value, WalletMode mode) => Assert.Equal(mode, WalletRules.ParseWish(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Separate")]
    [InlineData("SHARED")]
    [InlineData("each")]
    [InlineData("1")]
    [InlineData("null")]
    public void AnythingElseIsNoWish(string? value) => Assert.Null(WalletRules.ParseWish(value));

    // ---- the report ----

    private static readonly DateTime When = new(2026, 10, 7, 21, 4, 24, DateTimeKind.Utc);

    [Fact]
    public void TheReportOfASharedFarmShowsThePotAndWhatAWalletEachWouldGive()
    {
        var farmers = new[] { Host(12630), Player(2, "Alice", 12630), Away(3, "Dana", 12630), Player(-4, "Bob", 12630) };
        var report = WalletReport.Build("0.1.28", "450922254", "Squeet Farm", WalletMode.Shared, null, null, "Spring 12", 11, 12631, farmers, null, When);
        using var doc = JsonDocument.Parse(report.ToJson());
        var root = doc.RootElement;
        Assert.Equal("shared-day.wallets/1", root.GetProperty("schema").GetString());
        Assert.Equal("2026-10-07T21:04:24Z", root.GetProperty("writtenUtc").GetString());
        Assert.Equal("0.1.28", root.GetProperty("modVersion").GetString());
        Assert.Equal("450922254", root.GetProperty("farmId").GetString());
        Assert.Equal("shared", root.GetProperty("inForce").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("wanted").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("held").ValueKind);
        Assert.Equal("Spring 12", root.GetProperty("dateLabel").GetString());
        Assert.Equal(11, root.GetProperty("absoluteDay").GetInt32());
        Assert.Equal(12631, root.GetProperty("pot").GetInt32());
        Assert.Equal(0, root.GetProperty("leftover").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("lastChange").ValueKind);
        var listed = root.GetProperty("farmers").EnumerateArray().ToList();
        Assert.Equal(new[] { "host", "player", "away", "player" }, listed.Select(f => f.GetProperty("role").GetString()));
        Assert.Equal(new[] { "1", "2", "3", "-4" }, listed.Select(f => f.GetProperty("id").GetString()));
        // Nobody has a wallet of their own while the pot is shared.
        Assert.All(listed, f => Assert.Equal(JsonValueKind.Null, f.GetProperty("money").ValueKind));
        var preview = root.GetProperty("preview");
        Assert.Equal("separate", preview.GetProperty("to").GetString());
        Assert.Equal(12631, preview.GetProperty("pot").GetInt32());
        Assert.Equal(1, preview.GetProperty("leftover").GetInt32());
        Assert.Equal(new[] { 1, 6315, 0, 6315 }, preview.GetProperty("farmers").EnumerateArray().Select(f => f.GetProperty("gets").GetInt32()));
        // The Away farmer's 0 is there by name, for the owner to read before choosing.
        Assert.Contains(preview.GetProperty("farmers").EnumerateArray(), f => f.GetProperty("name").GetString() == "Dana" && f.GetProperty("gets").GetInt32() == 0);
    }

    [Fact]
    public void TheReportOfAFarmWithAWalletEachShowsEveryWalletAndWhatOneSharedWalletWouldHold()
    {
        var farmers = new[] { Host(2), Player(2, "Alice", 4210), Player(3, "Bob", 100) };
        var report = WalletReport.Build("0.1.28", "450922254", "Squeet Farm", WalletMode.Separate, WalletMode.Shared, WalletPlan.HeldShipping, "Spring 12", 11, 777777, farmers, null, When);
        Assert.Equal("separate", report.InForce);
        Assert.Equal("shared", report.Wanted);
        Assert.Equal("shipping", report.Held);
        Assert.Null(report.Pot);
        Assert.Equal(2, report.Leftover);
        Assert.Equal(new int?[] { 2, 4210, 100 }, report.Farmers.Select(f => f.Money));
        Assert.Equal("shared", report.Preview!.To);
        Assert.Equal(4312, report.Preview.Pot);
        // To one shared wallet, "gets" is what each farmer puts in.
        Assert.Equal(new[] { 2, 4210, 100 }, report.Preview.Farmers.Select(f => f.Gets));
        Assert.Contains("\"pot\": null", report.ToJson());
    }

    [Fact]
    public void AChangeThatWouldBeHeldHasNoPreview()
    {
        Assert.Null(WalletReport.Build("0.1.28", "1", "F", WalletMode.Shared, null, null, "Spring 1", 0, 500, new[] { Host() }, null, When).Preview);
        Assert.Null(WalletReport.Build("0.1.28", "1", "F", WalletMode.Separate, null, null, "Spring 1", 0, 0, new[] { Host(), Player(2, "A", int.MaxValue), Player(3, "B", 9) }, null, When).Preview);
    }

    [Fact]
    public void AChangeIsWrittenDownWithEveryFarmersBeforeAndAfter()
    {
        var farmers = new[] { Host(), Player(2, "Alice"), Player(3, "Bob") };
        var change = WalletChange.Of(WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 12631, farmers), "Spring 12", 11, When);
        Assert.Equal(32, change.Id.Length);
        Assert.Equal("shared", change.From);
        Assert.Equal("separate", change.To);
        Assert.Equal(12631, change.Pot);
        Assert.Equal(1, change.Leftover);
        Assert.Equal("2026-10-07T21:04:24Z", change.Utc);
        Assert.Equal(new[] { 1, 6315, 6315 }, change.Farmers.Select(f => f.After));
        var record = new WalletRecord { LastChange = change, Told = { "2" } };
        var read = WalletRecord.Parse(record.ToJson());
        Assert.Equal(change.Id, read.LastChange!.Id);
        Assert.Equal(new[] { "2" }, read.Told);
        Assert.Equal(new[] { 1, 6315, 6315 }, read.LastChange.Farmers.Select(f => f.After));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void ARecordNobodyCanReadIsNoRecord(string? json)
    {
        var record = WalletRecord.Parse(json);
        Assert.Null(record.LastChange);
        Assert.Empty(record.Told);
    }

    // ---- what players read ----

    [Fact]
    public void AfterASplitEachFarmerReadsTheirOwnNumbers()
    {
        var farmers = new[] { Host(), Player(2, "Alice"), Away(3, "Dana"), Player(4, "Bob"), Player(5, "Cleo") };
        var change = WalletChange.Of(WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 12630, farmers), "Spring 12", 11, When);
        Assert.Equal("This farm changed to a wallet each on Spring 12. You received 4,210g of the 12,630g that was shared.", WalletWords.ForFarmer(change, 2));
        Assert.Equal("This farm changed to a wallet each on Spring 12. You received none of the 12,630g that was shared, because the farm was not waiting for you then.", WalletWords.ForFarmer(change, 3));
        // A farmer made afterwards was no part of it.
        Assert.Null(WalletWords.ForFarmer(change, 99));
    }

    [Fact]
    public void AfterAMergeEachFarmerReadsWhatTheyPutIn()
    {
        var farmers = new[] { Host(), Player(2, "Alice", 4210), Player(3, "Bob", 8420) };
        var change = WalletChange.Of(WalletRules.Plan(WalletMode.Separate, WalletMode.Shared, 0, farmers), "Spring 12", 11, When);
        Assert.Equal("This farm changed to one shared wallet on Spring 12. Your 4,210g went into it; it holds 12,630g.", WalletWords.ForFarmer(change, 2));
    }

    [Fact]
    public void AWishThatWaitsIsSaidInTheSameWordsAsTheLauncher()
    {
        Assert.Equal("This farm is changing to a wallet each. It happens the next time the farm starts.", WalletWords.Waiting(WalletMode.Separate));
        Assert.Equal("This farm is changing to one shared wallet. It happens the next time the farm starts.", WalletWords.Waiting(WalletMode.Shared));
        Assert.Equal("1,234,567g", WalletWords.Gold(1234567));
        Assert.Equal("0g", WalletWords.Gold(0));
    }

    [Fact]
    public void TheLogNamesEveryFarmerWithTheirRoleAndBothNumbers()
    {
        var plan = WalletRules.Plan(WalletMode.Shared, WalletMode.Separate, 12631, new[] { Host(), Player(2, "Alice"), Away(3, "Dana") });
        Assert.Equal(new[]
        {
            "HostPC (host PC's farmer, 1): 0g -> 0g",
            "Alice (player, 2): 0g -> 12,631g",
            "Dana (away, 3): 0g -> 0g"
        }, plan.Farmers.Select(WalletWords.LogLine));
    }
}

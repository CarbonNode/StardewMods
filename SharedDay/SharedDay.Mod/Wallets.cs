using Netcode;
using SharedDay.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Objects;

namespace SharedDay.Mod;

/// <summary>
/// One shared wallet, or a wallet each, chosen by the group's owner in the ModRelay launcher and
/// applied here by the farm itself (docs/wallets-contract.md). The owner, 2026-10-07: "wallet
/// settings would be nice, maybe in that main settings popup with the nice background".
///
/// The game's own switch is the ledger in the Mayor's Manor, which nobody can walk to on a farm
/// that hosts itself, and whose split (ManorHouse.SeparateWallets, 1.6.15) hands the host PC's
/// farmer a share nobody can spend and drops the remainder. The arithmetic here is
/// <see cref="WalletRules"/>; this class only reads the farm, applies a plan, checks it and saves.
///
/// It is inert unless the ModRelay farm-host loop started this game for a wake
/// (<see cref="TitleAutoHost.WokenByLoop"/>), the farm was hosted by itself, this is the host, and
/// the loop gave a report path. Without a report path it does not read a wallet. With a report
/// path and no wish it reads and reports and changes nothing. A change happens at one moment
/// only: when the farm has just been loaded (on the first quiet update tick after every mod has
/// had SaveLoaded and DayStarted, before any farmhand is put into the day), nobody else is
/// connected and no shipping bin holds anything. A setting changed while the farm is up reaches
/// the next game the loop starts.
///
/// Money only ever moves when the mode changes, and the owner read who gets what before choosing
/// that. With a wallet each already in force nothing is moved between wallets, whatever the host
/// PC's farmer holds: it is shown as "leftover" and goes into the pot if the farm changes back.
/// </summary>
internal sealed class Wallets
{
    /// <summary>"shared" or "separate": what the group's owner chose. Unset, or anything else: no wish.</summary>
    public const string WishVariable = "SHAREDDAY_WALLETS";
    /// <summary>An absolute path: where to write what is in force. Unset: this class does nothing at all.</summary>
    public const string ReportVariable = "SHAREDDAY_WALLETS_REPORT";
    /// <summary>What the game gives a new farmer when everyone has a wallet (FarmerTeam.GetMoney).</summary>
    private const int NewFarmerMoney = 500;

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly string modVersion;
    private bool woken;
    private WalletMode? wish;
    private string? reportPath;
    /// <summary>Set once a load found everything in place; cleared at the title screen.</summary>
    private bool armed;
    /// <summary>Why this start did not apply the wish.</summary>
    private string? held;
    private readonly HashSet<long> toldWaiting = new();

    /// <summary>The last change this game made, for the tests. Null when it made none.</summary>
    internal WalletChange? Applied { get; private set; }
    internal string? Held => held;
    internal bool Armed => armed;
    /// <summary>The farm-host loop started this game and gave a report path. False: this class does nothing, and nothing waits for it.</summary>
    internal bool Listening => woken && reportPath is not null;

    public Wallets(IModHelper helper, IMonitor monitor, string modVersion)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.modVersion = modVersion;
        // Read once, at entry: nothing a running game is told later changes what it does.
        Configure(Environment.GetEnvironmentVariable(TitleAutoHost.WokenByLoop) == "1",
            Environment.GetEnvironmentVariable(WishVariable), Environment.GetEnvironmentVariable(ReportVariable));
    }

    /// <summary>What the environment said. Also how a lab hands a game its setting without restarting it.</summary>
    internal void Configure(bool wokenByLoop, string? wishWord, string? path)
    {
        woken = wokenByLoop;
        reportPath = !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? path : null;
        // No report path, no wish: a game that cannot say what it did must not do it.
        wish = reportPath is null ? null : WalletRules.ParseWish(wishWord);
    }

    internal void Reset()
    {
        armed = false;
        held = null;
        Applied = null;
        toldWaiting.Clear();
    }

    /// <summary>
    /// The farm has just been loaded, today's turns read, and every mod has had its load events.
    /// Applies the wish when there is one and it is safe, then writes the report. Never throws:
    /// nothing here may fail the day.
    /// </summary>
    /// <param name="hosted">This launch loaded the farm by itself (AutoHost).</param>
    /// <param name="save">Writes the farm to disk now, or throws.</param>
    internal void AtLoad(bool hosted, bool includeHost, DaySnapshot? day, string dateLabel, Action save)
    {
        armed = false;
        held = null;
        Applied = null;
        toldWaiting.Clear();
        if (!woken || !hosted || !Context.IsMainPlayer || reportPath is null) return;
        armed = true;
        try
        {
            var team = Game1.player.team;
            var inForce = team.useSeparateWallets.Value ? WalletMode.Separate : WalletMode.Shared;
            if (wish is null)
            {
                monitor.Log($"Wallets: this farm uses {WalletWords.ModeName(inForce)}. No change has been asked for in ModRelay.", LogLevel.Info);
                return;
            }
            // The ledger in the Mayor's Manor must not flip the wallets tonight on top of ModRelay's choice.
            if (Game1.player.changeWalletTypeTonight.Value)
            {
                Game1.player.changeWalletTypeTonight.Value = false;
                monitor.Log("Wallets: a change asked for at the Mayor's ledger was cancelled, because ModRelay sets this farm's wallets.", LogLevel.Info);
            }
            var farmers = Gather(day, includeHost, strict: true);
            var plan = WalletRules.Plan(inForce, wish, team.money.Value, farmers);
            if (plan.Held is null && !plan.Changes)
            {
                monitor.Log($"Wallets: this farm already uses {WalletWords.ModeName(inForce)}, as asked for in ModRelay. Nothing was changed.", LogLevel.Info);
                return;
            }
            held = plan.Held ?? HoldReason();
            if (held is not null)
            {
                monitor.Log($"Wallets: not changed. The farm stays on {WalletWords.ModeName(inForce)} for now ({WhyHeld(held)}). No money was touched.", LogLevel.Info);
                return;
            }
            Apply(plan, day, includeHost, dateLabel, save);
        }
        catch (Exception ex)
        {
            held = wish is null ? null : WalletPlan.HeldFailed;
            monitor.Log("Wallets: something went wrong before anything was changed, and the money was left exactly as it was. " + ex.Message, LogLevel.Error);
            monitor.Log(ex.ToString(), LogLevel.Trace);
        }
        finally { Report(day, includeHost, dateLabel); }
    }

    /// <summary>
    /// This start did not get to look at the wallets (the day could not be loaded, or the farm
    /// stayed busy): nothing was tried, no money was read into a plan, and the report says why.
    /// </summary>
    internal void NotTried(bool hosted, bool includeHost, string dateLabel, string reason)
    {
        Reset();
        if (!woken || !hosted || !Context.IsMainPlayer || reportPath is null) return;
        armed = true;
        var inForce = Game1.player.team.useSeparateWallets.Value ? WalletMode.Separate : WalletMode.Shared;
        if (wish is { } wanted && wanted != inForce)
        {
            held = reason;
            monitor.Log($"Wallets: not changed. The farm stays on {WalletWords.ModeName(inForce)} for now ({WhyHeld(reason)}). No money was touched.", LogLevel.Info);
        }
        Report(null, includeHost, dateLabel);
    }

    /// <summary>True when this start has a wish that differs from what the save uses, so a save may follow.</summary>
    internal bool WouldChange => woken && reportPath is not null && wish is { } wanted && Context.IsMainPlayer
        && wanted != (Game1.player.team.useSeparateWallets.Value ? WalletMode.Separate : WalletMode.Shared);

    private void Apply(WalletPlan plan, DaySnapshot? day, bool includeHost, string dateLabel, Action save)
    {
        if (!plan.Conserves) throw new InvalidOperationException("The plan does not add up.");
        var team = Game1.player.team;
        var before = Snapshot.Take();
        if (plan.From == plan.To) throw new InvalidOperationException("Money only moves when the wallets change.");
        string what = $"changing from {WalletWords.ModeName(plan.From)} to {WalletWords.ModeName(plan.To)}";
        try
        {
            var byId = Game1.getAllFarmers().ToDictionary(f => f.UniqueMultiplayerID);
            int emptyCabins = 0;
            if (plan.To == WalletMode.Shared)
            {
                // The flag is still "a wallet each" here, which is when the binding applies.
                WorldCheckpoint.BindOfflineWallets();
                team.useSeparateWallets.Value = false;
                team.money.Value = plan.Pot;
            }
            else
            {
                team.useSeparateWallets.Value = true;
                foreach (var share in plan.Farmers) team.SetIndividualMoney(byId[share.Id], share.After);
                if (plan.From == WalletMode.Shared)
                {
                    // With one shared wallet the game leaves a copy of the pot in every cabin's entry,
                    // empty cabins included. A farmer made later must start with the game's own 500g,
                    // not with a copy of the old pot.
                    foreach (var slot in byId.Values.Where(f => f.UniqueMultiplayerID != Game1.MasterPlayer.UniqueMultiplayerID && f.isUnclaimedFarmhand))
                    {
                        team.SetIndividualMoney(slot, NewFarmerMoney);
                        emptyCabins++;
                    }
                }
                // An offline farmer's wallet is written by the save only once it is tied to the host's.
                WorldCheckpoint.BindOfflineWallets();
            }

            Verify(plan, day, includeHost);
            var change = WalletChange.Of(plan, dateLabel, Game1.Date.TotalDays, DateTime.UtcNow);
            Game1.MasterPlayer.modData[WalletRecord.Key] = new WalletRecord { LastChange = change }.ToJson();
            save();

            Applied = change;
            monitor.Log($"Wallets: {what} on {dateLabel}, as asked for in ModRelay."
                + (plan.To == WalletMode.Shared ? "" : $" {WalletWords.Gold(plan.Pot)} was there to share."), LogLevel.Info);
            foreach (var share in plan.Farmers) monitor.Log("Wallets:   " + WalletWords.LogLine(share), LogLevel.Info);
            if (plan.To == WalletMode.Shared) monitor.Log($"Wallets:   the shared wallet now holds {WalletWords.Gold(plan.Pot)}.", LogLevel.Info);
            if (plan.Leftover > 0) monitor.Log($"Wallets:   {WalletWords.Gold(plan.Leftover)} could not be split equally and stays in the host PC's farmer's wallet. It goes into the pot if the farm changes back to one shared wallet.", LogLevel.Info);
            if (emptyCabins > 0) monitor.Log($"Wallets:   {emptyCabins} empty cabin(s) set to the game's starting {WalletWords.Gold(NewFarmerMoney)} for whoever makes a farmer there.", LogLevel.Info);
            monitor.Log($"Wallets:   every coin counted: {WalletWords.Gold(plan.TotalBefore)} before, {WalletWords.Gold(plan.TotalAfter)} after. Saved.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            before.Restore();
            held = WalletPlan.HeldFailed;
            Applied = null;
            monitor.Log($"Wallets: {what} failed, and the money was put back exactly as it was. " + ex.Message, LogLevel.Error);
            monitor.Log(ex.ToString(), LogLevel.Trace);
        }
    }

    /// <summary>Reads the farm back after the change: the mode, every wallet and the total, to the coin.</summary>
    private void Verify(WalletPlan plan, DaySnapshot? day, bool includeHost)
    {
        var team = Game1.player.team;
        if (team.useSeparateWallets.Value != (plan.To == WalletMode.Separate)) throw new InvalidOperationException("The wallet mode did not change.");
        long total;
        if (plan.To == WalletMode.Shared) total = team.money.Value;
        else
        {
            var now = Gather(day, includeHost, strict: true).ToDictionary(f => f.Id, f => f.Money);
            foreach (var share in plan.Farmers)
                if (!now.TryGetValue(share.Id, out int money) || money != share.After)
                    throw new InvalidOperationException($"{share.Name}'s wallet reads {(now.TryGetValue(share.Id, out int m) ? m : -1)}, not {share.After}.");
            if (now.Count != plan.Farmers.Count) throw new InvalidOperationException("The farmers changed while the wallets were being changed.");
            total = now.Values.Sum(v => (long)v);
        }
        if (total != plan.TotalBefore) throw new InvalidOperationException($"The total reads {total}, not {plan.TotalBefore}.");
    }

    /// <summary>Why nothing may change right now, or null. Money rules of their own (too much, no players) are the plan's.</summary>
    private string? HoldReason()
    {
        if (Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)) return WalletPlan.HeldPlayers;
        return AnythingWaitingToShip() ? WalletPlan.HeldShipping : null;
    }

    /// <summary>
    /// Any item in any shipping bin of either kind. The game pays the bins its mode selects
    /// (Farm.getShippingBin), so items in the other kind would never be paid after a change.
    /// </summary>
    private bool AnythingWaitingToShip()
    {
        static bool Has(IEnumerable<Item>? items) => items is not null && items.Any(i => i is not null);
        var farm = Game1.getFarm();
        // The shared bin is private to the farm, and unreachable through getShippingBin while everyone has a wallet.
        if (Has(helper.Reflection.GetField<NetRef<Inventory>>(farm, "sharedShippingBin").GetValue().Value)) return true;
        if (Game1.getAllFarmers().Any(f => Has(f.personalShippingBin.Value))) return true;
        bool found = false;
        Utility.ForEachLocation(location =>
        {
            foreach (var thing in location.objects.Values)
                if (thing is Chest { SpecialChestType: Chest.SpecialChestTypes.MiniShippingBin } bin
                    && (Has(bin.Items) || bin.separateWalletItems.Values.Any(own => Has(own))))
                {
                    found = true;
                    return false;
                }
            return true;
        });
        return found;
    }

    private static string WhyHeld(string reason) => reason switch
    {
        WalletPlan.HeldShipping => "there are things in a shipping bin from today; it changes at the first start after this farm day ends",
        WalletPlan.HeldPlayers => Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
            ? "a player was already connected" : "nobody has made a farmer on this farm",
        WalletPlan.HeldTooMuch => "the wallets together hold more than the game can keep in one",
        WalletPlan.HeldBusy => "the farm was busy when it started; it tries again the next time it starts",
        _ => "something went wrong"
    };

    /// <summary>
    /// Every claimed farmer with what they are to the money. Empty cabins are left out: they are
    /// nobody's. <paramref name="strict"/> refuses a farmer whose wallet the game has no entry for,
    /// where reading it through the game would quietly make one with 500g in it.
    /// </summary>
    private static List<WalletFarmer> Gather(DaySnapshot? day, bool includeHost, bool strict)
    {
        var team = Game1.player.team;
        bool separate = team.useSeparateWallets.Value;
        long hostId = Game1.MasterPlayer.UniqueMultiplayerID;
        var list = new List<WalletFarmer>();
        foreach (var farmer in Game1.getAllFarmers())
        {
            long id = farmer.UniqueMultiplayerID;
            bool isHost = id == hostId;
            if (!isHost && farmer.isUnclaimedFarmhand) continue;
            int money = 0;
            if (separate)
            {
                if (team.individualMoney.TryGetValue(id, out var wallet)) money = wallet.Value;
                else if (strict) throw new InvalidOperationException($"{farmer.Name} has no wallet the game knows of.");
            }
            var role = isHost && !includeHost ? WalletRole.Host
                : day?.Farmers.FirstOrDefault(f => f.Id == id) is { Away: true } ? WalletRole.Away : WalletRole.Player;
            list.Add(new WalletFarmer(id, string.IsNullOrWhiteSpace(farmer.Name) ? "A farmer" : farmer.Name, role, money, isHost));
        }
        return list;
    }

    private static WalletRecord Record() => WalletRecord.Parse(Game1.MasterPlayer.modData.TryGetValue(WalletRecord.Key, out string json) ? json : null);

    /// <summary>
    /// Writes what is in force to the report file, for the host loop to relay. Called after the
    /// load, after every same-date save and after every night's save, so that while the farm is
    /// asleep the file matches the save on disk. Changes nothing in the game and never throws.
    /// </summary>
    internal void Report(DaySnapshot? day, bool includeHost, string dateLabel)
    {
        if (!armed || reportPath is null || !Context.IsMainPlayer || !Context.IsWorldReady) return;
        try
        {
            var team = Game1.player.team;
            var inForce = team.useSeparateWallets.Value ? WalletMode.Separate : WalletMode.Shared;
            var report = WalletReport.Build(modVersion, Game1.uniqueIDForThisGame.ToString(), Game1.MasterPlayer.farmName.Value ?? "",
                inForce, wish, held, dateLabel, Game1.Date.TotalDays, team.money.Value, Gather(day, includeHost, strict: false), Record().LastChange, DateTime.UtcNow);
            // Whole or not at all: the loop reads this every few seconds and must never see half a file.
            string temporary = reportPath + ".tmp";
            File.WriteAllText(temporary, report.ToJson());
            // The loop may have the old file open for the instant it reads it; Windows then refuses
            // the move. A few short tries, and never a temporary file left lying beside the report.
            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    try { File.Move(temporary, reportPath, overwrite: true); break; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 6) { Thread.Sleep(15); }
                }
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex)
        {
            monitor.LogOnce("Wallets: the report for ModRelay could not be written, so the launcher may show an older state. " + ex.Message, LogLevel.Warn);
        }
    }

    /// <summary>
    /// A farmer has connected and been put into the day. Tells them, once, what the last change
    /// did to their own money, and that a change is still waiting when one is.
    /// </summary>
    /// <returns>True when the save has something new to keep (who has been told).</returns>
    internal bool Joined(long id, Action<long, string> tell)
    {
        if (!armed || !Context.IsMainPlayer) return false;
        // Somebody who is not here cannot be told, and must not be marked as told.
        if (id != Game1.player.UniqueMultiplayerID && !Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == id)) return false;
        bool changed = false;
        try
        {
            var record = Record();
            string key = WalletReport.IdText(id);
            if (record.LastChange is { } change && !record.Told.Contains(key) && WalletWords.ForFarmer(change, id) is { } line)
            {
                tell(id, line);
                record.Told.Add(key);
                Game1.MasterPlayer.modData[WalletRecord.Key] = record.ToJson();
                changed = true;
            }
            var inForce = Game1.player.team.useSeparateWallets.Value ? WalletMode.Separate : WalletMode.Shared;
            if (wish is { } wanted && wanted != inForce && toldWaiting.Add(id)) tell(id, WalletWords.Waiting(wanted));
        }
        catch (Exception ex) { monitor.Log("Wallets: a player could not be told about the farm's wallets. " + ex.Message, LogLevel.Warn); }
        return changed;
    }

    /// <summary>The money as it stood, so a change that fails part way is undone to the coin.</summary>
    private sealed class Snapshot
    {
        private bool separate;
        private int pot;
        private readonly Dictionary<long, int> wallets = new();
        private string? record;

        internal static Snapshot Take()
        {
            var team = Game1.player.team;
            var taken = new Snapshot { separate = team.useSeparateWallets.Value, pot = team.money.Value };
            foreach (long id in team.individualMoney.Keys.ToArray()) taken.wallets[id] = team.individualMoney[id].Value;
            taken.record = Game1.MasterPlayer.modData.TryGetValue(WalletRecord.Key, out string json) ? json : null;
            return taken;
        }

        internal void Restore()
        {
            var team = Game1.player.team;
            team.useSeparateWallets.Value = separate;
            team.money.Value = pot;
            foreach (long id in team.individualMoney.Keys.ToArray())
                if (!wallets.ContainsKey(id)) team.individualMoney.Remove(id);
            foreach (var (id, money) in wallets)
            {
                if (team.individualMoney.TryGetValue(id, out var wallet)) wallet.Value = money;
                else team.individualMoney[id] = new NetIntDelta(money) { Minimum = 0 };
            }
            if (record is null) Game1.MasterPlayer.modData.Remove(WalletRecord.Key);
            else Game1.MasterPlayer.modData[WalletRecord.Key] = record;
        }
    }
}

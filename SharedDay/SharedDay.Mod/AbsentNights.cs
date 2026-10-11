using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace SharedDay.Mod;

/// <summary>One item an absent farmer had in the shipping bin the night it was sold, as the night shown to them later rebuilds it.</summary>
public sealed class NightItem
{
    public string QualifiedItemId { get; set; } = "";
    public int Stack { get; set; }
    public int Quality { get; set; }
    public string DisplayName { get; set; } = "";
}

/// <summary>A level gained in a skill (Farmer.newLevels: X is the skill, Y the level).</summary>
public sealed class SkillLevel
{
    public int Skill { get; set; }
    public int Level { get; set; }
}

/// <summary>
/// One night that ran while a farmer was away (0.1.29): what was in their bin and what it
/// fetched, and the levels they gained. Kept on the host (MasterPlayer.modData, the last seven
/// per farmer) and shown by their own game before their next turn. Display only: the money and
/// the levels themselves are the game's, credited in the real night.
/// </summary>
public sealed class NightRecord
{
    public int Day { get; set; }
    /// <summary>"Alice's farm" when Many Farms says so; null means the farm itself.</summary>
    public string? FarmLabel { get; set; }
    public List<NightItem> Items { get; set; } = new();
    public int Total { get; set; }
    /// <summary>The money went to this farmer's own wallet (a wallet each); false: into the shared one.</summary>
    public bool OwnWallet { get; set; }
    public List<SkillLevel> Levels { get; set; } = new();
}

/// <summary>The host's record of absent farmers' nights, kept in the save with the farm.</summary>
internal static class NightsLedger
{
    internal const string Key = "CarbonNode.SharedDay/Nights";
    internal const string LastShownKey = "CarbonNode.SharedDay/LastNightShown";
    /// <summary>Records kept per farmer; an older one is dropped for a newer.</summary>
    internal const int Kept = 7;

    internal static Dictionary<string, List<NightRecord>> Read()
    {
        if (!Game1.MasterPlayer.modData.TryGetValue(Key, out string json) || string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, List<NightRecord>>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    internal static void Write(Dictionary<string, List<NightRecord>> ledger)
        => Game1.MasterPlayer.modData[Key] = JsonSerializer.Serialize(ledger);

    internal static void Add(Dictionary<string, List<NightRecord>> ledger, long farmerId, NightRecord record)
    {
        string id = farmerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!ledger.TryGetValue(id, out var list)) ledger[id] = list = new List<NightRecord>();
        list.RemoveAll(r => r.Day == record.Day);
        list.Add(record);
        list.Sort((a, b) => a.Day.CompareTo(b.Day));
        if (list.Count > Kept) list.RemoveRange(0, list.Count - Kept);
    }

    /// <summary>The last date whose night this farmer has been shown (their own modData), or -1.</summary>
    internal static int LastShown(Farmer farmer)
        => farmer.modData.TryGetValue(LastShownKey, out string raw) && int.TryParse(raw, out int day) ? day : -1;

    /// <summary>This farmer's records not yet shown to them, oldest first.</summary>
    internal static List<NightRecord> Unshown(Dictionary<string, List<NightRecord>> ledger, Farmer farmer)
    {
        int shown = LastShown(farmer);
        return ledger.TryGetValue(farmer.UniqueMultiplayerID.ToString(System.Globalization.CultureInfo.InvariantCulture), out var list)
            ? list.Where(r => r.Day > shown).OrderBy(r => r.Day).ToList()
            : new List<NightRecord>();
    }
}

/// <summary>
/// The host's half (0.1.29): inside the farm's one real night, every roster farmer who is not
/// connected gets the night a connected farmer's own game would give them, and a record of it to
/// be shown later. It runs from GameLoop.DayEnding on the host, which is before the game's own
/// night work and before its save, so whatever it does is saved with that night: once, or (if the
/// night's save fails) not at all, never twice.
/// </summary>
internal static class AbsentNights
{
    /// <param name="roster">Today's roster, by farmer id.</param>
    /// <param name="hostPlays">The host PC's own farmer is a player; it is connected either way and never credited here.</param>
    /// <param name="timeWentToSleep">The host's clock as the night starts: the late-bed stamina rule reads it.</param>
    /// <param name="farmLabel">Many Farms' name for a farmer's own farm, or null.</param>
    /// <returns>How many farmers were credited.</returns>
    internal static int Credit(IEnumerable<long> roster, bool hostPlays, int day, int timeWentToSleep, Func<long, string?>? farmLabel, IMonitor monitor)
    {
        var online = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();
        var ledger = NightsLedger.Read();
        int credited = 0;
        bool ownWallets = Game1.player.team.useSeparateWallets.Value;
        foreach (long id in roster)
        {
            if (online.Contains(id) || id == Game1.player.UniqueMultiplayerID) continue;
            var farmer = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id);
            if (farmer is null || farmer.isUnclaimedFarmhand) continue;
            try
            {
                var record = Describe(farmer, day, ownWallets, farmLabel);
                // The night itself. Farmer.dayupdate (1.6.15, read in the decompile) touches only
                // the farmer, Utility.getHomeOfFarmer(farmer), the global stats and random, and,
                // for a farmer standing in a house with a shirt and pants on, that house's objects
                // (a cursed mannequin roll). An offline farmhand's copy on the host has no such
                // location, so the direct call is sound and is preferred over copying its list:
                // the game's own order and rules, and nothing left out when the game changes.
                // (verify in 1.6.15: the game's newDayAfterFade runs dayupdate for Game1.player
                // only, never for an offline farmhand, so this is the only night they get)
                farmer.dayupdate(timeWentToSleep);
                NightsLedger.Add(ledger, id, record);
                credited++;
                monitor.Log($"Credited {farmer.Name}'s night for {ModEntry.DateWords(day)} while they are away: {record.Items.Sum(i => i.Stack)} items shipped for {record.Total}g, {record.Levels.Count} level-ups recorded.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Could not credit {farmer.Name}'s night for {ModEntry.DateWords(day)}: {ex.Message} Their counters stay as they were; nothing was written for them.", LogLevel.Warn);
                monitor.Log(ex.ToString(), LogLevel.Trace);
            }
        }
        if (credited > 0) NightsLedger.Write(ledger);
        return credited;
    }

    /// <summary>What this farmer's bin holds and what it will fetch, and the levels waiting for them. Reads only.</summary>
    private static NightRecord Describe(Farmer farmer, int day, bool ownWallets, Func<long, string?>? farmLabel)
    {
        var record = new NightRecord { Day = day, OwnWallet = ownWallets };
        try { record.FarmLabel = farmLabel?.Invoke(farmer.UniqueMultiplayerID); }
        catch (Exception) { record.FarmLabel = null; }
        // With a wallet each this is the farmer's own bin; with one shared wallet it is the farm's,
        // recorded for them to see, and paid once by the night into the shared wallet as always.
        foreach (var item in Game1.getFarm().getShippingBin(farmer))
        {
            if (item is null) continue;
            int quality = item is StardewValley.Object made ? made.Quality : 0;
            record.Items.Add(new NightItem { QualifiedItemId = item.QualifiedItemId, Stack = item.Stack, Quality = quality, DisplayName = item.DisplayName });
            // The price that farmer gets, professions included, as the night will pay it.
            // (verify in 1.6.15: Item.sellToStorePrice(long specificPlayerID) is virtual on Item)
            record.Total += Math.Max(0, item.sellToStorePrice(farmer.UniqueMultiplayerID)) * Math.Max(1, item.Stack);
        }
        foreach (Point level in farmer.newLevels) record.Levels.Add(new SkillLevel { Skill = level.X, Level = level.Y });
        return record;
    }
}

/// <summary>
/// The player's half (0.1.29): the nights that ran without them, shown by their own game before
/// their turn starts. Each record: a short fade with a title line ("Spring 13's night on Squeet
/// Farm"), the game's own shipping menu rebuilt from the record, a level-up menu per recorded
/// level, then a line for anything that could not be rebuilt; then the date is written as shown
/// (the farmer's own modData, which the host saves). Display only: the money and the levels
/// were credited by the host in the real night. A crash before the write shows it again. Never
/// on the host PC's own farmer.
/// </summary>
internal sealed class NightReplay
{
    private readonly IMonitor monitor;
    private readonly IModHelper helper;
    private readonly Action<int> shown;
    private readonly Queue<NightRecord> queue = new();
    private NightRecord? current;
    private int step;
    private int levelIndex;
    private int waitTicks;
    private int foreignMenuTicks;
    private int outroTicks;
    private IClickableMenu? opened;
    private ShippingMenu? shipping;
    private int lost;
    private int lostValue;

    /// <param name="shown">Called with the date once a record has been shown in full.</param>
    internal NightReplay(IMonitor monitor, IModHelper helper, Action<int> shown)
    {
        this.monitor = monitor;
        this.helper = helper;
        this.shown = shown;
    }

    /// <summary>A night is being shown or waits to be.</summary>
    internal bool Busy => current is not null || queue.Count > 0;

    /// <summary>The menu the replay opened itself and the lock must leave alone (a DialogueBox; the game's own menus are left alone by their types).</summary>
    internal IClickableMenu? OwnMenu => opened;

    internal void Queue(IEnumerable<NightRecord> records)
    {
        if (Context.IsMainPlayer) return;
        int already = NightsLedger.LastShown(Game1.player);
        int queued = 0;
        foreach (var record in records.OrderBy(r => r.Day))
        {
            if (record.Day <= already || queue.Any(q => q.Day == record.Day) || current?.Day == record.Day) continue;
            queue.Enqueue(record);
            queued++;
        }
        monitor.Log($"Queued {queued} missed night{(queued == 1 ? "" : "s")} to show (already shown through {(already < 0 ? "none" : ModEntry.DateWords(already))}).", LogLevel.Info);
    }

    internal void Reset()
    {
        queue.Clear();
        current = null;
        opened = null;
        shipping = null;
        step = levelIndex = waitTicks = foreignMenuTicks = outroTicks = lost = lostValue = 0;
    }

    internal void Tick()
    {
        if (!Busy || Context.IsMainPlayer || !Context.IsWorldReady || Game1.eventUp || Game1.newDay) return;
        if (current is null)
        {
            current = queue.Dequeue();
            step = levelIndex = lost = lostValue = 0;
            waitTicks = foreignMenuTicks = 0;
            monitor.Log($"Showing {ModEntry.DateWords(current.Day)}'s night: {current.Items.Count} kinds of item shipped for {current.Total}g, {current.Levels.Count} level-up{(current.Levels.Count == 1 ? "" : "s")}.", LogLevel.Info);
        }
        NightRecord record = current;
        var menu = Game1.activeClickableMenu;
        if (menu is not null)
        {
            // The game's own menus close themselves; a save screen that the shipping menu would run
            // outside the night is closed at once (and told not to save, see GamePatches).
            if (menu is SaveGameMenu) { Game1.activeClickableMenu = null; return; }
            if (menu is LevelUpMenu level && helper.Reflection.GetField<bool>(level, "isActive", required: false)?.GetValue() == false) { Game1.exitActiveMenu(); return; }
            // Once OK was pressed on the shipping menu (its outro), the menu ends through a fade and
            // its embedded save. Outside the night neither is relied on: four seconds after OK the
            // menu is closed here and any fade it left is cleared, so the night goes on.
            // (verify in 1.6.15: ShippingMenu.outro is the field set by okClicked; Game1.globalFade,
            // fadeToBlack, fadeIn and fadeToBlackAlpha are the screen fade's state)
            if (menu is ShippingMenu totals && ReferenceEquals(totals, shipping)
                && helper.Reflection.GetField<bool>(totals, "outro", required: false)?.GetValue() == true && ++outroTicks > 240)
            {
                Game1.activeClickableMenu = null;
                Game1.globalFade = false;
                Game1.fadeToBlack = false;
                Game1.fadeIn = false;
                Game1.fadeToBlackAlpha = 0f;
                shipping = null;
                monitor.Log("The shipping menu did not close itself after OK; closed for it.", LogLevel.Info);
                return;
            }
            // A menu that is neither ours nor the game's night menus is somebody else's; it is logged once a minute, not closed.
            if (menu is not (ShippingMenu or LevelUpMenu) && !ReferenceEquals(menu, opened) && ++foreignMenuTicks % 3600 == 0)
                monitor.Log($"The night cannot be shown while {menu.GetType().FullName} is open.", LogLevel.Info);
            return;
        }
        opened = null;
        // A breath between two menus, so a press that closed one is not read by the next.
        if (++waitTicks < 20) return;
        waitTicks = 0;
        try
        {
            switch (step)
            {
                case 0:
                    // The title line. (A fade here never came back in the lab of 2026-10-08, so none.)
                    Open(new DialogueBox(Title(record)));
                    step = 1;
                    monitor.Log("Night shown, step 1 of 4: the title line.", LogLevel.Debug);
                    return;
                case 1:
                    step = 2;
                    if (record.Items.Count == 0) return;
                    var items = new List<Item>();
                    foreach (var recorded in record.Items)
                    {
                        try
                        {
                            var item = ItemRegistry.Create(recorded.QualifiedItemId, Math.Max(1, recorded.Stack), recorded.Quality);
                            if (item is null) throw new InvalidOperationException("no such item any more");
                            items.Add(item);
                        }
                        catch (Exception ex)
                        {
                            lost += Math.Max(1, recorded.Stack);
                            monitor.Log($"Could not rebuild {recorded.DisplayName} ({recorded.QualifiedItemId}) for {ModEntry.DateWords(record.Day)}'s night: {ex.Message}", LogLevel.Debug);
                        }
                    }
                    if (lost > 0)
                    {
                        // The menu prices what it can show; the rest is said in a line afterwards.
                        int shownValue = 0;
                        foreach (var item in items) shownValue += Math.Max(0, item.sellToStorePrice(Game1.player.UniqueMultiplayerID)) * item.Stack;
                        lostValue = Math.Max(0, record.Total - shownValue);
                    }
                    if (items.Count > 0)
                    {
                        shipping = new ShippingMenu(items);
                        outroTicks = 0;
                        Game1.activeClickableMenu = shipping;
                        monitor.Log($"Night shown, step 2 of 4: the shipping menu with {items.Count} kinds of item.", LogLevel.Debug);
                    }
                    return;
                case 2:
                    if (levelIndex < record.Levels.Count)
                    {
                        var gained = record.Levels[levelIndex++];
                        var point = new Point(gained.Skill, gained.Level);
                        // The game's menu takes its level off this list when OK is pressed; the host
                        // credited the level itself in the real night, so this only lets it be shown.
                        if (!Game1.player.newLevels.Contains(point)) Game1.player.newLevels.Add(point);
                        Game1.activeClickableMenu = new LevelUpMenu(gained.Skill, gained.Level);
                        monitor.Log($"Night shown, step 3 of 4: the level-up menu for skill {gained.Skill} level {gained.Level}.", LogLevel.Debug);
                        return;
                    }
                    step = 3;
                    return;
                case 3:
                    step = 4;
                    if (lost > 0) Open(new DialogueBox($"+ {lost} other item{(lost == 1 ? "" : "s")}, {lostValue:N0}g"));
                    return;
                default:
                    current = null;
                    monitor.Log($"Showed {ModEntry.DateWords(record.Day)}'s night.", LogLevel.Info);
                    shown(record.Day);
                    return;
            }
        }
        catch (Exception ex)
        {
            // A night that cannot be shown is written off as shown: nothing in it is the player's to lose.
            current = null;
            monitor.Log($"Could not show {ModEntry.DateWords(record.Day)}'s night: {ex.Message}", LogLevel.Warn);
            shown(record.Day);
        }
    }

    private void Open(DialogueBox box)
    {
        Game1.activeClickableMenu = box;
        opened = box;
    }

    private static string Title(NightRecord record)
    {
        string where = record.FarmLabel is { Length: > 0 } label ? label : FarmWordsOfFarm();
        string gained = record.Levels.Count == 0 ? "" : record.Levels.Count == 1 ? " One level-up." : $" {record.Levels.Count} level-ups.";
        string sold = record.Items.Count == 0 ? "Nothing was shipped." : $"Shipped for {record.Total:N0}g{(record.OwnWallet ? "" : " into the shared wallet")}.";
        return $"{ModEntry.DateWords(record.Day)}'s night on {where}. {sold}{gained}";
    }

    private static string FarmWordsOfFarm()
        => Game1.MasterPlayer?.farmName.Value is { Length: > 0 } name ? SharedDay.Core.FarmWords.Titled(name) : "the farm";
}

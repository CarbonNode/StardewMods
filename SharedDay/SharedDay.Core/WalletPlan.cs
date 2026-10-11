using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedDay.Core;

/// <summary>The game's two ways to keep a co-op farm's money: one pot for everyone, or a wallet per farmer.</summary>
public enum WalletMode { Shared, Separate }

/// <summary>
/// What a farmer is to the farm's money. A <see cref="Player"/> gets a share when a pot is split.
/// The <see cref="Host"/> is the host PC's own farmer on a farm where nobody plays it
/// (IncludeHost false): it gets no share, and only ever holds the coins that could not be split.
/// An <see cref="Away"/> farmer is one the farm has stopped waiting for (not connected for
/// AwayAfterDays farm days): no share at a split, but every coin they hold goes into a merge.
/// </summary>
public enum WalletRole { Player, Host, Away }

/// <param name="Money">The farmer's own wallet. Only read when a wallet each is in force; with one shared wallet nobody has one.</param>
/// <param name="IsHostFarmer">The host PC's farmer, whatever its role: the coins that cannot be split equally stay with it.</param>
public sealed record WalletFarmer(long Id, string Name, WalletRole Role, int Money, bool IsHostFarmer = false);

/// <summary>One farmer's wallet before and after a change. With one shared wallet a farmer's own wallet counts as 0.</summary>
public sealed record WalletShare(long Id, string Name, WalletRole Role, int Before, int After);

/// <summary>
/// Exactly what a change of wallets does to every coin, worked out before anything is touched.
/// </summary>
/// <param name="Held">Why nothing may change ("players", "too_much", "failed"), or null. The mod adds "shipping" and "busy".</param>
/// <param name="Changes">True when applying this plan moves money or changes the mode. False for a hold and for "already as wished".</param>
/// <param name="Pot">A split: the shared pot that is shared out. A merge: the shared pot afterwards.</param>
/// <param name="Leftover">Coins that stay in the host farmer's wallet because they could not be split equally (only when nobody plays that farmer).</param>
/// <param name="TotalBefore">Every coin on the farm before. Always equals <paramref name="TotalAfter"/>.</param>
public sealed record WalletPlan(
    WalletMode From,
    WalletMode To,
    string? Held,
    bool Changes,
    int Pot,
    int Leftover,
    IReadOnlyList<WalletShare> Farmers,
    long TotalBefore,
    long TotalAfter)
{
    public const string HeldShipping = "shipping", HeldPlayers = "players", HeldTooMuch = "too_much", HeldFailed = "failed", HeldBusy = "busy";

    /// <summary>No coin is made or lost. A plan that does not conserve must never be applied.</summary>
    public bool Conserves => TotalBefore == TotalAfter;
}

/// <summary>
/// The money rules of a wallet change, as pure arithmetic (docs/wallets-contract.md).
///
/// The game's own switch (the ledger in the Mayor's Manor, <c>ManorHouse.SeparateWallets</c> and
/// <c>MergeWallets</c> in 1.6.15) divides the pot by every farmer including the host PC's and
/// farmers who have left, drops the remainder of the division, and sums a merge in a 32-bit
/// number with no check. On a farm whose host nobody plays that hands a share to a farmer who can
/// never spend it. These rules are Shared Day's own:
///
/// - Splitting: the pot is divided among the players only. Each gets floor(pot / players). The
///   coins left over (fewer than there are players) stay in the host farmer's wallet, an Away
///   farmer gets 0, and the host farmer gets nothing else unless it is a player itself.
/// - Merging: every farmer's wallet goes in, players, Away farmers and the host farmer alike.
/// - A wallet each already in force and wished for: nothing moves. Whatever the host farmer holds
///   (the coins a split could not divide, anything earned by someone sitting at the host PC) stays
///   in that wallet, is shown as "leftover", and goes into the pot at a merge. Shared Day never
///   moves money between wallets without the owner having read who gets what first.
/// - Every coin is counted before and after, and the two totals are equal or nothing happens.
/// </summary>
public static class WalletRules
{
    public const string SharedWord = "shared", SeparateWord = "separate";

    /// <summary>The wish as the host loop hands it over. Anything but the two exact words is no wish.</summary>
    public static WalletMode? ParseWish(string? value) => value?.Trim() switch
    {
        SharedWord => WalletMode.Shared,
        SeparateWord => WalletMode.Separate,
        _ => null
    };

    public static string Word(WalletMode mode) => mode == WalletMode.Shared ? SharedWord : SeparateWord;

    public static WalletMode Other(WalletMode mode) => mode == WalletMode.Shared ? WalletMode.Separate : WalletMode.Shared;

    /// <param name="inForce">What the save uses now.</param>
    /// <param name="wish">What the group's owner chose, or null to leave the farm as it is.</param>
    /// <param name="pot">The shared pot. Only read when one shared wallet is in force.</param>
    /// <param name="farmers">Every claimed farmer, the host PC's included. Empty cabins are never listed.</param>
    public static WalletPlan Plan(WalletMode inForce, WalletMode? wish, int pot, IReadOnlyList<WalletFarmer> farmers)
    {
        ArgumentNullException.ThrowIfNull(farmers);
        var to = wish ?? inForce;
        bool shared = inForce == WalletMode.Shared;
        // With one shared wallet a farmer's own wallet is not money: it counts as 0 here.
        var asIs = farmers.Select(f => new WalletShare(f.Id, f.Name, f.Role, shared ? 0 : f.Money, shared ? 0 : f.Money)).ToArray();
        long before = shared ? pot : farmers.Sum(f => (long)f.Money);
        WalletPlan Same(string? held = null) => new(inForce, to, held, false, shared ? pot : 0,
            shared ? 0 : HostLeftover(farmers), asIs, before, before);

        // Anything that is not a plain, countable farm is left exactly as it is.
        if (farmers.Select(f => f.Id).Distinct().Count() != farmers.Count || farmers.Count(f => f.IsHostFarmer) > 1
            || (shared ? pot < 0 : farmers.Any(f => f.Money < 0)))
            return Same(wish is null ? null : WalletPlan.HeldFailed);
        if (wish is null) return Same();

        var players = farmers.Where(f => f.Role == WalletRole.Player).ToList();
        if (shared && to == WalletMode.Separate) return Split(pot, farmers, players, Same);
        if (!shared && to == WalletMode.Shared)
        {
            // 64-bit, because the game's own merge overflows without a word.
            if (before > int.MaxValue) return Same(WalletPlan.HeldTooMuch);
            var into = farmers.Select(f => new WalletShare(f.Id, f.Name, f.Role, f.Money, 0)).ToArray();
            return new(inForce, to, null, true, (int)before, 0, into, before, before);
        }
        return Same();
    }

    /// <summary>What changing to the other mode would do right now, for the owner to read before choosing it.</summary>
    public static WalletPlan Preview(WalletMode inForce, int pot, IReadOnlyList<WalletFarmer> farmers) => Plan(inForce, Other(inForce), pot, farmers);

    private static int HostLeftover(IReadOnlyList<WalletFarmer> farmers) => farmers.FirstOrDefault(f => f.IsHostFarmer && f.Role != WalletRole.Player)?.Money ?? 0;

    private static WalletPlan Split(int pot, IReadOnlyList<WalletFarmer> farmers, List<WalletFarmer> players, Func<string?, WalletPlan> same)
    {
        if (players.Count == 0) return same(WalletPlan.HeldPlayers);
        int share = pot / players.Count, remainder = pot - share * players.Count;
        // The remainder must have a wallet to stay in. Every real farm has a host farmer; a list
        // without one keeps it with the first player rather than dropping a coin.
        long keeper = farmers.FirstOrDefault(f => f.IsHostFarmer)?.Id ?? players[0].Id;
        var after = farmers.Select(f => new WalletShare(f.Id, f.Name, f.Role, 0,
            (f.Role == WalletRole.Player ? share : 0) + (f.Id == keeper ? remainder : 0))).ToArray();
        bool unplayedHost = farmers.Any(f => f.Id == keeper && f.Role != WalletRole.Player);
        return new(WalletMode.Shared, WalletMode.Separate, null, true, pot, unplayedHost ? remainder : 0, after, pot, after.Sum(f => (long)f.After));
    }
}

// ---- what is written down: the report file, and the record kept in the save ----

/// <summary>One farmer in the report. <see cref="Money"/> is null with one shared wallet.</summary>
public sealed class WalletReportFarmer
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "player";
    [JsonPropertyName("money")] public int? Money { get; set; }
}

/// <summary>What a farmer would get (to a wallet each), or would put in (to one shared wallet).</summary>
public sealed class WalletPreviewFarmer
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("gets")] public int Gets { get; set; }
}

public sealed class WalletPreview
{
    [JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonPropertyName("pot")] public int Pot { get; set; }
    [JsonPropertyName("leftover")] public int Leftover { get; set; }
    [JsonPropertyName("farmers")] public List<WalletPreviewFarmer> Farmers { get; set; } = new();
}

public sealed class WalletChangeFarmer
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("before")] public int Before { get; set; }
    [JsonPropertyName("after")] public int After { get; set; }
}

/// <summary>A change Shared Day made, kept in the save beside the money it moved.</summary>
public sealed class WalletChange
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonPropertyName("dateLabel")] public string DateLabel { get; set; } = "";
    [JsonPropertyName("absoluteDay")] public int AbsoluteDay { get; set; }
    [JsonPropertyName("utc")] public string Utc { get; set; } = "";
    [JsonPropertyName("pot")] public int Pot { get; set; }
    [JsonPropertyName("leftover")] public int Leftover { get; set; }
    [JsonPropertyName("farmers")] public List<WalletChangeFarmer> Farmers { get; set; } = new();

    public static WalletChange Of(WalletPlan plan, string dateLabel, int absoluteDay, DateTime utc) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        From = WalletRules.Word(plan.From),
        To = WalletRules.Word(plan.To),
        DateLabel = dateLabel,
        AbsoluteDay = absoluteDay,
        Utc = WalletReport.Time(utc),
        Pot = plan.Pot,
        Leftover = plan.Leftover,
        Farmers = plan.Farmers.Select(f => new WalletChangeFarmer { Id = WalletReport.IdText(f.Id), Name = f.Name, Before = f.Before, After = f.After }).ToList()
    };
}

/// <summary>
/// What Shared Day keeps in the save (the host farmer's modData, key <see cref="Key"/>): the last
/// change, and which farmers have been told about it. Written by the same save as the money.
/// </summary>
public sealed class WalletRecord
{
    public const string Key = "CarbonNode.SharedDay/Wallets";
    [JsonPropertyName("lastChange")] public WalletChange? LastChange { get; set; }
    [JsonPropertyName("told")] public List<string> Told { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Never throws: a record nobody can read is no record, and the farm goes on.</summary>
    public static WalletRecord Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<WalletRecord>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}

/// <summary>
/// The file the farm-host loop reads and relays to ModRelay (schema "shared-day.wallets/1",
/// docs/wallets-contract.md). It says what is in force on the save, never what somebody wishes.
/// </summary>
public sealed class WalletReport
{
    public const string SchemaName = "shared-day.wallets/1";

    [JsonPropertyName("schema")] public string Schema { get; set; } = SchemaName;
    [JsonPropertyName("writtenUtc")] public string WrittenUtc { get; set; } = "";
    [JsonPropertyName("modVersion")] public string ModVersion { get; set; } = "";
    [JsonPropertyName("farmId")] public string FarmId { get; set; } = "";
    [JsonPropertyName("farmName")] public string FarmName { get; set; } = "";
    [JsonPropertyName("inForce")] public string InForce { get; set; } = "";
    [JsonPropertyName("wanted")] public string? Wanted { get; set; }
    [JsonPropertyName("held")] public string? Held { get; set; }
    [JsonPropertyName("dateLabel")] public string DateLabel { get; set; } = "";
    [JsonPropertyName("absoluteDay")] public int AbsoluteDay { get; set; }
    [JsonPropertyName("pot")] public int? Pot { get; set; }
    [JsonPropertyName("leftover")] public int Leftover { get; set; }
    [JsonPropertyName("farmers")] public List<WalletReportFarmer> Farmers { get; set; } = new();
    [JsonPropertyName("preview")] public WalletPreview? Preview { get; set; }
    [JsonPropertyName("lastChange")] public WalletChange? LastChange { get; set; }

    public static string Time(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    public static string IdText(long id) => id.ToString(CultureInfo.InvariantCulture);
    public static string RoleWord(WalletRole role) => role switch { WalletRole.Host => "host", WalletRole.Away => "away", _ => "player" };

    /// <param name="pot">The shared pot; only used with one shared wallet in force.</param>
    /// <param name="held">Why this start did not apply the wish, or null.</param>
    public static WalletReport Build(string modVersion, string farmId, string farmName, WalletMode inForce, WalletMode? wanted, string? held,
        string dateLabel, int absoluteDay, int pot, IReadOnlyList<WalletFarmer> farmers, WalletChange? lastChange, DateTime utc)
    {
        bool shared = inForce == WalletMode.Shared;
        var preview = WalletRules.Preview(inForce, pot, farmers);
        return new WalletReport
        {
            WrittenUtc = Time(utc),
            ModVersion = modVersion,
            FarmId = farmId,
            FarmName = farmName,
            InForce = WalletRules.Word(inForce),
            Wanted = wanted is { } wish ? WalletRules.Word(wish) : null,
            Held = held,
            DateLabel = dateLabel,
            AbsoluteDay = absoluteDay,
            Pot = shared ? pot : null,
            Leftover = shared ? 0 : farmers.FirstOrDefault(f => f.IsHostFarmer && f.Role == WalletRole.Host)?.Money ?? 0,
            Farmers = farmers.Select(f => new WalletReportFarmer { Id = IdText(f.Id), Name = f.Name, Role = RoleWord(f.Role), Money = shared ? null : f.Money }).ToList(),
            // A change that would be held (nobody to share with, too much for one wallet) has no numbers to show.
            Preview = preview.Held is not null || !preview.Changes ? null : new WalletPreview
            {
                To = WalletRules.Word(preview.To),
                Pot = preview.Pot,
                Leftover = preview.Leftover,
                // To a wallet each: what each farmer gets. To one shared wallet: what each puts in.
                Farmers = preview.Farmers.Select(f => new WalletPreviewFarmer { Id = IdText(f.Id), Name = f.Name, Gets = shared ? f.After : f.Before }).ToList()
            },
            LastChange = lastChange
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>What players read in the game's chat about the farm's wallets. The launcher's words for the same facts are the same.</summary>
public static class WalletWords
{
    public static string ModeName(string word) => word == WalletRules.SeparateWord ? "a wallet each" : "one shared wallet";
    public static string ModeName(WalletMode mode) => ModeName(WalletRules.Word(mode));

    /// <summary>"12,630g".</summary>
    public static string Gold(long amount) => amount.ToString("#,0", CultureInfo.InvariantCulture) + "g";

    /// <summary>
    /// The one line for one farmer about the last change, with their own numbers. Null for a
    /// farmer the change did not touch (made afterwards, or not a farmer of this farm).
    /// </summary>
    public static string? ForFarmer(WalletChange change, long farmerId)
    {
        var mine = change.Farmers.FirstOrDefault(f => f.Id == WalletReport.IdText(farmerId));
        if (mine is null) return null;
        if (change.From == WalletRules.SharedWord && change.To == WalletRules.SeparateWord)
            return mine.After > 0
                ? $"This farm changed to a wallet each on {change.DateLabel}. You received {Gold(mine.After)} of the {Gold(change.Pot)} that was shared."
                : $"This farm changed to a wallet each on {change.DateLabel}. You received none of the {Gold(change.Pot)} that was shared, because the farm was not waiting for you then.";
        if (change.From == WalletRules.SeparateWord && change.To == WalletRules.SharedWord)
            return $"This farm changed to one shared wallet on {change.DateLabel}. Your {Gold(mine.Before)} went into it; it holds {Gold(change.Pot)}.";
        return null;
    }

    /// <summary>A wish the running farm could not apply: said to each farmer as they join.</summary>
    public static string Waiting(WalletMode wish) => $"This farm is changing to {ModeName(wish)}. It happens the next time the farm starts.";

    /// <summary>The host's log line for one farmer of a change: exactly what it did to whom.</summary>
    public static string LogLine(WalletShare f) => $"{f.Name} ({RoleNoun(f.Role)}, {WalletReport.IdText(f.Id)}): {Gold(f.Before)} -> {Gold(f.After)}";

    private static string RoleNoun(WalletRole role) => role switch { WalletRole.Host => "host PC's farmer", WalletRole.Away => "away", _ => "player" };
}

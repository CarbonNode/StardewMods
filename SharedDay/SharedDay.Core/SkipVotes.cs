namespace SharedDay.Core;

/// <summary>What the host remembers of today's votes, so they outlive the game being closed and woken again.</summary>
public sealed class SkipVoteState
{
    public string SaveId { get; set; } = "";
    public int Day { get; set; }
    /// <summary>The farmer a vote is about, then everyone who voted for it.</summary>
    public Dictionary<long, long[]> Votes { get; set; } = new();
}

public enum SkipVoteResult
{
    /// <summary>Not counted: the wrong day, a farmer who is here or has played, or a voter with no say.</summary>
    Refused,
    /// <summary>Counted, and more are needed.</summary>
    Counted,
    /// <summary>Counted, and that makes a majority: the farmer sits today out.</summary>
    Passed
}

/// <summary>
/// The farm's vote to let an absent farmer sit today out. Only the host could skip someone, and a
/// farm on an unattended host has nobody at it, so one farmer who never shows held the date for
/// good. Any farmer on today's roster may now vote; the skip passes when more than half of the
/// OTHER farmers agree (the owner, 2026-10-06: "maybe group vote?").
///
/// Who may be voted on: a farmer who has not played today and is not in the farm right now, the
/// same farmers the host's own skip offers. Who has a say: every other farmer on today's roster
/// who is not sitting the day out, in the farm or not, so a vote cast before leaving still counts
/// when the next farmer adds theirs. A vote is dropped when its farmer comes back or the day ends.
/// </summary>
public sealed class SkipVoteBook
{
    private readonly Dictionary<long, HashSet<long>> votes = new();

    public int Day { get; private set; } = -1;

    /// <summary>More than half of those with a say; one is enough when there is only one other farmer.</summary>
    public static int Needed(int electorate) => Math.Max(1, electorate / 2 + 1);

    public static bool CanBeSkipped(FarmerSnapshot farmer) => farmer.Status == FarmerStatus.Pending && !farmer.IsOnline;

    /// <summary>Everyone with a say on skipping <paramref name="target"/>. A group member with no farmer yet (0.1.37) has none: nobody is there to cast it.</summary>
    public static IReadOnlyList<long> Electorate(IEnumerable<FarmerSnapshot> roster, long target) =>
        roster.Where(f => f.Id != target && f.Status != FarmerStatus.Skipped && !f.IsPlaceholder).Select(f => f.Id).ToArray();

    public SkipVoteResult Cast(int day, long voter, long target, IReadOnlyCollection<FarmerSnapshot> roster)
    {
        Prune(day, roster);
        var farmer = roster.FirstOrDefault(f => f.Id == target);
        if (farmer is null || !CanBeSkipped(farmer) || !Electorate(roster, target).Contains(voter)) return SkipVoteResult.Refused;
        if (!votes.TryGetValue(target, out var voters)) votes[target] = voters = new HashSet<long>();
        voters.Add(voter);
        var (cast, needed, _) = Tally(target, roster);
        return cast >= needed ? SkipVoteResult.Passed : SkipVoteResult.Counted;
    }

    /// <summary>Votes that count right now for skipping <paramref name="target"/>, and how many it takes.</summary>
    public (int Votes, int Needed, long[] Voters) Tally(long target, IReadOnlyCollection<FarmerSnapshot> roster)
    {
        var electorate = Electorate(roster, target);
        var counted = votes.TryGetValue(target, out var voters) ? voters.Where(electorate.Contains).OrderBy(v => v).ToArray() : Array.Empty<long>();
        return (counted.Length, Needed(electorate.Count), counted);
    }

    /// <summary>A new day starts clean, and a farmer who came back or has played is no longer voted on.</summary>
    public void Prune(int day, IReadOnlyCollection<FarmerSnapshot> roster)
    {
        if (day != Day) { votes.Clear(); Day = day; }
        foreach (long target in votes.Keys.ToArray())
            if (roster.FirstOrDefault(f => f.Id == target) is not { } farmer || !CanBeSkipped(farmer)) votes.Remove(target);
    }

    public void Forget(long target) => votes.Remove(target);

    public SkipVoteState Export(string saveId) => new()
    {
        SaveId = saveId,
        Day = Day,
        Votes = votes.Where(v => v.Value.Count > 0).ToDictionary(v => v.Key, v => v.Value.OrderBy(x => x).ToArray())
    };

    /// <summary>Votes from another farm or another day are not this farm's votes.</summary>
    public static SkipVoteBook Restore(SkipVoteState? state, string saveId, int day)
    {
        var book = new SkipVoteBook { Day = day };
        if (state is null || state.Day != day || !StringComparer.Ordinal.Equals(state.SaveId, saveId) || state.Votes is null) return book;
        foreach (var (target, voters) in state.Votes)
            if (voters is { Length: > 0 }) book.votes[target] = new HashSet<long>(voters);
        return book;
    }
}

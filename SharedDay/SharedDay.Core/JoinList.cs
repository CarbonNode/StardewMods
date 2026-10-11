namespace SharedDay.Core;

/// <summary>One farmer the host offers on the join screen, in the order the host sent it.</summary>
/// <param name="Made">Somebody has made this farmer (it has a name and a face). False for an empty cabin.</param>
/// <param name="AnotherPlayers">The game marks it as another player's and will not let this one pick it.</param>
public readonly record struct JoinRow(bool Made, bool AnotherPlayers);

/// <summary>What the join screen shows for one farm: which of the host's rows, in what order.</summary>
/// <param name="Rows">
/// Top to bottom: indexes into the host's list, and at most one <see cref="JoinList.NoRoomRow"/>,
/// which is no row of the host's but the line that says no cabin is free.
/// </param>
/// <param name="NewFarmerRow">The host's row that stands for "make a new farmer", or -1 when no cabin is free.</param>
/// <param name="FreeCabins">How many empty cabins that one row stands for.</param>
public sealed record JoinListPlan(IReadOnlyList<int> Rows, int NewFarmerRow, int FreeCabins);

/// <summary>
/// The co-op join screen, made plain. The game lists one row per cabin: a farmer somebody has made
/// shows with its picture, name and play time, and every empty cabin is a bare "New Farmer" row, so
/// a farm with two free cabins greets a first-time joiner with two rows that read the same and no
/// picture on either ("why did it show new farmer twice", the owner, 2026-10-07). Which empty cabin is
/// picked makes no difference to the player.
///
/// So the host's rows are only re-ordered and thinned, never invented: the farmers this player can
/// pick come first, then one row for making a new farmer (the first empty cabin the host listed,
/// which is the row the top "New Farmer" was), then farmers that belong to other players, greyed by
/// the game as before. Picking the new-farmer row is picking that first empty cabin, exactly as today.
///
/// Two things follow from what the game can know. On a farm joined by its address (not through
/// Steam or GOG) the game cannot tell whose farmer is whose, so every made farmer counts as one
/// this player can pick: the new-farmer row is therefore never lower than the last row of the
/// first page, however many farmers stand above it, so a first-time joiner sees it without
/// scrolling. And when no cabin is free, one line that cannot be picked stands where the
/// new-farmer row would be and says so, instead of a list that silently offers only other
/// people's farmers.
/// </summary>
public static class JoinList
{
    public const string NewFarmerTitle = "Make a new farmer";
    public const string NoRoomTitle = "No room for a new farmer";
    public const string NoRoomDetail = "Every cabin is taken. Ask the host to build one.";
    public const string NoRoomHover = "Every cabin on this farm has a farmer already. Ask the host to build another cabin, then join again.";

    /// <summary>In <see cref="JoinListPlan.Rows"/>: the line that says no cabin is free. Not a row of the host's.</summary>
    public const int NoRoomRow = -1;

    /// <summary>How many rows the game shows at once.</summary>
    public const int PageRows = 4;

    public static JoinListPlan Plan(IReadOnlyList<JoinRow> hostRows)
    {
        var mine = new List<int>();
        var others = new List<int>();
        int firstFree = -1, free = 0;
        for (int i = 0; i < hostRows.Count; i++)
        {
            if (!hostRows[i].Made)
            {
                if (firstFree < 0) firstFree = i;
                free++;
            }
            else if (hostRows[i].AnotherPlayers) others.Add(i);
            else mine.Add(i);
        }
        var rows = new List<int>(mine);
        // An empty list stays the game's own (it says "no slots" itself).
        if (hostRows.Count > 0)
            rows.Insert(Math.Min(mine.Count, PageRows - 1), firstFree >= 0 ? firstFree : NoRoomRow);
        rows.AddRange(others);
        return new JoinListPlan(rows, firstFree, free);
    }

    /// <summary>The second line of the new-farmer row, where a made farmer shows the farm's date.</summary>
    public static string CabinsFree(int count) => count == 1 ? "1 cabin free" : $"{Math.Max(count, 0)} cabins free";

    /// <summary>
    /// The farm's name on a row. The game writes "{name} Farm" in its own language, which doubles
    /// the word for a farm already called "Squeet Farm". When the game's line is the name with a
    /// second "Farm" after it, the name alone is shown; anything else is the game's line untouched.
    /// </summary>
    /// <param name="farmName">The name as the host typed it.</param>
    /// <param name="gameLine">What the game would draw for it.</param>
    public static string FarmTitle(string? farmName, string gameLine)
    {
        string name = (farmName ?? "").Trim();
        if (name.Length == 0) return gameLine;
        int lastWord = name.LastIndexOf(' ') + 1;
        string word = name[lastWord..];
        // Only a doubled word is dropped: "Farmville Farm" and "Granja Squeet" stay as the game has them.
        return string.Equals(gameLine.Trim(), name + " " + word, StringComparison.OrdinalIgnoreCase) ? name : gameLine;
    }
}

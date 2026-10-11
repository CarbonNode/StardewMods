namespace ManyFarms.Core;

/// <summary>
/// "The farm this call is about". The game asks "where is the farm?" in a hundred places and
/// always answers with the main farm. Most of those must stay so (shipping, the save, Shared Day's
/// checkpoint), so there is no standing answer "my farm": a scope is opened for the length of one
/// named call and closed when that call ends, whatever happens in it, and only code that was
/// rewritten to ask the scope ever sees it.
/// </summary>
/// <typeparam name="T">What is scoped (the mod scopes a farm).</typeparam>
public sealed class ScopeStack<T> where T : class
{
    private readonly List<T?> entered = new();

    /// <summary>How many scopes are open. Zero between calls: anything else is a scope that was not closed.</summary>
    public int Depth => entered.Count;

    /// <summary>What the innermost open scope is about, or null when none is open or it is about nothing.</summary>
    public T? Current => entered.Count == 0 ? null : entered[^1];

    /// <summary>Opens a scope about this (null opens one about nothing, which hides an outer scope).</summary>
    /// <returns>What to hand to <see cref="Exit"/>.</returns>
    public int Enter(T? about)
    {
        entered.Add(about);
        return entered.Count;
    }

    /// <summary>
    /// Closes the scope <see cref="Enter"/> opened and every scope opened inside it that was left
    /// open. Closing one that is already closed (or was never opened: token 0) does nothing.
    /// </summary>
    public void Exit(int token)
    {
        if (token <= 0 || token > entered.Count) return;
        entered.RemoveRange(token - 1, entered.Count - (token - 1));
    }

    /// <summary>Closes everything. Returns how many scopes were still open, which should be none.</summary>
    public int Reset()
    {
        int left = entered.Count;
        entered.Clear();
        return left;
    }
}

/// <summary>
/// Rewrites named places in one method's instructions and leaves everything else alone. The mod
/// uses it on the game's code (a call to "the farm" becomes a call to "the farm this call is
/// about"); kept free of the game so the finding and replacing can be tested.
/// </summary>
public static class LexicalRewrite
{
    /// <summary>How many instructions match.</summary>
    public static int Count<T>(IEnumerable<T> code, Func<T, bool> match) => code.Count(match);

    /// <summary>Replaces every matching instruction. Returns how many were replaced.</summary>
    public static int ReplaceEach<T>(IList<T> code, Func<T, bool> match, Func<T, T> replacement)
    {
        int done = 0;
        for (int i = 0; i < code.Count; i++)
        {
            if (!match(code[i])) continue;
            code[i] = replacement(code[i]);
            done++;
        }
        return done;
    }

    /// <summary>How many times one instruction is directly followed by another.</summary>
    public static int CountPairs<T>(IReadOnlyList<T> code, Func<T, bool> first, Func<T, bool> second)
    {
        int found = 0;
        for (int i = 0; i + 1 < code.Count; i++)
            if (first(code[i]) && second(code[i + 1])) { found++; i++; }
        return found;
    }

    /// <summary>
    /// Replaces every pair "first directly followed by second" with two instructions made from
    /// them (so whatever jumps to either still lands on something). Returns how many pairs were replaced.
    /// </summary>
    public static int ReplacePairs<T>(IList<T> code, Func<T, bool> first, Func<T, bool> second, Func<T, T> newFirst, Func<T, T> newSecond)
    {
        int done = 0;
        for (int i = 0; i + 1 < code.Count; i++)
        {
            if (!first(code[i]) || !second(code[i + 1])) continue;
            code[i] = newFirst(code[i]);
            code[i + 1] = newSecond(code[i + 1]);
            done++;
            i++;
        }
        return done;
    }
}

/// <summary>Repeats, for every started plot, something the game does for its one farm only.</summary>
public static class EachFarmRule
{
    /// <summary>
    /// Does the work for each item in order. One that fails is reported and the rest still get
    /// theirs: a storm that cannot strike one farm still strikes the others.
    /// </summary>
    /// <returns>How many were done without failing.</returns>
    public static int Run<T>(IEnumerable<T> farms, Action<T> work, Action<T, Exception> failed)
    {
        int done = 0;
        foreach (var farm in farms)
        {
            try
            {
                work(farm);
                done++;
            }
            catch (Exception ex)
            {
                failed(farm, ex);
            }
        }
        return done;
    }
}

/// <summary>
/// Where the game's "move this animal to another building" view looks. The game always shows the
/// same corner of its own farm (pixel 3136, 320); for an animal that lives on a plot the view
/// opens on the building it lives in.
/// </summary>
public static class AnimalView
{
    /// <summary>The top-left pixel of a view centred on a building, kept on the map.</summary>
    /// <param name="building">The building's tile and size in tiles.</param>
    public static (int X, int Y) Origin(TilePoint tile, TilePoint size, int viewWidth, int viewHeight, int mapWidthTiles, int mapHeightTiles)
    {
        int centreX = tile.X * 64 + size.X * 32;
        int centreY = tile.Y * 64 + size.Y * 32;
        return (Clamp(centreX - viewWidth / 2, mapWidthTiles * 64 - viewWidth), Clamp(centreY - viewHeight / 2, mapHeightTiles * 64 - viewHeight));
    }

    // A map smaller than the view is shown from its own corner.
    private static int Clamp(int value, int max) => Math.Max(0, Math.Min(value, Math.Max(0, max)));
}

/// <summary>The plots on the game's world map: each is entered under the farm's own area, by a name of its own.</summary>
public static class WorldMapIds
{
    public const string Prefix = "CarbonNode.ManyFarms_Plot";

    /// <summary>The id of a plot's entry in Data/WorldMap.</summary>
    public static string For(int slot) => Prefix + FarmSlots.FarmName(slot).Substring(FarmSlots.Prefix.Length);

    /// <summary>The plot an entry of the world map is for, when it is one of this mod's.</summary>
    public static bool TryPlot(string? id, out int slot)
    {
        slot = 0;
        return id is not null && id.StartsWith(Prefix, StringComparison.Ordinal) && FarmSlots.TryFarmSlot(FarmSlots.Prefix + id.Substring(Prefix.Length), out slot);
    }

    /// <summary>The locations a plot's entry covers: the plot and its cave.</summary>
    public static IReadOnlyList<string> Locations(int slot) => new[] { FarmSlots.FarmName(slot), FarmSlots.CaveName(slot) };
}

/// <summary>
/// Whose kind of farm decides what a farmer finds where the game asks "what kind of farm is this
/// save?" about the farmer and not about a place (a crate broken in the mines holds deluxe
/// retaining soil for a Beach farmer, who cannot use sprinklers, and an omni geode for anyone else).
/// </summary>
public static class FarmerFinds
{
    /// <summary>The location name of the farm whose kind counts for this farmer: their own started plot, else the main farm.</summary>
    public static string FarmFor(string? ownStartedPlot) => ownStartedPlot ?? FarmSlots.MainFarm;
}

using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ManyFarms.Core;
using StardewModdingAPI;
using StardewValley;

namespace ManyFarms.Mod;

/// <summary>
/// The farm one call of the game's is about. The location named "Farm" is the main farm
/// everywhere, always: there is no standing "my farm", on the host (one game serves every
/// player) or on a player's PC (shipping, the save, the mailbox and Shared Day's checkpoint all
/// ask for "the farm" and must get the main one). A scope is opened by a prefix on one named
/// method and closed by a finalizer on that same method; only code this mod rewrote to ask
/// <see cref="FarmOrMain"/> sees it. Never held across an iterator, a save, a network receive, a
/// building's update or a menu's lifetime.
/// </summary>
internal static class FarmScope
{
    [ThreadStatic] private static ScopeStack<Farm>? stack;

    private static ScopeStack<Farm> Stack => stack ??= new ScopeStack<Farm>();

    /// <summary>An open scope: hand it back to <see cref="Exit"/> from a finalizer.</summary>
    public readonly struct Handle
    {
        internal readonly int Token;
        internal readonly KindScope Kind;

        internal Handle(int token, KindScope kind)
        {
            Token = token;
            Kind = kind;
        }

        /// <summary>Closes the scope and puts the game's farm type back. Safe on a handle that was never opened.</summary>
        public void Exit()
        {
            if (Token <= 0) return;
            Kind.Exit();
            Stack.Exit(Token);
        }
    }

    /// <summary>
    /// Opens a scope about this farm. For a started plot the game's one farm type is that plot's
    /// kind until the scope closes (<see cref="KindScope"/>); for the main farm, or for null,
    /// nothing changes and <see cref="FarmOrMain"/> answers the main farm.
    /// </summary>
    public static Handle Enter(Farm? farm)
    {
        if (farm is null || !Farms.IsPool(farm) || !Farms.IsStarted(farm)) return default;
        return new Handle(Stack.Enter(farm), KindScope.Enter(farm));
    }

    /// <summary>What a rewritten call to Game1.getFarm() gets: the farm the open scope is about, else the main farm.</summary>
    public static Farm FarmOrMain() => stack?.Current ?? Game1.getFarm();

    /// <summary>The farm the open scope is about, or null between scopes (the lab reads it).</summary>
    internal static Farm? Current => stack?.Current;

    internal static int Depth => stack?.Depth ?? 0;

    /// <summary>How many times a scope was found open between two ticks in this session. Never, if every scope is closed by its finalizer.</summary>
    internal static int Leaks { get; private set; }

    /// <summary>
    /// Once per tick, between the game's calls: no scope may be open. One that is was left open by
    /// a call that did not end the usual way; it is closed here, so nothing after this tick can
    /// take a plot for the main farm, and the log says so once.
    /// </summary>
    public static void CheckIdle(IMonitor monitor)
    {
        if (stack is null || stack.Depth == 0) return;
        int left = stack.Reset();
        Leaks++;
        if (Leaks == 1) monitor.Log($"Many Farms: {left} farm scope(s) were still open between two ticks and were closed. The game's farm type may be wrong until the next farm does its work; tell the mod's author.", LogLevel.Error);
    }

    /// <summary>True for "Farm" and for the name of a plot somebody has started: what a rewritten name test asks.</summary>
    public static bool IsAFarmName(string? name) =>
        name == FarmSlots.MainFarm || (FarmSlots.TryFarmSlot(name, out int slot) && Farms.Slot(slot) is { } farm && Farms.IsStarted(farm));
}

/// <summary>
/// Rewrites one named method of the game's so that it means "the farm this call is about" where
/// it said "the main farm". Only the method named is touched: what it calls is not, so shipping,
/// the save and other mods' code can never be redirected by it. Each rewrite is looked for before
/// anything is patched; a method that does not hold it (a game update changed it) is left exactly
/// as the game made it and the log says what stays main-farm-only.
/// </summary>
internal static class Redirect
{
    private static readonly MethodInfo GetFarm = AccessTools.DeclaredMethod(typeof(Game1), nameof(Game1.getFarm));
    private static readonly MethodInfo FarmOrMain = AccessTools.DeclaredMethod(typeof(FarmScope), nameof(FarmScope.FarmOrMain));
    private static readonly MethodInfo IsAFarmName = AccessTools.DeclaredMethod(typeof(FarmScope), nameof(FarmScope.IsAFarmName));
    private static readonly MethodInfo StringEquals = AccessTools.DeclaredMethod(typeof(string), "op_Equality", new[] { typeof(string), typeof(string) });

    private static bool IsGetFarm(CodeInstruction code) => code.Calls(GetFarm);

    private static bool IsFarmLiteral(CodeInstruction code) => code.opcode == OpCodes.Ldstr && code.operand as string == FarmSlots.MainFarm;

    private static bool IsStringEquals(CodeInstruction code) => code.Calls(StringEquals);

    /// <summary>Every call to Game1.getFarm() in this method asks the open scope instead.</summary>
    /// <returns>How many calls were found; 0 leaves the method unpatched.</returns>
    public static int FarmCalls(Harmony harmony, MethodBase method)
    {
        int found = LexicalRewrite.Count(PatchProcessor.GetOriginalInstructions(method), IsGetFarm);
        if (found > 0) harmony.Patch(method, transpiler: new HarmonyMethod(typeof(Redirect), nameof(FarmCallsTranspiler)));
        return found;
    }

    /// <summary>Every test "this location's name is Farm" in this method is true on a started plot as well.</summary>
    /// <returns>How many tests were found; 0 leaves the method unpatched.</returns>
    public static int FarmNameTests(Harmony harmony, MethodBase method)
    {
        int found = LexicalRewrite.CountPairs(PatchProcessor.GetOriginalInstructions(method), IsFarmLiteral, IsStringEquals);
        if (found > 0) harmony.Patch(method, transpiler: new HarmonyMethod(typeof(Redirect), nameof(FarmNameTranspiler)));
        return found;
    }

    private static IEnumerable<CodeInstruction> FarmCallsTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        LexicalRewrite.ReplaceEach(code, IsGetFarm, Keep(OpCodes.Call, FarmOrMain));
        return code;
    }

    private static IEnumerable<CodeInstruction> FarmNameTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        // name == "Farm"  becomes  FarmScope.IsAFarmName(name): the literal goes, the comparison becomes the question.
        LexicalRewrite.ReplacePairs(code, IsFarmLiteral, IsStringEquals, Keep(OpCodes.Nop, null), Keep(OpCodes.Call, IsAFarmName));
        return code;
    }

    // The new instruction stands where the old one stood: whatever jumps to it, or guards it, still does.
    private static Func<CodeInstruction, CodeInstruction> Keep(OpCode opcode, object? operand) => old =>
    {
        var made = new CodeInstruction(opcode, operand);
        made.labels.AddRange(old.labels);
        made.blocks.AddRange(old.blocks);
        return made;
    };
}

/// <summary>
/// Repeats, on the host, something the game does for the main farm only, once for each started
/// plot. One plot's failure is logged once and does not stop the others.
/// </summary>
internal static class EachFarm
{
    /// <param name="what">What is being repeated, for the log ("lightning over the other farms").</param>
    /// <returns>How many plots were done.</returns>
    public static int Plots(string what, Action<Farm, int> work)
    {
        if (!Game1.IsMasterGame) return 0;
        var plots = new List<(Farm Farm, int Slot)>();
        foreach (int slot in FarmSlots.All)
            if (Farms.Slot(slot) is { } farm && Farms.IsStarted(farm)) plots.Add((farm, slot));
        return EachFarmRule.Run(plots, plot => work(plot.Farm, plot.Slot), (plot, ex) => Patches.Warn($"{what} (plot {plot.Slot})", ex));
    }
}

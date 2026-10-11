using System.Globalization;

namespace ManyFarms.Core;

/// <summary>
/// The spare farm plots. Every game that runs Many Farms has the same eight, by the same names,
/// whether or not anyone farms them yet: a location has to exist on the host and on every player
/// before the save is read, so the set cannot depend on what a save says.
/// </summary>
public static class FarmSlots
{
    /// <summary>The game's own farm, where the farmhouse and the cabins stand.</summary>
    public const string MainFarm = "Farm";

    public const string Prefix = "CarbonNode.ManyFarms_Farm";
    public const string CaveSuffix = "_Cave";

    /// <summary>How many spare plots there are. Changing this changes what a save may hold.</summary>
    public const int Count = 8;

    public static IEnumerable<int> All => Enumerable.Range(1, Count);

    public static string FarmName(int slot) => Prefix + Checked(slot).ToString(CultureInfo.InvariantCulture);

    /// <summary>The cave that belongs to a plot, so one farm's mushrooms are not another's.</summary>
    public static string CaveName(int slot) => FarmName(slot) + CaveSuffix;

    public static bool TryFarmSlot(string? name, out int slot) => TryParse(name, "", out slot);

    public static bool TryCaveSlot(string? name, out int slot) => TryParse(name, CaveSuffix, out slot);

    private static bool TryParse(string? name, string suffix, out int slot)
    {
        slot = 0;
        if (name is null || !name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) return false;
        string digits = name.Substring(Prefix.Length, name.Length - Prefix.Length - suffix.Length);
        // "01" and "+1" are not plot names: a name has exactly one spelling.
        if (digits.Length == 0 || digits.Length > 2 || digits[0] == '0' || !digits.All(char.IsDigit)) return false;
        int parsed = int.Parse(digits, CultureInfo.InvariantCulture);
        if (parsed < 1 || parsed > Count) return false;
        slot = parsed;
        return true;
    }

    private static int Checked(int slot) =>
        slot >= 1 && slot <= Count ? slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, $"Farm plots are numbered 1 to {Count}.");
}

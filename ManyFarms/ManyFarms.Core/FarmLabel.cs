namespace ManyFarms.Core;

/// <summary>What a farm is called on screen: after the farmers whose farm it is.</summary>
public static class FarmLabel
{
    /// <param name="memberNames">The farmers whose farm it is, in the order they joined.</param>
    /// <param name="whenEmpty">What to call a farm nobody farms.</param>
    public static string For(IReadOnlyList<string> memberNames, string whenEmpty)
    {
        var names = memberNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        return names.Count switch
        {
            0 => whenEmpty,
            1 => $"{names[0]}'s farm",
            2 => $"{names[0]} and {names[1]}'s farm",
            // Three names already fill a row; past that the faces say who.
            3 => $"{names[0]}, {names[1]} and {names[2]}'s farm",
            _ => $"{names[0]}, {names[1]} and {names.Count - 2} more",
        };
    }
}

namespace SharedDay.Core;

/// <summary>
/// A farm's name in a sentence. The game stores what the host typed ("Sunny", or "Squeet Farm")
/// and Shared Day says "Sunny Farm"; a name that already ends in the word must not get it twice.
/// </summary>
public static class FarmWords
{
    /// <summary>"Sunny" is "Sunny Farm"; "Squeet Farm" stays "Squeet Farm".</summary>
    public static string Titled(string name)
    {
        name = name.Trim();
        return EndsWithFarm(name) ? name : name + " Farm";
    }

    /// <summary>The name for a sentence that adds " Farm" itself: "Squeet Farm" is "Squeet", "Sunny" stays "Sunny".</summary>
    public static string Bare(string name)
    {
        name = name.Trim();
        return EndsWithFarm(name) && name.Length > 5 ? name[..^5].TrimEnd() : name;
    }

    // The last word, not the last letters: "Windfarm" is a name, not a farm called "Wind".
    private static bool EndsWithFarm(string name) =>
        name.Equals("farm", StringComparison.OrdinalIgnoreCase) || name.EndsWith(" farm", StringComparison.OrdinalIgnoreCase);
}

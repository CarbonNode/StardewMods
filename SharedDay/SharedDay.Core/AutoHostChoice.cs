namespace SharedDay.Core;

/// <summary>
/// Which farm an unattended host loads. A dedicated host PC starts the game with nobody at
/// the keyboard, so the choice has to be the one a person would have made, and never a guess
/// that hosts the wrong farm.
/// </summary>
public static class AutoHostChoice
{
    /// <param name="saves">The farms the game's own Co-op menu offers to host, in its order (most recently played first).</param>
    /// <param name="configured">What the host wrote in config: a save folder name ("Sunny_385772993") or a farm's name ("Sunny"). Empty for "the farm I hosted last".</param>
    /// <param name="lastHosted">The save this installation hosted last, if it remembers one.</param>
    /// <returns>The save folder to host, or null when the request cannot be met exactly.</returns>
    public static string? Pick(IReadOnlyList<string> saves, string? configured, string? lastHosted)
    {
        if (saves.Count == 0) return null;
        string wanted = (configured ?? "").Trim();
        if (wanted.Length == 0)
        {
            // Nothing named: the farm hosted last here, else the one played most recently.
            return saves.FirstOrDefault(save => string.Equals(save, lastHosted, StringComparison.OrdinalIgnoreCase)) ?? saves[0];
        }
        var exact = saves.FirstOrDefault(save => string.Equals(save, wanted, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        // A farm's name without the game's numeric suffix, only when it names one farm.
        var byName = saves.Where(save => string.Equals(FarmName(save), wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    /// <summary>"Sunny_385772993" is the farm "Sunny"; a folder with no numeric suffix is its own name.</summary>
    public static string FarmName(string save)
    {
        int cut = save.LastIndexOf('_');
        return cut > 0 && cut < save.Length - 1 && save.Skip(cut + 1).All(char.IsDigit) ? save[..cut] : save;
    }
}

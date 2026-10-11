using SharedDay.Core;

namespace SharedDay.Mod;

/// <summary>
/// The game's own question at the bed ("Go to sleep for the night?", Strings/Locations:
/// FarmHouse_Bed_GoToSleep), reworded while Shared Day runs the farm (0.1.29): on a Relay farm
/// going to bed ends a turn, not a day, and the last farmer to play should know the night then
/// runs for everyone. The words are an asset edit (SMAPI's AssetRequested), invalidated whenever
/// the sentence changes; with Shared Day off, or once the night is under way, the game's own text
/// is left alone.
/// </summary>
internal static class BedQuestion
{
    internal const string Asset = "Strings/Locations";
    internal const string Key = "FarmHouse_Bed_GoToSleep";

    /// <param name="active">Shared Day runs this farm and the day is still open for this player.</param>
    /// <param name="mode">Relay or Together.</param>
    /// <param name="last">Every other roster farmer has played or sits today out: this one is the last.</param>
    /// <param name="othersStillUp">Together only: somebody else is not in bed yet.</param>
    /// <param name="date">"Spring 13".</param>
    /// <returns>The question, or null for the game's own text.</returns>
    internal static string? Wording(bool active, PlayMode mode, bool last, bool othersStillUp, string date)
    {
        if (!active) return null;
        if (mode == PlayMode.Together && othersStillUp) return "Go to sleep? The night starts once everyone is in bed.";
        if (last) return $"Go to sleep? You are the last to play {date}: the night then runs for everyone.";
        return $"Go to sleep? This ends your turn for {date}. You can't play again until the next day.";
    }
}

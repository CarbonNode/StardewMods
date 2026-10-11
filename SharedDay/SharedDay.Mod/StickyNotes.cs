using SharedDay.Core;
using StardewModdingAPI;
using StardewValley;

namespace SharedDay.Mod;

/// <summary>
/// The farm's sticky-note board in the game (0.1.33). The host keeps it in its own save
/// (<see cref="Key"/> on the main farmer, like the farm's other standing state) and sends it with
/// every status; a player pins and takes down through the host. Each farmer's own reading
/// (seen, put away, dragged where) lives on their farmer (<see cref="ReaderKey"/>), which the
/// game carries to whichever PC they play from.
/// </summary>
public static class NoteBoard
{
    public const string Key = "CarbonNode.SharedDay/stickyNotes";
    public const string ReaderKey = "CarbonNode.SharedDay/stickyNotesReader";

    /// <summary>The host's board, from its save.</summary>
    public static List<StickyNote> Read() => Game1.MasterPlayer?.modData.TryGetValue(Key, out string json) == true ? NoteRules.FromJson(json) : new List<StickyNote>();

    public static void Write(IReadOnlyList<StickyNote> board)
    {
        if (Game1.MasterPlayer is null) return;
        if (board.Count == 0) Game1.MasterPlayer.modData.Remove(Key);
        else Game1.MasterPlayer.modData[Key] = NoteRules.ToJson(board);
    }

    /// <summary>What the host tells the players, from its board.</summary>
    public static StickyNoteEntry[] Entries(IReadOnlyList<StickyNote> board)
        => board.Select(n => new StickyNoteEntry { Id = n.Id, AuthorId = n.AuthorId, Author = n.Author, Day = n.Day, Date = n.Date, Text = n.Text }).ToArray();

    /// <summary>The board as a player received it; null from a host too old to have one.</summary>
    public static List<StickyNote>? FromStatus(StatusMessage? status)
        => status?.Notes?.Where(e => e is { Id.Length: > 0, Text.Length: > 0 }).Select(e => new StickyNote(e.Id, e.AuthorId, e.Author ?? "", e.Day, e.Date ?? "", e.Text)).ToList();

    /// <summary>This farmer's own reading of the board.</summary>
    public static NoteReader Reader() => NoteReader.FromJson(Game1.player?.modData.TryGetValue(ReaderKey, out string json) == true ? json : null);

    public static void Save(NoteReader reader)
    {
        if (Game1.player is null) return;
        Game1.player.modData[ReaderKey] = reader.ToJson();
    }

    /// <summary>
    /// The host applies a request from <paramref name="asker"/>: pin a note under their name, or
    /// take one down. Null when done; the reason when refused, for the asker's screen.
    /// </summary>
    public static string? Apply(long asker, string askerName, StickyNoteRequest request, string date, IMonitor monitor)
    {
        var board = Read();
        switch (request.Action)
        {
            case "add":
            {
                string text = NoteRules.Clean(request.Text);
                if (NoteRules.WhyNot(board, text) is { } why) return why;
                var note = new StickyNote(NoteRules.NewId(DateTime.UtcNow, Game1.random.Next()), asker, askerName, Game1.Date.TotalDays, date, text);
                Write(NoteRules.Add(board, note));
                monitor.Log($"{askerName} pinned a note to the board ({text.Length} letters).", LogLevel.Info);
                return null;
            }
            case "remove":
            {
                bool isHost = asker == Game1.MasterPlayer.UniqueMultiplayerID;
                var after = NoteRules.Remove(board, request.Id ?? "", asker, isHost);
                if (after is null) return NoteRules.NotYoursWords;
                Write(after);
                monitor.Log($"{askerName} took a note down from the board.", LogLevel.Info);
                return null;
            }
            default:
                return "That is not something the board does.";
        }
    }
}

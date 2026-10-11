using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedDay.Core;

/// <summary>
/// Sticky notes between the farmers of a farm (the owner, 2026-10-09: "ability to leave cute sticky
/// note for other players ... other players can read it (its at the top left) next to hover
/// element that opens cute draggable sticky notes"). A note is the farm's, not a PC's: the host
/// keeps the board in its save and tells every player about it, so a note left in one turn is
/// read in the next, on another PC. What each reader has seen, put away or dragged where is
/// theirs alone. Pure rules here; the game plumbing is in the mod.
/// </summary>
public sealed record StickyNote(string Id, long AuthorId, string Author, int Day, string Date, string Text)
{
    /// <summary>The author's farmer, as the board shows faces; 0 when unknown.</summary>
    public bool By(long farmerId) => AuthorId == farmerId;
}

public static class NoteRules
{
    /// <summary>Room for a sentence or two in the game's small font on one paper; the paper wraps it.</summary>
    public const int MaxLength = 160;
    /// <summary>Papers on one farm's board. Past it, a new note needs an old one taken down first.</summary>
    public const int MaxNotes = 12;
    public const string EmptyWords = "Write something first.";
    public static readonly string TooLongWords = $"A note holds {MaxLength} letters at most.";
    public static readonly string FullWords = $"The board is full ({MaxNotes} notes). Take one of yours down first.";
    public const string NotYoursWords = "Only the farmer who left a note takes it down.";
    public const string PostedWords = "Pinned to the board for everyone on the farm.";

    /// <summary>What was typed, as it will be kept: trimmed, one space between words, line breaks kept as spaces.</summary>
    public static string Clean(string? text) => string.Join(" ", (text ?? "").Replace("\r", " ").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Null when the note may be added; the reason otherwise.</summary>
    public static string? WhyNot(IReadOnlyList<StickyNote> board, string text)
    {
        string clean = Clean(text);
        if (clean.Length == 0) return EmptyWords;
        if (clean.Length > MaxLength) return TooLongWords;
        if (board.Count >= MaxNotes) return FullWords;
        return null;
    }

    /// <summary>The board with a note added, newest last. The caller has checked <see cref="WhyNot"/>.</summary>
    public static List<StickyNote> Add(IReadOnlyList<StickyNote> board, StickyNote note) => board.Append(note).ToList();

    /// <summary>The board without a note; only its author, or the host (<paramref name="isHost"/>), takes one down. Null when refused.</summary>
    public static List<StickyNote>? Remove(IReadOnlyList<StickyNote> board, string id, long by, bool isHost)
    {
        var note = board.FirstOrDefault(n => n.Id == id);
        if (note is null) return board.ToList();
        if (!isHost && !note.By(by)) return null;
        return board.Where(n => n.Id != id).ToList();
    }

    /// <summary>A short id: time and a little chance, so two notes pinned in the same second differ.</summary>
    public static string NewId(DateTime now, int salt) => now.ToUniversalTime().ToString("yyyyMMddHHmmss") + "-" + (salt & 0xffff).ToString("x4");

    public static string ToJson(IReadOnlyList<StickyNote> board) => JsonSerializer.Serialize(board);

    /// <summary>The board read back; anything unreadable is an empty board, never a crash.</summary>
    public static List<StickyNote> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<StickyNote>();
        try
        {
            var list = JsonSerializer.Deserialize<List<StickyNote>>(json!) ?? new List<StickyNote>();
            return list.Where(n => n is { Id.Length: > 0, Text.Length: > 0 }).Take(MaxNotes).ToList();
        }
        catch (JsonException) { return new List<StickyNote>(); }
    }
}

/// <summary>
/// One reader's own side of the board: which notes they have read, which they put away (gone
/// from their screen, still on the farm's board for everyone else), and where they dragged each
/// paper. Kept with the farmer, so it follows them to any PC.
/// </summary>
public sealed class NoteReader
{
    [JsonInclude] public List<string> Seen { get; set; } = new();
    [JsonInclude] public List<string> PutAway { get; set; } = new();
    /// <summary>Paper positions by note id, as offsets from the paper's own place in the grid.</summary>
    [JsonInclude] public Dictionary<string, int[]> Moved { get; set; } = new();

    public static NoteReader FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new NoteReader();
        try { return JsonSerializer.Deserialize<NoteReader>(json!) ?? new NoteReader(); }
        catch (JsonException) { return new NoteReader(); }
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>The notes this reader has not opened the board on yet; their own notes never count.</summary>
    public int Unread(IReadOnlyList<StickyNote> board, long me) => board.Count(n => !n.By(me) && !Seen.Contains(n.Id) && !PutAway.Contains(n.Id));

    /// <summary>The papers on this reader's screen: everything not put away, oldest first.</summary>
    public IReadOnlyList<StickyNote> Showing(IReadOnlyList<StickyNote> board) => board.Where(n => !PutAway.Contains(n.Id)).ToList();

    /// <summary>Opening the board reads every note on it. Forgets ids that are no longer on the board, so the lists never grow for ever.</summary>
    public void SawAll(IReadOnlyList<StickyNote> board)
    {
        var ids = board.Select(n => n.Id).ToHashSet();
        Seen = Seen.Where(ids.Contains).Concat(board.Select(n => n.Id).Where(id => !Seen.Contains(id))).ToList();
        PutAway = PutAway.Where(ids.Contains).ToList();
        foreach (string gone in Moved.Keys.Where(id => !ids.Contains(id)).ToList()) Moved.Remove(gone);
    }

    public void Put(string id) { if (!PutAway.Contains(id)) PutAway.Add(id); }
    public void MovedTo(string id, int dx, int dy) => Moved[id] = new[] { dx, dy };
    public (int X, int Y) Offset(string id) => Moved.TryGetValue(id, out var o) && o.Length == 2 ? (o[0], o[1]) : (0, 0);
}

/// <summary>Where one paper is drawn, and its drag handle (the whole paper).</summary>
public sealed record NotePaper(StickyNote Note, Box Box, bool Unread, bool Mine);

/// <summary>
/// The notes board as papers on the screen: a grid of equal papers, oldest first, each shifted
/// by wherever its reader dragged it, and kept on screen. Plain arithmetic, tested without the
/// game. The new-note paper, when one is being written, takes the next free cell.
/// </summary>
public static class NoteBoardLayout
{
    public const int PaperW = 400;
    public const int PaperH = 228;
    public const int Gap = 24;
    public const int Margin = 24;
    /// <summary>The strip across the top with the title, its line under it and the buttons; the first row of papers (and their tape) starts below it.</summary>
    public const int Head = 112;

    public static int Columns(Box screen) => Math.Max(1, (screen.Width - Margin * 2 + Gap) / (PaperW + Gap));

    /// <summary>The cell a paper takes before any drag: left to right, then down. A short board is centred, so one paper sits in the middle, not off to the left.</summary>
    public static Box Cell(Box screen, int index, int total = int.MaxValue)
    {
        int cols = Columns(screen);
        int across = Math.Max(1, Math.Min(cols, total));
        int gridW = across * PaperW + (across - 1) * Gap;
        int x0 = screen.X + (screen.Width - gridW) / 2;
        int y0 = screen.Y + Head + Margin;
        return new Box(x0 + index % cols * (PaperW + Gap), y0 + index / cols * (PaperH + Gap), PaperW, PaperH);
    }

    /// <summary>Keeps a paper on the screen, whatever it was dragged to.</summary>
    public static Box Clamp(Box screen, Box paper)
    {
        int x = Math.Clamp(paper.X, screen.X, Math.Max(screen.X, screen.Right - paper.Width));
        int y = Math.Clamp(paper.Y, screen.Y + Head, Math.Max(screen.Y + Head, screen.Bottom - paper.Height));
        return new Box(x, y, paper.Width, paper.Height);
    }

    /// <param name="total">How many cells the board lays out (the papers, plus one while a draft is being written), so a short board is centred.</param>
    public static IReadOnlyList<NotePaper> Place(Box screen, IReadOnlyList<StickyNote> showing, NoteReader reader, long me, int total = -1)
    {
        if (total < 0) total = showing.Count;
        var papers = new List<NotePaper>();
        for (int i = 0; i < showing.Count; i++)
        {
            var n = showing[i];
            var cell = Cell(screen, i, total);
            var (dx, dy) = reader.Offset(n.Id);
            papers.Add(new NotePaper(n, Clamp(screen, new Box(cell.X + dx, cell.Y + dy, cell.Width, cell.Height)), !n.By(me) && !reader.Seen.Contains(n.Id), n.By(me)));
        }
        return papers;
    }

    /// <summary>The offset to remember for a paper dropped at <paramref name="dropped"/>: its distance from its own cell.</summary>
    public static (int X, int Y) OffsetFor(Box screen, int index, int total, Box dropped)
    {
        var cell = Cell(screen, index, total);
        return (dropped.X - cell.X, dropped.Y - cell.Y);
    }
}

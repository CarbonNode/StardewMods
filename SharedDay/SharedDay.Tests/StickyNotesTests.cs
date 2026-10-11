using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>The farm's sticky notes (0.1.33): what may be pinned, who takes what down, what each reader sees, and where the papers go.</summary>
public sealed class StickyNotesTests
{
    private static StickyNote Note(string id, long by, string name, string text = "Water the parsnips") => new(id, by, name, 0, "Spring 1", text);
    // Wide enough for three sheets across (1280 holds two).
    private static readonly Box Screen = new(0, 0, 1440, 720);

    [Fact]
    public void WhatIsTypedIsKeptClean()
    {
        Assert.Equal("Water the parsnips, I ran out of time.", NoteRules.Clean("  Water the   parsnips,\r\nI ran out of time. "));
        Assert.Equal("", NoteRules.Clean(null));
    }

    [Fact]
    public void ANoteNeedsWordsFitsOnePaperAndRoomOnTheBoard()
    {
        var empty = new List<StickyNote>();
        Assert.Equal(NoteRules.EmptyWords, NoteRules.WhyNot(empty, "   "));
        Assert.Equal(NoteRules.TooLongWords, NoteRules.WhyNot(empty, new string('x', NoteRules.MaxLength + 1)));
        Assert.Null(NoteRules.WhyNot(empty, new string('x', NoteRules.MaxLength)));
        var full = Enumerable.Range(0, NoteRules.MaxNotes).Select(i => Note($"n{i}", 1, "Alice")).ToList();
        Assert.Equal(NoteRules.FullWords, NoteRules.WhyNot(full, "one more"));
        Assert.Contains("12", NoteRules.FullWords);
    }

    [Fact]
    public void OnlyTheAuthorOrTheHostTakesANoteDown()
    {
        var board = new List<StickyNote> { Note("a", 1, "Alice"), Note("b", 2, "Bob") };
        Assert.Null(NoteRules.Remove(board, "a", by: 2, isHost: false));
        Assert.Equal(new[] { "b" }, NoteRules.Remove(board, "a", by: 1, isHost: false)!.Select(n => n.Id));
        Assert.Equal(new[] { "b" }, NoteRules.Remove(board, "a", by: 99, isHost: true)!.Select(n => n.Id));
        // A note already gone is no error.
        Assert.Equal(2, NoteRules.Remove(board, "zzz", by: 2, isHost: false)!.Count);
        Assert.Equal(new[] { "a", "b", "c" }, NoteRules.Add(board, Note("c", 1, "Alice")).Select(n => n.Id));
    }

    [Fact]
    public void TheBoardSurvivesTheSaveAndAnythingUnreadable()
    {
        var board = new List<StickyNote> { Note("a", 1, "Alice", "Hi \"Bob\" <3"), Note("b", 2, "Bob") };
        var back = NoteRules.FromJson(NoteRules.ToJson(board));
        Assert.Equal(board, back);
        Assert.Empty(NoteRules.FromJson(null));
        Assert.Empty(NoteRules.FromJson("not json"));
        Assert.Empty(NoteRules.FromJson("[{ \"Id\": \"\", \"Text\": \"\" }]"));
        Assert.Equal(NoteRules.MaxNotes, NoteRules.FromJson(NoteRules.ToJson(Enumerable.Range(0, 30).Select(i => Note($"n{i}", 1, "A")).ToList())).Count);
        Assert.Matches(@"^\d{14}-[0-9a-f]{4}$", NoteRules.NewId(new DateTime(2026, 10, 9, 1, 2, 3, DateTimeKind.Utc), 0x1234));
        Assert.NotEqual(NoteRules.NewId(DateTime.UtcNow, 1), NoteRules.NewId(DateTime.UtcNow, 2));
    }

    [Fact]
    public void AReaderCountsOnlyOtherPeoplesUnreadNotesAndKeepsWhatTheyPutAway()
    {
        var board = new List<StickyNote> { Note("a", 1, "Alice"), Note("b", 2, "Bob"), Note("c", 2, "Bob") };
        var bob = new NoteReader();
        Assert.Equal(1, bob.Unread(board, me: 2));
        Assert.Equal(3, bob.Showing(board).Count);
        bob.SawAll(board);
        Assert.Equal(0, bob.Unread(board, me: 2));
        bob.Put("a");
        Assert.Equal(new[] { "b", "c" }, bob.Showing(board).Select(n => n.Id));
        bob.MovedTo("b", 30, -10);
        Assert.Equal((30, -10), bob.Offset("b"));
        Assert.Equal((0, 0), bob.Offset("zzz"));
        // Through the save and back; then notes that left the board are forgotten.
        var again = NoteReader.FromJson(bob.ToJson());
        Assert.Equal(new[] { "a" }, again.PutAway);
        Assert.Equal((30, -10), again.Offset("b"));
        again.SawAll(new List<StickyNote> { Note("c", 2, "Bob"), Note("d", 1, "Alice") });
        Assert.Empty(again.PutAway);
        Assert.Equal((0, 0), again.Offset("b"));
        Assert.Equal(1, again.Unread(new List<StickyNote> { Note("c", 2, "Bob"), Note("d", 1, "Alice"), Note("e", 1, "Alice") }, me: 2));
        Assert.Empty(NoteReader.FromJson("garbage").Seen);
    }

    [Fact]
    public void PapersSitInAGridOfEqualSheetsOldestFirstAndMoveWhereTheyWereDragged()
    {
        var board = new List<StickyNote> { Note("a", 1, "Alice"), Note("b", 2, "Bob"), Note("c", 1, "Alice"), Note("d", 2, "Bob") };
        var reader = new NoteReader();
        reader.MovedTo("b", 500, 300);
        var papers = NoteBoardLayout.Place(Screen, reader.Showing(board), reader, me: 2);
        Assert.Equal(3, NoteBoardLayout.Columns(Screen));
        Assert.All(papers, p => Assert.Equal((NoteBoardLayout.PaperW, NoteBoardLayout.PaperH), (p.Box.Width, p.Box.Height)));
        Assert.Equal(papers[0].Box.Y, papers[2].Box.Y);
        Assert.True(papers[3].Box.Y > papers[0].Box.Y, "the fourth paper starts a second row");
        Assert.True(papers[0].Box.X < papers[2].Box.X);
        // The dragged paper is where it was left, and never off the screen.
        Assert.Equal(papers[1].Box, NoteBoardLayout.Clamp(Screen, new Box(NoteBoardLayout.Cell(Screen, 1, 4).X + 500, NoteBoardLayout.Cell(Screen, 1, 4).Y + 300, NoteBoardLayout.PaperW, NoteBoardLayout.PaperH)));
        Assert.True(papers[1].Box.Right <= Screen.Right && papers[1].Box.Bottom <= Screen.Bottom);
        Assert.True(papers[1].Box.Y >= NoteBoardLayout.Head);
        Assert.True(papers[0].Unread && !papers[1].Unread && papers[1].Mine && !papers[0].Mine);
        var (dx, dy) = NoteBoardLayout.OffsetFor(Screen, 1, 4, papers[1].Box);
        Assert.True(dx <= 500 && dy <= 300);
        // One paper sits in the middle of the screen, not in the left column; two share the middle.
        var one = NoteBoardLayout.Place(Screen, board.Take(1).ToList(), new NoteReader(), 2);
        Assert.Equal(Screen.Width / 2, one[0].Box.X + one[0].Box.Width / 2);
        var two = NoteBoardLayout.Place(Screen, board.Take(2).ToList(), new NoteReader(), 2);
        Assert.Equal(Screen.Width, two[0].Box.X + two[1].Box.Right);
        // While a draft is being written the papers make room for it as one more cell.
        var withDraft = NoteBoardLayout.Place(Screen, board.Take(1).ToList(), new NoteReader(), 2, total: 2);
        Assert.Equal(two[0].Box.X, withDraft[0].Box.X);
        // Every paper starts under the head strip.
        Assert.All(papers, p => Assert.True(p.Box.Y >= NoteBoardLayout.Head + NoteBoardLayout.Margin - 8));
        // A phone-narrow window still has one column and every paper on it.
        var narrow = new Box(0, 0, 420, 700);
        var squeezed = NoteBoardLayout.Place(narrow, board, new NoteReader(), 2);
        Assert.Equal(1, NoteBoardLayout.Columns(narrow));
        Assert.All(squeezed, p => Assert.True(p.Box.X >= 0 && p.Box.Right <= narrow.Right));
    }
}

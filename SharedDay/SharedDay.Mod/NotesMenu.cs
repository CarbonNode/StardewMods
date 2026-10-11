using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharedDay.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace SharedDay.Mod;

/// <summary>
/// The farm's sticky notes, as papers you can drag about the screen (0.1.33). Opened from the
/// little note beside the turn board. Each paper is the game's own letter paper with the
/// author's face, their name, the farm's date and the words; your own carry "Take down", the
/// others "Put away" (gone from your screen only). "New note" lays a blank paper with a line to
/// write on; Enter or "Pin it" sends it to the host, who keeps it for everyone. Escape closes.
/// </summary>
public sealed class NotesMenu : IClickableMenu
{
    private static readonly Rectangle Frame = new(384, 396, 15, 15);
    private static readonly Rectangle ButtonFrame = new(432, 439, 9, 9);
    /// <summary>The game's letter paper (the plain one), 320 by 180 on the sheet.</summary>
    private static readonly Rectangle Paper = new(0, 0, 320, 180);
    private const float PaperScale = 1.25f;
    private const int Pad = 22;
    private const int FaceSize = 40;
    private const int DragSlack = 6;

    private readonly ModEntry mod;
    private readonly IMonitor monitor;
    private readonly Texture2D paper;
    private Box screen;
    private NoteReader reader;
    private IReadOnlyList<StickyNote> board = Array.Empty<StickyNote>();
    private IReadOnlyList<NotePaper> papers = Array.Empty<NotePaper>();
    private readonly Dictionary<string, Rectangle> putAwayButtons = new();
    private Rectangle newButton, closeButton, pinButton, dropButton;
    private Rectangle? draftBox;
    private TextBox? draft;
    private bool writing;
    private string? notice;
    private DateTime noticeUntil;
    private bool faceDrawFailed;
    // Dragging a paper: which, where it was pressed, and whether it has moved enough to be a drag.
    private string? carrying;
    private Point pressedAt;
    private Point carryOffset;
    private bool carried;
    private Box? carryBox;
    private string lastSignature = "";

    /// <summary>What the menu last drew as its lines of text, for the lab.</summary>
    public IReadOnlyList<string> Lines { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<NotePaper> Papers => papers;
    public bool Writing => writing;
    public string? Notice => notice is not null && DateTime.UtcNow < noticeUntil ? notice : null;

    public NotesMenu(ModEntry mod, IMonitor monitor)
        : base(0, 0, 0, 0)
    {
        this.mod = mod;
        this.monitor = monitor;
        paper = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\letterBG");
        reader = NoteBoard.Reader();
        Layout();
        Refresh(force: true);
        // Opening the board reads everything on it.
        reader.SawAll(board);
        NoteBoard.Save(reader);
        if (Game1.options.SnappyMenus) snapToDefaultClickableComponent();
    }

    private void Layout()
    {
        var safe = Utility.getSafeArea();
        screen = new Box(safe.X, safe.Y, safe.Width, safe.Height);
        xPositionOnScreen = screen.X;
        yPositionOnScreen = screen.Y;
        width = screen.Width;
        height = screen.Height;
        int h = 56, w = 200;
        closeButton = new Rectangle(screen.Right - NoteBoardLayout.Margin - w, screen.Y + 16, w, h);
        newButton = new Rectangle(closeButton.X - 16 - w, screen.Y + 16, w, h);
    }

    /// <summary>The board as the host last sent it; the papers follow.</summary>
    private void Refresh(bool force = false)
    {
        var now = mod.NotesNow();
        string signature = string.Join("|", now.Select(n => n.Id));
        if (!force && signature == lastSignature) return;
        lastSignature = signature;
        board = now;
        papers = NoteBoardLayout.Place(screen, reader.Showing(board), reader, Game1.player.UniqueMultiplayerID, reader.Showing(board).Count + (writing ? 1 : 0));
        // The draft paper takes the next cell.
        if (writing) draftBox = NoteBoardLayout.Cell(screen, papers.Count, papers.Count + 1).ToRectangle();
        PlaceDraft();
    }

    private void PlaceDraft()
    {
        if (!writing || draft is null || draftBox is not { } box) return;
        draft.X = box.X + Pad;
        draft.Y = box.Y + Pad + 50;
        draft.Width = box.Width - Pad * 2;
        pinButton = new Rectangle(box.Right - Pad - 150, box.Bottom - Pad - 44, 150, 44);
        dropButton = new Rectangle(box.X + Pad, box.Bottom - Pad - 44, 150, 44);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        Layout();
        Refresh(force: true);
    }

    public override bool readyToClose() => !writing;

    public override void update(GameTime time)
    {
        base.update(time);
        Refresh();
    }

    /// <summary>A blank paper to write on. The lab calls it too.</summary>
    public bool BeginNew()
    {
        if (writing) return true;
        if (NoteRules.WhyNot(board, "x") is { } why && why != NoteRules.EmptyWords) return Say(why);
        writing = true;
        draft ??= new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), null, Game1.smallFont, Game1.textColor) { textLimit = NoteRules.MaxLength, limitWidth = false };
        draft.Text = "";
        draft.OnEnterPressed -= OnEnter;
        draft.OnEnterPressed += OnEnter;
        draftBox = NoteBoardLayout.Cell(screen, papers.Count, papers.Count + 1).ToRectangle();
        // The papers move over to make room for the draft beside them.
        papers = NoteBoardLayout.Place(screen, reader.Showing(board), reader, Game1.player.UniqueMultiplayerID, papers.Count + 1);
        PlaceDraft();
        draft.SelectMe();
        Game1.playSound("shwip");
        return true;
    }

    private void OnEnter(TextBox sender) => Pin();

    /// <summary>What is being written. The lab calls it; a player types.</summary>
    public void TypeNew(string text)
    {
        if (draft is not null) draft.Text = text;
    }

    /// <summary>Pin the draft to the board: it goes to the host, who tells everyone. False, with the reason shown, when it cannot.</summary>
    public bool Pin()
    {
        if (!writing || draft is null) return false;
        string text = NoteRules.Clean(draft.Text);
        if (NoteRules.WhyNot(board, text) is { } why) return Say(why);
        mod.PinNote(text);
        Game1.playSound("bigSelect");
        DropDraft();
        notice = NoteRules.PostedWords;
        noticeUntil = DateTime.UtcNow.AddSeconds(5);
        return true;
    }

    /// <summary>Throw the draft away.</summary>
    public void DropDraft()
    {
        if (!writing) return;
        writing = false;
        draftBox = null;
        if (draft is not null) { draft.Text = ""; draft.Selected = false; }
        Game1.playSound("bigDeSelect");
    }

    /// <summary>Put a note away: gone from this screen, still on the board for everyone else.</summary>
    public void PutAway(string id)
    {
        reader.Put(id);
        NoteBoard.Save(reader);
        Refresh(force: true);
        Game1.playSound("shwip");
    }

    /// <summary>Take one of your own notes down, for everyone.</summary>
    public void TakeDown(string id)
    {
        mod.TakeDownNote(id);
        Game1.playSound("trashcan");
    }

    /// <summary>The host said no to the last note.</summary>
    public void Refused(string reason) => Say(reason);

    private bool Say(string words)
    {
        Game1.playSound("cancel");
        notice = words;
        noticeUntil = DateTime.UtcNow.AddSeconds(6);
        return false;
    }

    public void Close()
    {
        if (writing) DropDraft();
        if (Game1.activeClickableMenu == this) Game1.exitActiveMenu();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (closeButton.Contains(x, y)) { Close(); return; }
        if (newButton.Contains(x, y)) { BeginNew(); return; }
        if (writing && draft is not null)
        {
            if (pinButton.Contains(x, y)) { Pin(); return; }
            if (dropButton.Contains(x, y)) { DropDraft(); return; }
            if (new Rectangle(draft.X, draft.Y, draft.Width, draft.Height).Contains(x, y)) { draft.SelectMe(); return; }
        }
        foreach (var (id, button) in putAwayButtons)
        {
            if (!button.Contains(x, y)) continue;
            var p = papers.FirstOrDefault(n => n.Note.Id == id);
            if (p is null) return;
            if (p.Mine) TakeDown(id); else PutAway(id);
            return;
        }
        // Topmost paper under the pointer is picked up; a press that does not move is nothing.
        for (int i = papers.Count - 1; i >= 0; i--)
        {
            var p = papers[i];
            if (!p.Box.Contains(x, y)) continue;
            carrying = p.Note.Id;
            pressedAt = new Point(x, y);
            carryOffset = new Point(x - p.Box.X, y - p.Box.Y);
            carried = false;
            carryBox = null;
            return;
        }
    }

    public override void leftClickHeld(int x, int y)
    {
        if (carrying is null) return;
        if (!carried && Math.Abs(x - pressedAt.X) < DragSlack && Math.Abs(y - pressedAt.Y) < DragSlack) return;
        carried = true;
        carryBox = NoteBoardLayout.Clamp(screen, new Box(x - carryOffset.X, y - carryOffset.Y, NoteBoardLayout.PaperW, NoteBoardLayout.PaperH));
    }

    public override void releaseLeftClick(int x, int y)
    {
        if (carrying is { } id && carried && carryBox is { } dropped)
        {
            var showing = reader.Showing(board);
            int index = showing.ToList().FindIndex(n => n.Id == id);
            if (index >= 0)
            {
                var (dx, dy) = NoteBoardLayout.OffsetFor(screen, index, showing.Count + (writing ? 1 : 0), dropped);
                reader.MovedTo(id, dx, dy);
                NoteBoard.Save(reader);
                Refresh(force: true);
                Game1.playSound("stoneStep");
            }
        }
        carrying = null;
        carried = false;
        carryBox = null;
    }

    /// <summary>Where a paper is drawn right now, carried or not. For the lab.</summary>
    public Box? PaperBox(string id) => carrying == id && carried && carryBox is { } c ? c : papers.FirstOrDefault(p => p.Note.Id == id)?.Box;

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            if (writing) DropDraft(); else Close();
            return;
        }
        if (writing) return; // the line has the keyboard
        if (Game1.options.doesInputListContain(Game1.options.menuButton, key)) Close();
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (button == Buttons.B) { if (writing) DropDraft(); else Close(); }
        else if (button == Buttons.Y && !writing) BeginNew();
    }

    public override void draw(SpriteBatch b)
    {
        mod.TurnHud.DrawBackdrop(b);
        var lines = new List<string>();
        var font = Game1.smallFont;
        int lineHeight = (int)font.MeasureString("Ay").Y;

        // The head: what this is, and the two buttons.
        string farm = Game1.MasterPlayer?.farmName.Value is { Length: > 0 } named ? FarmWords.Titled(named) : "the farm";
        string title = $"Notes on {farm}";
        Utility.drawTextWithShadow(b, title, Game1.dialogueFont, new Vector2(screen.X + NoteBoardLayout.Margin, screen.Y + 24), Color.White);
        lines.Add(title);
        string count = board.Count == 0 ? "Nothing pinned yet. Leave the first note." : $"{board.Count} of {NoteRules.MaxNotes} papers; drag one to move it";
        Utility.drawTextWithShadow(b, count, font, new Vector2(screen.X + NoteBoardLayout.Margin, screen.Y + 24 + (int)Game1.dialogueFont.MeasureString(title).Y + 2), Color.White * 0.8f);
        lines.Add(count);
        Button(b, lines, newButton, "New note", !writing && board.Count < NoteRules.MaxNotes);
        Button(b, lines, closeButton, "Close", true);

        putAwayButtons.Clear();
        foreach (var p in papers)
        {
            if (carrying == p.Note.Id && carried) continue;
            DrawPaper(b, lines, p, p.Box.ToRectangle());
        }
        // The carried paper rides on top.
        if (carrying is { } id && carried && carryBox is { } box && papers.FirstOrDefault(p => p.Note.Id == id) is { } held) DrawPaper(b, lines, held, box.ToRectangle());

        if (writing && draft is not null && draftBox is { } d)
        {
            DrawSheet(b, d);
            Utility.drawTextWithShadow(b, "A note for everyone on the farm", font, new Vector2(d.X + Pad, d.Y + Pad), Game1.textColor * 0.75f);
            lines.Add("[new note]");
            draft.Draw(b, drawShadow: false);
            string left = $"{NoteRules.MaxLength - NoteRules.Clean(draft.Text).Length} letters left";
            Utility.drawTextWithShadow(b, left, font, new Vector2(d.X + Pad, draft.Y + draft.Height + 6), Game1.textColor * 0.6f);
            Button(b, lines, dropButton, "Drop it", true, small: true);
            Button(b, lines, pinButton, "Pin it", true, small: true);
        }

        if (Notice is { } said)
        {
            var size = font.MeasureString(said);
            Utility.drawTextWithShadow(b, said, font, new Vector2(screen.X + (screen.Width - size.X) / 2, screen.Bottom - NoteBoardLayout.Margin - size.Y), Color.White);
            lines.Add(said);
        }
        Lines = lines;
        drawMouse(b);
    }

    /// <summary>The paper itself: the game's letter, with a strip of tape across its top edge.</summary>
    private void DrawSheet(SpriteBatch b, Rectangle box)
    {
        b.Draw(Game1.staminaRect, new Rectangle(box.X + 6, box.Y + 8, box.Width, box.Height), Color.Black * 0.35f);
        b.Draw(paper, new Rectangle(box.X, box.Y, box.Width, box.Height), Paper, Color.White);
        // Tape: a pale strip, slightly askew, over the top edge.
        b.Draw(Game1.staminaRect, new Vector2(box.X + box.Width / 2 - 36, box.Y - 8), null, new Color(236, 224, 170) * 0.85f, -0.08f, Vector2.Zero, new Vector2(72, 16), SpriteEffects.None, 0.9f);
    }

    private void DrawPaper(SpriteBatch b, List<string> lines, NotePaper p, Rectangle box)
    {
        var font = Game1.smallFont;
        DrawSheet(b, box);
        int x = box.X + Pad, y = box.Y + Pad, inner = box.Width - Pad * 2;
        int lineHeight = (int)font.MeasureString("Ay").Y;
        DrawFace(b, p.Note.AuthorId, p.Note.Author, new Rectangle(x, y, FaceSize, FaceSize));
        // Who and when on one line beside the face: "Alice, Spring 1"; the words start under both.
        string who = p.Mine ? "You" : p.Note.Author;
        string whoWhen = Fit($"{who}, {p.Note.Date}", font, inner - FaceSize - 10);
        Utility.drawTextWithShadow(b, whoWhen, font, new Vector2(x + FaceSize + 10, y + (FaceSize - lineHeight) / 2 + 2), Game1.textColor);
        if (p.Unread)
        {
            string tag = "New";
            var size = font.MeasureString(tag);
            var tagBox = new Rectangle(box.Right - Pad - (int)size.X - 12, y, (int)size.X + 12, (int)size.Y + 2);
            b.Draw(Game1.staminaRect, tagBox, new Color(255, 214, 90));
            Utility.drawTextWithShadow(b, tag, font, new Vector2(tagBox.X + 6, tagBox.Y + 1), Game1.textColor);
            lines.Add("[New]");
        }
        string words = Game1.parseText(p.Note.Text, font, inner);
        // The lines that fit above the button; a longer note is cut with "..." on the last line it has room for.
        var wrapped = words.Split('\n');
        int top = y + FaceSize + 10;
        int room = (box.Bottom - Pad - 40 - 8 - top) / lineHeight;
        if (wrapped.Length > room && room > 0) wrapped = wrapped.Take(room - 1).Append(Fit(string.Join(" ", wrapped.Skip(room - 1)), font, inner)).ToArray();
        Utility.drawTextWithShadow(b, string.Join("\n", wrapped), font, new Vector2(x, top), Game1.textColor);
        lines.Add($"{who}: {p.Note.Text}");
        // Wide enough for "Take down" and "Put away" whole: the lab read "Take do..." at 130.
        var button = new Rectangle(box.Right - Pad - 176, box.Bottom - Pad - 40, 176, 40);
        putAwayButtons[p.Note.Id] = button;
        Button(b, lines, button, p.Mine ? "Take down" : "Put away", true, small: true);
    }

    private void DrawFace(SpriteBatch b, long id, string name, Rectangle cell)
    {
        b.Draw(Game1.staminaRect, cell, Game1.textColor * 0.12f);
        var farmer = id == 0 ? null : Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == id);
        if (farmer is not null && !faceDrawFailed)
        {
            try
            {
                farmer.FarmerRenderer.drawMiniPortrat(b, new Vector2(cell.X, cell.Y), 0.89f, 2.5f, 2, farmer, 1f);
                return;
            }
            catch (Exception ex)
            {
                faceDrawFailed = true;
                monitor.Log("The notes could not draw a farmer's face and show initials instead. " + ex.GetType().Name, LogLevel.Warn);
            }
        }
        string initial = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
        var size = Game1.dialogueFont.MeasureString(initial) * 0.7f;
        Utility.drawTextWithShadow(b, initial, Game1.dialogueFont, new Vector2(cell.X + (cell.Width - size.X) / 2, cell.Y + (cell.Height - size.Y) / 2 + 2), Game1.textColor, 0.7f);
    }

    private void Button(SpriteBatch b, List<string> lines, Rectangle c, string label, bool enabled, bool small = false)
    {
        bool hot = enabled && c.Contains(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
        var tint = !enabled ? Color.White * 0.45f : hot ? Color.White : new Color(228, 218, 204);
        drawTextureBox(b, Game1.mouseCursors, ButtonFrame, c.X, c.Y, c.Width, c.Height, tint, 4f, drawShadow: enabled);
        var font = small ? Game1.smallFont : Game1.dialogueFont;
        float scale = small ? 1f : 0.8f;
        string text = Fit(label, font, c.Width - 20, scale);
        var size = font.MeasureString(text) * scale;
        Utility.drawTextWithShadow(b, text, font, new Vector2(c.X + (c.Width - size.X) / 2, c.Y + (c.Height - size.Y) / 2 + 2), Game1.textColor * (enabled ? 1f : 0.45f), scale);
        lines.Add((enabled ? "[" : "(") + text + (enabled ? "]" : ")"));
    }

    private static string Fit(string text, SpriteFont font, float max, float scale = 1f)
    {
        if (font.MeasureString(text).X * scale <= max) return text;
        while (text.Length > 1 && font.MeasureString(text + "...").X * scale > max) text = text[..^1];
        return text.TrimEnd() + "...";
    }
}

internal static class BoxExtensions
{
    public static Rectangle ToRectangle(this Box box) => new(box.X, box.Y, box.Width, box.Height);
}

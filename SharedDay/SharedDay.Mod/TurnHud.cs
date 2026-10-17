using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharedDay.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace SharedDay.Mod;

/// <summary>
/// The turn board on the screen while a farmer plays: a small strip of the farm's faces in a
/// corner, each marked with whether that farmer has taken today's turn. Under the pointer it
/// opens to one line per farmer, and only there unless HudConfig.OpenOnChange is switched on.
/// A click on it opens Shared Day's own menu, like the menu key.
///
/// It is drawn with the game's HUD, so it hides whenever the game hides its HUD (cutscenes,
/// festivals' menus, screenshots) and never over an open menu.
/// </summary>
public sealed class TurnHud
{
    // The same wooden frame the title hub uses, and the game's own tick box and close cross.
    private static readonly Rectangle Frame = new(384, 396, 15, 15);
    private static readonly Rectangle Ticked = new(236, 425, 9, 9);
    private static readonly Rectangle Cross = new(337, 494, 12, 12);
    private static readonly Color Green = new(40, 110, 40);
    private static readonly TimeSpan ShowNews = TimeSpan.FromSeconds(6);

    private readonly IMonitor monitor;
    private readonly Func<HudConfig> config;
    private readonly Func<TurnBoardView?> board;
    private readonly Func<string?> date;
    /// <summary>The farm's sticky notes (0.1.33): how many this farmer has not read, or null while the host offers no board.</summary>
    private readonly Func<int?> notes;
    private string? lastSignature;
    private DateTime newsUntil;
    private bool hovered;
    private bool faceDrawFailed;
    private Box lastScreen;

    /// <summary>Carrying the board with the mouse. A press that does not move is still a click.</summary>
    public BoardDrag Drag { get; } = new();

    /// <summary>Where the board was last drawn, for clicks and for the tests. Null while it is not on screen.</summary>
    public TurnBoardPlacement? Placement { get; private set; }
    /// <summary>What the board last showed, for the tests.</summary>
    public TurnBoardView? Shown { get; private set; }
    public bool IsOpen { get; private set; }

    public TurnHud(IMonitor monitor, Func<HudConfig> config, Func<TurnBoardView?> board, Func<string?> date, Func<int?>? notes = null)
    {
        this.monitor = monitor;
        this.config = config;
        this.board = board;
        this.date = date;
        this.notes = notes ?? (() => null);
    }

    /// <summary>The little note beside the strip that opens the farm's sticky notes (0.1.33). Null while there is none on screen.</summary>
    public Box? NotesTile { get; private set; }
    /// <summary>How many notes the tile said were unread when it was last drawn.</summary>
    public int NotesUnread { get; private set; }

    /// <summary>The pointer is on the notes tile: a click there opens the notes, not the board and not the world.</summary>
    public bool OwnsNotes(int x, int y) => NotesTile is { } t && Game1.activeClickableMenu is null && t.Contains(x, y);

    public void Reset()
    {
        NotesTile = null;
        lastSignature = null;
        newsUntil = default;
        hovered = false;
        Placement = null;
        Shown = null;
        IsOpen = false;
        Drag.Forget();
    }

    /// <summary>Opens the board for a while without a pointer on it, the way a change on it does.</summary>
    public void OpenFor(TimeSpan time) => newsUntil = DateTime.UtcNow + time;

    /// <summary>True when the pointer is on the board, so a click there is the board's and not the world's.</summary>
    public bool Owns(int x, int y) => Placement is { } p && Game1.activeClickableMenu is null && p.Contains(x, y);

    /// <summary>
    /// The left button went down on the board. Picks it up, so moving the pointer carries it and
    /// letting go without moving is still the click that opens Shared Day's menu. False when the
    /// board is not on screen to be picked up.
    /// </summary>
    public bool BeginDrag(int x, int y)
    {
        if (Placement is not { } p) return false;
        Drag.Press(lastScreen, p.Strip, x, y);
        return true;
    }

    public void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        Placement = null;
        NotesTile = null;
        var cfg = config();
        var view = cfg.Enabled ? board() : null;
        if (view is null || Game1.activeClickableMenu is not null || Game1.eventUp || Game1.farmEvent is not null)
        {
            Shown = null;
            IsOpen = false;
            // Nothing to carry: a menu opened, a cutscene began, or the board was switched off.
            Drag.Forget();
            return;
        }

        // A farm nobody has joined yet still has a board: one tile for the farm itself.
        string? farmName = Game1.MasterPlayer?.farmName.Value is { Length: > 0 } named ? FarmWords.Titled(named) : null;
        var tiles = view.Tiles(farmName);

        // The board opens under the pointer and only there (the owner, 2026-10-07: "only appear on
        // hover please"). It used to open by itself for a few seconds whenever it changed, which
        // on a host PC is the first key pressed. OpenOnChange brings that back for a player on a
        // controller, who has no pointer to hover with.
        string signature = view.Signature;
        if (cfg.OpenOnChange && lastSignature is not null && signature != lastSignature) newsUntil = DateTime.UtcNow + ShowNews;
        lastSignature = signature;

        var b = e.SpriteBatch;
        var font = Game1.smallFont;
        var safe = Utility.getSafeArea();
        var screen = new Box(safe.X, safe.Y, safe.Width, safe.Height);
        string head = view.Headline;
        string sub = string.Join("  |  ", new[] { date(), view.Count }.Where(s => !string.IsNullOrEmpty(s)));
        int lineHeight = (int)font.MeasureString("Ay").Y;
        int headHeight = lineHeight * 2 + 12;
        int widest = (int)Math.Max(font.MeasureString(head).X, font.MeasureString(sub).X);
        foreach (var f in tiles)
            widest = Math.Max(widest, TurnBoardLayout.Face + 12 + (int)font.MeasureString(Label(f)).X + 28 + (int)font.MeasureString(f.Words).X);
        int panelWidth = Math.Min(widest + TurnBoardLayout.Pad * 2, 560);

        lastScreen = screen;
        int mx = Game1.getMouseX(ui_scale: true), my = Game1.getMouseY(ui_scale: true);
        // While it is being carried it stays shut and goes exactly where the pointer puts it, so
        // what the player drops is what they saw. Everything else is the board's saved place.
        bool carrying = Drag.Dragging && Drag.Anchor is not null;
        var anchor = carrying ? Drag.Anchor! : new HudAnchor(cfg.Corner, cfg.OffsetX, cfg.OffsetY);
        bool wantOpen = !carrying && (hovered || DateTime.UtcNow < newsUntil);
        // The game's toolbar moves between the top and the bottom of the screen with the player; the board keeps out of its way.
        var toolbar = carrying ? null : Game1.onScreenMenus.OfType<Toolbar>().FirstOrDefault();
        Box? avoid = toolbar is null ? null : new Box(toolbar.xPositionOnScreen, toolbar.yPositionOnScreen, toolbar.width, toolbar.height);
        var place = TurnBoardLayout.Place(screen, tiles.Count, wantOpen, anchor.Corner, panelWidth, headHeight, anchor.OffsetX, anchor.OffsetY, avoid);
        // Hover is judged on what is on screen now; an open board stays open while the pointer is anywhere on it.
        bool over = place.Contains(mx, my);
        if (over != hovered)
        {
            hovered = over;
            if (!carrying && over != wantOpen) place = TurnBoardLayout.Place(screen, tiles.Count, over || DateTime.UtcNow < newsUntil, anchor.Corner, panelWidth, headHeight, anchor.OffsetX, anchor.OffsetY, avoid);
        }
        Placement = place;
        Shown = view;
        IsOpen = place.Panel is not null;

        // The strip: one face per farmer.
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, Frame, place.Strip.X, place.Strip.Y, place.Strip.Width, place.Strip.Height, Color.White, 2f, drawShadow: true);
        for (int i = 0; i < place.Faces.Count; i++) DrawFace(b, tiles[i], place.Faces[i], marks: true);
        if (place.More is { } more)
        {
            string text = "+" + place.Hidden;
            var size = font.MeasureString(text);
            Utility.drawTextWithShadow(b, text, font, new Vector2(more.X + (more.Width - size.X) / 2, more.Y + (more.Height - size.Y) / 2 + 2), Game1.textColor);
        }
        if (!carrying) DrawNotesTile(b, place.Strip, anchor.Corner, screen);
        if (place.Panel is not { } panel) return;

        // The open board: what the day is waiting on, then every farmer.
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, Frame, panel.X, panel.Y, panel.Width, panel.Height, Color.White, 2f, drawShadow: true);
        int x = panel.X + TurnBoardLayout.Pad, y = panel.Y + TurnBoardLayout.Pad, inner = panel.Width - TurnBoardLayout.Pad * 2;
        Utility.drawTextWithShadow(b, Fit(head, font, inner), font, new Vector2(x, y), Game1.textColor);
        Utility.drawTextWithShadow(b, Fit(sub, font, inner), font, new Vector2(x, y + lineHeight + 2), Game1.textColor * 0.7f);
        for (int i = 0; i < place.Rows.Count; i++)
        {
            var row = place.Rows[i];
            // The last row that fits stands for everyone below it.
            if (i == place.Rows.Count - 1 && tiles.Count > place.Rows.Count)
            {
                Utility.drawTextWithShadow(b, $"and {tiles.Count - i} more", font, new Vector2(row.X, row.Y + (row.Height - lineHeight) / 2 + 2), Game1.textColor * 0.7f);
                break;
            }
            var f = tiles[i];
            if (i % 2 == 0) b.Draw(Game1.staminaRect, new Rectangle(row.X - 6, row.Y + 2, row.Width + 12, row.Height - 4), Game1.textColor * 0.06f);
            DrawFace(b, f, new Box(row.X, row.Y + (row.Height - TurnBoardLayout.Face) / 2, TurnBoardLayout.Face, TurnBoardLayout.Face), marks: false);
            var wordsSize = font.MeasureString(f.Words);
            int textY = row.Y + (row.Height - lineHeight) / 2 + 2;
            int nameRoom = row.Width - TurnBoardLayout.Face - 12 - (int)wordsSize.X - 16;
            Utility.drawTextWithShadow(b, Fit(Label(f), font, nameRoom), font, new Vector2(row.X + TurnBoardLayout.Face + 12, textY),
                f.Mark is BoardMark.Done or BoardMark.SittingOut ? Game1.textColor * 0.65f : Game1.textColor);
            Utility.drawTextWithShadow(b, f.Words, font, new Vector2(row.Right - wordsSize.X, textY),
                f.Mark == BoardMark.Playing ? Green : f.Mark == BoardMark.Waiting ? Game1.textColor : Game1.textColor * 0.65f);
        }
    }

    /// <summary>
    /// The sticky notes' door (0.1.33): a small framed tile under the strip (above it in a bottom
    /// corner) with the game's own Secret Note on it, and a gold tag with how many notes this
    /// farmer has not read. Drawn only when the host offers a board.
    /// </summary>
    private void DrawNotesTile(SpriteBatch b, Box strip, BoardCorner corner, Box screen)
    {
        if (notes() is not { } unread) return;
        NotesUnread = unread;
        int size = TurnBoardLayout.Pad * 2 + TurnBoardLayout.Face;
        bool bottom = corner is BoardCorner.BottomLeft or BoardCorner.BottomRight;
        int y = bottom ? strip.Y - TurnBoardLayout.Seam - size : strip.Bottom + TurnBoardLayout.Seam;
        y = Math.Clamp(y, screen.Y, Math.Max(screen.Y, screen.Bottom - size));
        var tile = new Box(strip.X, y, size, size);
        NotesTile = tile;
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, Frame, tile.X, tile.Y, tile.Width, tile.Height, Color.White, 2f, drawShadow: true);
        // The Secret Note item, as the game draws it: 16 pixels, here at 2.5x to sit with the faces.
        var data = ItemRegistry.GetDataOrErrorItem("(O)79");
        b.Draw(data.GetTexture(), new Vector2(tile.X + TurnBoardLayout.Pad, tile.Y + TurnBoardLayout.Pad), data.GetSourceRect(), Color.White, 0f, Vector2.Zero, 2.5f, SpriteEffects.None, 0.9f);
        if (unread <= 0) return;
        string count = unread > 9 ? "9+" : unread.ToString();
        var font = Game1.smallFont;
        var measured = font.MeasureString(count);
        var tag = new Rectangle(tile.Right - (int)measured.X - 14, tile.Y - 6, (int)measured.X + 12, (int)measured.Y + 2);
        b.Draw(Game1.staminaRect, new Rectangle(tag.X + 2, tag.Y + 2, tag.Width, tag.Height), Color.Black * 0.3f);
        b.Draw(Game1.staminaRect, tag, new Color(255, 214, 90));
        Utility.drawTextWithShadow(b, count, font, new Vector2(tag.X + 6, tag.Y + 1), Game1.textColor);
    }

    // The backdrop's colours: the game's own dark joinery, a shade lighter for the weave.
    private static readonly Color BackdropBase = new(52, 33, 22);
    private static readonly Color BackdropWeave = new(84, 54, 34);
    private static readonly Color BackdropThread = new(150, 102, 56);

    /// <summary>
    /// Everything behind one of Shared Day's popups: the whole screen, in dark wood with a
    /// trellis woven across it (the owner, 2026-10-07: "patterned fullscreen around the popup so you
    /// dont even see the world"). Drawn before the popup; the world and the HUD are under it.
    /// </summary>
    public void DrawBackdrop(SpriteBatch b)
    {
        if (!config().PopupBackdrop) return;
        int w = Game1.uiViewport.Width, h = Game1.uiViewport.Height;
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, w, h), BackdropBase);
        // Two sets of diagonals make the trellis; a thin lighter thread beside each makes it read as woven.
        const int step = 64;
        float length = (float)Math.Sqrt((double)w * w + (double)h * h) + step;
        for (int x = -h - step; x < w + h + step; x += step)
        {
            b.Draw(Game1.staminaRect, new Vector2(x, 0), null, BackdropWeave, MathHelper.PiOver4, Vector2.Zero, new Vector2(length, 8), SpriteEffects.None, 0f);
            b.Draw(Game1.staminaRect, new Vector2(x + 14, 0), null, BackdropThread * 0.55f, MathHelper.PiOver4, Vector2.Zero, new Vector2(length, 2), SpriteEffects.None, 0f);
            b.Draw(Game1.staminaRect, new Vector2(x, 0), null, BackdropWeave, MathHelper.Pi - MathHelper.PiOver4, Vector2.Zero, new Vector2(length, 8), SpriteEffects.None, 0f);
            b.Draw(Game1.staminaRect, new Vector2(x - 14, 0), null, BackdropThread * 0.55f, MathHelper.Pi - MathHelper.PiOver4, Vector2.Zero, new Vector2(length, 2), SpriteEffects.None, 0f);
        }
        // A darker band top and bottom, so the popup's frame and the faces above it sit on calm ground.
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, w, 20), Color.Black * 0.35f);
        b.Draw(Game1.staminaRect, new Rectangle(0, h - 20, w, 20), Color.Black * 0.35f);
    }

    /// <summary>
    /// The farm's faces above Shared Day's own popup, so "still to play: Bob and Cleo" is also
    /// who Bob and Cleo are (the owner, 2026-10-06: "says waiting for [show faces]"). Every farmer
    /// wears the mark the board gives them: a tick for played, a cross for sitting out, the gold
    /// glow for whoever is playing now. Drawn after the popup, never over its words.
    /// </summary>
    public void DrawOverPopup(SpriteBatch b, DialogueBox popup)
    {
        var view = board();
        if (view is null || view.Farmers.Count == 0) return;
        var font = Game1.smallFont;
        int lineHeight = (int)font.MeasureString("Ay").Y;
        var safe = Utility.getSafeArea();
        // A question is drawn upwards from the popup's own y by however much its answers need.
        int top = popup.y - (popup.heightForQuestions - popup.height);
        var box = new Box(popup.x, top, popup.width, popup.heightForQuestions);
        // The game's little question bubble sits above the popup's top-right corner.
        var bubble = new Box(popup.x + popup.width - 72, top - 88, 68, 76);
        string head = view.Headline;
        var place = WaitingFacesLayout.Place(new Box(safe.X, safe.Y, safe.Width, safe.Height), box, view.Farmers.Count, (int)font.MeasureString(head).X, lineHeight, bubble);
        if (place is null) return;

        var panel = place.Panel;
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, Frame, panel.X, panel.Y, panel.Width, panel.Height, Color.White, 2f, drawShadow: true);
        string shownHead = Fit(head, font, panel.Width - WaitingFacesLayout.Pad * 2);
        Utility.drawTextWithShadow(b, shownHead, font, new Vector2(panel.X + (panel.Width - font.MeasureString(shownHead).X) / 2, panel.Y + WaitingFacesLayout.Pad), Game1.textColor);
        for (int i = 0; i < place.Cells.Count; i++)
        {
            var f = view.Farmers[i];
            var cell = place.Cells[i];
            DrawFace(b, f, new Box(cell.X + (cell.Width - WaitingFacesLayout.Face) / 2, cell.Y, WaitingFacesLayout.Face, WaitingFacesLayout.Face), marks: true);
            // The popup is already speaking to this player, so a name is just the name here:
            // "Alice (you)" did not fit under a face and was cut to "Alice (y...".
            string name = Fit(f.Name, font, cell.Width);
            Utility.drawTextWithShadow(b, name, font, new Vector2(cell.X + (cell.Width - font.MeasureString(name).X) / 2, cell.Y + WaitingFacesLayout.Face + 6),
                f.Mark is BoardMark.Done or BoardMark.SittingOut ? Game1.textColor * 0.65f : Game1.textColor);
        }
        if (place.More is { } more)
        {
            string text = "+" + place.Hidden;
            var size = font.MeasureString(text);
            Utility.drawTextWithShadow(b, text, font, new Vector2(more.X + (more.Width - size.X) / 2, more.Y + (WaitingFacesLayout.Face - size.Y) / 2 + 2), Game1.textColor);
        }
        // The popup drew the pointer before this panel did; put it back on top.
        popup.drawMouse(b);
    }

    /// <summary>A farmer's head in a square cell (40 pixels on the board, 64 above the popup), with a mark for where their day stands.</summary>
    private void DrawFace(SpriteBatch b, BoardFarmer f, Box cell, bool marks)
    {
        // Everything is sized from the cell, so the face is the same picture at either size.
        float k = cell.Width / 40f;
        float alpha = f.Mark is BoardMark.Done or BoardMark.SittingOut ? 0.55f : f.Online ? 1f : 0.8f;
        // Whose turn it is right now: a slow gold glow behind the face.
        if (f.Mark == BoardMark.Playing)
        {
            float pulse = 0.35f + 0.25f * (float)Math.Sin(Game1.currentGameTime.TotalGameTime.TotalSeconds * 3.0);
            b.Draw(Game1.staminaRect, new Rectangle(cell.X - 3, cell.Y - 3, cell.Width + 6, cell.Height + 6), new Color(255, 214, 90) * pulse);
        }
        b.Draw(Game1.staminaRect, new Rectangle(cell.X, cell.Y, cell.Width, cell.Height), Game1.textColor * 0.12f);

        var farmer = f.Id == 0 ? null : Game1.getAllFarmers().FirstOrDefault(p => p.UniqueMultiplayerID == f.Id);
        bool drawn = false;
        if (farmer is not null && !faceDrawFailed)
        {
            try
            {
                // Their face as the farm sees them (0.1.44): the game's own small portrait, with the
                // accessory and the hat it leaves out. The picture is 20 sprite pixels square, so a
                // hat has its room and the head keeps the middle of the cell.
                MiniFace.Draw(b, farmer, new Vector2(cell.X, cell.Y), cell.Width / (float)MiniFace.Span, alpha);
                drawn = true;
            }
            catch (Exception ex)
            {
                // A mod that replaces the farmer's renderer must not take the board down with it.
                faceDrawFailed = true;
                monitor.Log("The turn board could not draw a farmer's face and shows initials instead. " + ex.GetType().Name, LogLevel.Warn);
            }
        }
        if (!drawn)
        {
            string initial = f.Name.Length > 0 ? char.ToUpperInvariant(f.Name[0]).ToString() : "?";
            var size = Game1.dialogueFont.MeasureString(initial) * 0.7f * k;
            Utility.drawTextWithShadow(b, initial, Game1.dialogueFont, new Vector2(cell.X + (cell.Width - size.X) / 2, cell.Y + (cell.Height - size.Y) / 2 + 2), Game1.textColor * alpha, 0.7f * k);
        }
        if (!marks) return;
        // A mark is only drawn when it SAYS something: a tick for a day already played, a cross for
        // a farmer sitting today out. A farmer who has yet to play carries no mark at all - that is
        // the ordinary state of the board, and an empty box on every face was read as decoration
        // rather than as meaning (the owner, 2026-10-06: "just a square next to face?"). Whose turn it
        // is now is already said louder by the gold glow, and the open panel spells all of it out.
        if (f.Mark is not (BoardMark.Done or BoardMark.SittingOut)) return;
        var mark = f.Mark == BoardMark.Done ? Ticked : Cross;
        float scale = (f.Mark == BoardMark.SittingOut ? 1.5f : 2f) * k;
        b.Draw(Game1.mouseCursors, new Vector2(cell.Right - mark.Width * scale + 4 * k, cell.Bottom - mark.Height * scale + 4 * k), mark, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.9f);
    }

    private static string Label(BoardFarmer f) => f.IsYou ? f.Name + " (you)" : f.Name;

    private static string Fit(string text, SpriteFont font, int width)
    {
        if (width <= 0 || font.MeasureString(text).X <= width) return text;
        while (text.Length > 1 && font.MeasureString(text + "...").X > width) text = text[..^1];
        return text.TrimEnd() + "...";
    }
}

public sealed class HudConfig
{
    /// <summary>The turn board on screen while playing: every farmer's face and whether they have taken today's turn.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The corner of the screen it lives in: TopLeft, BottomLeft, TopRight or BottomRight.</summary>
    public BoardCorner Corner { get; set; } = BoardCorner.TopLeft;

    /// <summary>Moves it in from that corner, in UI pixels, if another mod's icons sit there.</summary>
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }

    /// <summary>Also open the board by itself for a few seconds when something on it changes. Off: it opens under the pointer only.</summary>
    public bool OpenOnChange { get; set; }

    /// <summary>Cover the whole screen behind Shared Day's own popups with a patterned backdrop, so the popup is all there is to look at.</summary>
    public bool PopupBackdrop { get; set; } = true;
}

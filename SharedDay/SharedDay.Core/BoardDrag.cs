namespace SharedDay.Core;

/// <summary>Where a board rests: the corner it belongs to, and how far in from that corner.</summary>
public sealed record HudAnchor(BoardCorner Corner, int OffsetX, int OffsetY);

/// <summary>
/// Moving the turn board with the mouse, while the pointer is free (no menu is open).
///
/// A press on the board that does not move is a click, and opens Shared Day's menu as it always
/// has. A press that moves more than a few pixels is a drag: the board follows the pointer, and
/// where it is let go becomes its corner and offsets, saved so it stays there next time.
///
/// Plain arithmetic, no game types, so the whole of it is tested without the game.
/// </summary>
public sealed class BoardDrag
{
    /// <summary>How far the pointer must move before a press counts as a drag and not a click.</summary>
    public const int Slop = 6;

    private Box screen;
    private Box strip;
    private int startX;
    private int startY;
    private bool pressed;

    /// <summary>True once the pointer has moved far enough: the board is being carried.</summary>
    public bool Dragging { get; private set; }

    /// <summary>Where the board sits right now while it is being carried; null when it is not.</summary>
    public HudAnchor? Anchor { get; private set; }

    /// <summary>The left button went down on the board, which was drawn at <paramref name="strip"/>.</summary>
    public void Press(Box screen, Box strip, int x, int y)
    {
        this.screen = screen;
        this.strip = strip;
        startX = x;
        startY = y;
        pressed = true;
        Dragging = false;
        Anchor = null;
    }

    /// <summary>
    /// The pointer moved while the button is down. Returns true once this is a drag, so the caller
    /// knows the press is no longer a click. The board keeps the grip it was picked up by and is
    /// never carried off the screen.
    /// </summary>
    public bool MoveTo(int x, int y)
    {
        if (!pressed) return false;
        if (!Dragging && Math.Abs(x - startX) + Math.Abs(y - startY) < Slop) return false;
        Dragging = true;
        int left = Math.Clamp(strip.X + (x - startX), screen.X, Math.Max(screen.X, screen.Right - strip.Width));
        int top = Math.Clamp(strip.Y + (y - startY), screen.Y, Math.Max(screen.Y, screen.Bottom - strip.Height));
        Anchor = TurnBoardLayout.AnchorFor(screen, new Box(left, top, strip.Width, strip.Height));
        return true;
    }

    /// <summary>
    /// The button came up. Returns where to save the board when it was dragged, or null when the
    /// press never moved and is therefore a click for the caller to act on.
    /// </summary>
    public HudAnchor? Release()
    {
        var anchor = Dragging ? Anchor : null;
        Forget();
        return anchor;
    }

    /// <summary>Drop the board where it was picked up: the window lost the button, or the board left the screen.</summary>
    public void Forget()
    {
        pressed = false;
        Dragging = false;
        Anchor = null;
    }
}

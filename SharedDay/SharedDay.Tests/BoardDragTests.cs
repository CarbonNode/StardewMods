using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>Moving the turn board with the mouse: what is a click, what is a drag, and where a dropped board is saved.</summary>
public sealed class BoardDragTests
{
    private static readonly Box Screen = new(0, 0, 1280, 720);

    /// <summary>The strip as the board itself would draw it in a corner, so the tests drag the real thing.</summary>
    private static Box StripAt(BoardCorner corner, int ox = 0, int oy = 0, int count = 3) =>
        TurnBoardLayout.Place(Screen, count, open: false, corner, panelWidth: 340, headHeight: 64, ox, oy).Strip;

    [Fact]
    public void A_press_that_does_not_move_is_a_click_and_not_a_drag()
    {
        var drag = new BoardDrag();
        drag.Press(Screen, StripAt(BoardCorner.TopLeft), 40, 40);

        // A hand never holds quite still: a pixel or two is still a click.
        Assert.False(drag.MoveTo(42, 41));
        Assert.False(drag.Dragging);
        // Release says "no anchor", which is the caller's cue to open the menu instead.
        Assert.Null(drag.Release());
    }

    [Fact]
    public void A_press_that_moves_becomes_a_drag_and_the_board_follows_the_pointer()
    {
        var drag = new BoardDrag();
        var strip = StripAt(BoardCorner.TopLeft);
        drag.Press(Screen, strip, 40, 40);

        Assert.True(drag.MoveTo(40 + BoardDrag.Slop, 40));
        Assert.True(drag.Dragging);
        Assert.NotNull(drag.Anchor);
    }

    [Fact]
    public void A_board_dragged_to_a_far_corner_is_saved_in_that_corner()
    {
        var drag = new BoardDrag();
        var strip = StripAt(BoardCorner.TopLeft);
        drag.Press(Screen, strip, strip.X + 10, strip.Y + 10);
        // Carry it to the bottom right of the screen.
        drag.MoveTo(Screen.Right - 10, Screen.Bottom - 10);
        var saved = drag.Release();

        Assert.NotNull(saved);
        Assert.Equal(BoardCorner.BottomRight, saved!.Corner);
        // Pressed 10 pixels in from the strip's own corner, dropped 10 from the screen's: it ends
        // flush in the corner, so the saved offsets are zero rather than negative.
        Assert.Equal(0, saved.OffsetX);
        Assert.Equal(0, saved.OffsetY);
    }

    [Fact]
    public void A_dropped_board_is_drawn_again_exactly_where_it_was_let_go()
    {
        // The whole point of the saved anchor: Place must put the strip back on the same pixels.
        var drag = new BoardDrag();
        var strip = StripAt(BoardCorner.TopLeft);
        drag.Press(Screen, strip, strip.X, strip.Y);
        drag.MoveTo(strip.X + 300, strip.Y + 200);
        var dropped = drag.Anchor!;
        var saved = drag.Release();
        Assert.Equal(dropped, saved);

        var back = TurnBoardLayout.Place(Screen, 3, open: false, saved!.Corner, 340, 64, saved.OffsetX, saved.OffsetY).Strip;
        Assert.Equal(strip.X + 300, back.X);
        Assert.Equal(strip.Y + 200, back.Y);
    }

    [Theory]
    [InlineData(BoardCorner.TopLeft)]
    [InlineData(BoardCorner.BottomLeft)]
    [InlineData(BoardCorner.TopRight)]
    [InlineData(BoardCorner.BottomRight)]
    public void An_anchor_read_back_from_an_undragged_board_leaves_it_where_it_is(BoardCorner corner)
    {
        // AnchorFor is Place's inverse: reading a resting board and placing it again must not move it.
        var strip = StripAt(corner, ox: 24, oy: 12);
        var anchor = TurnBoardLayout.AnchorFor(Screen, strip);
        var again = TurnBoardLayout.Place(Screen, 3, open: false, anchor.Corner, 340, 64, anchor.OffsetX, anchor.OffsetY).Strip;

        Assert.Equal(strip.X, again.X);
        Assert.Equal(strip.Y, again.Y);
    }

    [Fact]
    public void The_board_cannot_be_carried_off_the_screen()
    {
        var drag = new BoardDrag();
        var strip = StripAt(BoardCorner.TopLeft);
        drag.Press(Screen, strip, strip.X, strip.Y);
        // Far past the bottom right of the screen, and far past the top left.
        drag.MoveTo(Screen.Right + 4000, Screen.Bottom + 4000);
        var corner = TurnBoardLayout.Place(Screen, 3, false, drag.Anchor!.Corner, 340, 64, drag.Anchor.OffsetX, drag.Anchor.OffsetY).Strip;
        Assert.True(Screen.Contains(corner), "a board dragged past the edge stays on screen");

        drag.MoveTo(-4000, -4000);
        var other = TurnBoardLayout.Place(Screen, 3, false, drag.Anchor!.Corner, 340, 64, drag.Anchor.OffsetX, drag.Anchor.OffsetY).Strip;
        Assert.True(Screen.Contains(other), "and so does one dragged the other way");
    }

    [Fact]
    public void A_drag_keeps_the_grip_it_was_picked_up_by()
    {
        var drag = new BoardDrag();
        var strip = StripAt(BoardCorner.TopLeft);
        // Picked up well inside the strip, not by its corner.
        int gripX = strip.X + 37, gripY = strip.Y + 11;
        drag.Press(Screen, strip, gripX, gripY);
        drag.MoveTo(gripX + 120, gripY + 90);
        var a = drag.Anchor!;
        var moved = TurnBoardLayout.Place(Screen, 3, false, a.Corner, 340, 64, a.OffsetX, a.OffsetY).Strip;

        // The point under the pointer is the same point of the board it was grabbed by.
        Assert.Equal(gripX + 120 - (gripX - strip.X), moved.X);
        Assert.Equal(gripY + 90 - (gripY - strip.Y), moved.Y);
    }

    [Fact]
    public void Nothing_happens_without_a_press_on_the_board()
    {
        var drag = new BoardDrag();
        Assert.False(drag.MoveTo(500, 500));
        Assert.False(drag.Dragging);
        Assert.Null(drag.Anchor);
        Assert.Null(drag.Release());
    }

    [Fact]
    public void A_forgotten_drag_saves_nothing()
    {
        // The window lost the button, so the board goes back to its saved place and nothing is written.
        var drag = new BoardDrag();
        var strip = StripAt(BoardCorner.TopLeft);
        drag.Press(Screen, strip, strip.X, strip.Y);
        drag.MoveTo(strip.X + 200, strip.Y + 200);
        Assert.True(drag.Dragging);

        drag.Forget();
        Assert.False(drag.Dragging);
        Assert.Null(drag.Anchor);
        Assert.Null(drag.Release());
    }

    [Fact]
    public void A_second_press_starts_a_fresh_drag()
    {
        var drag = new BoardDrag();
        var strip = StripAt(BoardCorner.TopLeft);
        drag.Press(Screen, strip, strip.X, strip.Y);
        drag.MoveTo(strip.X + 200, strip.Y + 200);
        drag.Release();

        drag.Press(Screen, strip, strip.X, strip.Y);
        Assert.False(drag.Dragging);
        Assert.Null(drag.Anchor);
        Assert.Null(drag.Release());
    }
}

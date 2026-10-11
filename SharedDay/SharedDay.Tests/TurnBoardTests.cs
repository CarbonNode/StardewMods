using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>The in-game turn board: what each face says, the line above them, and where it all sits on any screen.</summary>
public sealed class TurnBoardTests
{
    private static RosterFarmer F(long id, string name, FarmerStatus status, bool online = false, bool inTurn = false) => new(id, name, status, online, inTurn);

    [Fact]
    public void Each_face_says_whether_that_farmer_has_played_today()
    {
        var board = TurnBoardView.Build(new[]
        {
            F(1, "Alice", FarmerStatus.Completed),
            F(2, "Bob", FarmerStatus.Pending, online: true, inTurn: true),
            F(3, "Cleo", FarmerStatus.Pending),
            F(4, "Dan", FarmerStatus.Skipped),
        }, you: 3, released: false);

        Assert.Equal(new[] { BoardMark.Done, BoardMark.Playing, BoardMark.Waiting, BoardMark.SittingOut }, board.Farmers.Select(f => f.Mark));
        Assert.Equal(new[] { "Alice", "Bob", "Cleo", "Dan" }, board.Farmers.Select(f => f.Name));
        Assert.True(board.Farmers[2].IsYou);
        Assert.Equal("Bob is playing", board.Headline);
        // Dan sits today out, so the day is owed three turns, not four.
        Assert.Equal("1 of 3 played", board.Count);
        Assert.Equal(new[] { "Played today", "Playing now", "Still to play", "Sitting today out" }, board.Farmers.Select(f => f.Words));
    }

    [Fact]
    public void The_headline_speaks_to_the_player_reading_it()
    {
        var mine = TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Completed), F(2, "Bob", FarmerStatus.Pending, true, true) }, you: 2, released: false);
        Assert.Equal("It's your turn", mine.Headline);
        Assert.Equal("Your turn", mine.Farmers[1].Words);

        var both = TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Pending, true, true), F(2, "Bob", FarmerStatus.Pending, true, true) }, you: 2, released: false);
        Assert.Equal("You and Alice are playing", both.Headline);

        var nobodyHere = TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Completed), F(2, "Bob", FarmerStatus.Pending), F(3, "Cleo", FarmerStatus.Pending) }, you: 1, released: false);
        Assert.Equal("Waiting on Bob and Cleo", nobodyHere.Headline);
        Assert.Equal("Still to play, not here yet", nobodyHere.Farmers[1].Words);

        // A farmer who is in the farm but joined after the turn began is waiting, not playing.
        var late = TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Pending, online: true, inTurn: false) }, you: 9, released: false);
        Assert.Equal(BoardMark.Waiting, late.Farmers[0].Mark);

        var done = TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Completed), F(2, "Bob", FarmerStatus.Skipped) }, you: 1, released: false);
        Assert.Equal("Everyone has played today", done.Headline);
        Assert.Equal("1 of 1 played", done.Count);
        Assert.Equal("Everyone has played. The day is ending", TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Completed) }, 1, released: true).Headline);

        var many = TurnBoardView.Build(Enumerable.Range(1, 6).Select(i => F(i, "F" + i, FarmerStatus.Pending)), you: 0, released: false);
        Assert.Equal("Waiting on F1, F2 and 4 more", many.Headline);
        Assert.Equal("No farmers on today's roster yet", TurnBoardView.Build(Array.Empty<RosterFarmer>(), 1, false).Headline);
    }

    [Fact]
    public void A_farm_nobody_has_joined_still_has_a_board_with_one_tile_for_the_farm()
    {
        var empty = TurnBoardView.Build(Array.Empty<RosterFarmer>(), you: 0, released: false);
        var tiles = empty.Tiles("Squeet Farm");
        var tile = Assert.Single(tiles);
        Assert.True(tile.StandsForFarm);
        Assert.Equal("Squeet Farm", tile.Name);
        Assert.Equal("Nobody has joined yet", tile.Words);
        Assert.False(tile.IsYou);
        Assert.Equal("The farm", Assert.Single(empty.Tiles(" ")).Name);
        // The same from a host too old to send its roster, and it is never counted as a farmer.
        Assert.Single(TurnBoardView.FromNames(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "Alice", false).Tiles(null));
        Assert.Empty(empty.Farmers);
        Assert.Equal("", empty.Count);

        // With anyone on the roster the tiles are the farmers, untouched.
        var one = TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Pending) }, you: 1, released: false);
        Assert.Same(one.Farmers, one.Tiles("Squeet Farm"));
        Assert.False(one.Farmers[0].StandsForFarm);
    }

    [Fact]
    public void A_nameless_farmer_still_gets_a_face_and_the_host_account_zero_is_never_you()
    {
        var board = TurnBoardView.Build(new[] { F(0, " ", FarmerStatus.Pending) }, you: 0, released: false);
        Assert.Equal("A farmer", board.Farmers[0].Name);
        Assert.False(board.Farmers[0].IsYou);
    }

    [Fact]
    public void The_signature_changes_only_when_the_board_does()
    {
        var a = TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Pending, true, true), F(2, "Bob", FarmerStatus.Pending) }, 1, false);
        var same = TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Pending, true, true), F(2, "Bob", FarmerStatus.Pending) }, 2, false);
        var finished = TurnBoardView.Build(new[] { F(1, "Alice", FarmerStatus.Completed, true, true), F(2, "Bob", FarmerStatus.Pending) }, 1, false);
        Assert.Equal(a.Signature, same.Signature);
        Assert.NotEqual(a.Signature, finished.Signature);
    }

    [Fact]
    public void An_older_host_that_sends_only_names_still_fills_the_board()
    {
        var board = TurnBoardView.FromNames(waiting: new[] { "Bob", "Cleo" }, finished: new[] { "Alice" }, skipped: new[] { "Dan" }, yourName: "bob", released: false);
        Assert.Equal(new[] { "Alice", "Bob", "Cleo", "Dan" }, board.Farmers.Select(f => f.Name));
        Assert.Equal(new[] { BoardMark.Done, BoardMark.Waiting, BoardMark.Waiting, BoardMark.SittingOut }, board.Farmers.Select(f => f.Mark));
        Assert.True(board.Farmers[1].IsYou);
        Assert.Equal("Waiting on you and Cleo", board.Headline);
        Assert.Equal("1 of 3 played", board.Count);
        // A name a host listed twice is one farmer.
        Assert.Single(TurnBoardView.FromNames(new[] { "Bob" }, new[] { "bob" }, Array.Empty<string>(), null, false).Farmers);
    }

    private static readonly (int W, int H)[] Screens =
    {
        (640, 480), (1024, 768), (1280, 720), (1366, 768), (1920, 1080), (2560, 1440), (3840, 2160)
    };

    [Fact]
    public void Closed_or_open_the_board_stays_on_screen_in_every_corner_at_every_size()
    {
        foreach (var (w, h) in Screens)
        foreach (var corner in Enum.GetValues<BoardCorner>())
        foreach (int count in new[] { 1, 2, 4, 6, 7, 12, 32 })
        foreach (bool open in new[] { false, true })
        foreach (var (ox, oy) in new[] { (0, 0), (200, 90), (-50, -50), (9000, 9000) })
        {
            var screen = new Box(0, 0, w, h);
            var p = TurnBoardLayout.Place(screen, count, open, corner, panelWidth: 340, headHeight: 64, ox, oy);
            string where = $"{w}x{h} {corner} count={count} open={open} offset={ox},{oy}";
            Assert.True(screen.Contains(p.Strip), where);
            Assert.All(p.Faces, f => Assert.True(p.Strip.Contains(f), where));
            if (p.More is { } more) Assert.True(p.Strip.Contains(more), where);
            // Every farmer is either a face or counted in "+N".
            Assert.Equal(count, p.Faces.Count + p.Hidden);
            Assert.True(p.Faces.Count + (p.More is null ? 0 : 1) <= TurnBoardLayout.MaxFaces, where);
            if (!open) { Assert.Null(p.Panel); continue; }
            Assert.NotNull(p.Panel);
            Assert.True(screen.Contains(p.Panel!.Value), where);
            Assert.All(p.Rows, r => Assert.True(p.Panel!.Value.Contains(r), where));
            Assert.InRange(p.Rows.Count, 1, count);
        }
    }

    [Fact]
    public void The_panel_opens_beside_the_standing_strip_and_does_not_cover_it()
    {
        var screen = new Box(0, 0, 1920, 1080);
        foreach (var corner in Enum.GetValues<BoardCorner>())
        {
            var p = TurnBoardLayout.Place(screen, 4, open: true, corner, 340, 64);
            Assert.False(p.Panel!.Value.Intersects(p.Strip), corner.ToString());
            // The strip stands up, so its panel opens BESIDE it, away from the nearer edge.
            bool right = corner is BoardCorner.TopRight or BoardCorner.BottomRight;
            Assert.True(right ? p.Panel.Value.Right <= p.Strip.X : p.Panel.Value.X >= p.Strip.Right, corner.ToString());
            Assert.Equal(4, p.Rows.Count);
            // The pointer can cross from the strip to the panel without the board closing under it.
            int seamX = right ? p.Strip.X - 2 : p.Strip.Right + 2;
            Assert.True(p.Contains(seamX, p.Strip.Y + 20), corner.ToString());
            Assert.False(p.Contains(screen.Width / 2, screen.Height / 2));
        }
    }

    [Fact]
    public void The_strip_steps_past_the_toolbar_whichever_edge_it_is_on()
    {
        // A 1280x720 window at the game's 150% UI scale: the HUD's area is 853x480 UI pixels.
        var screen = new Box(0, 0, 853, 480);
        // The game's toolbar: 896 UI pixels wide and centred (so on a HUD this narrow it reaches both
        // corners), at the top when the player is low on the screen, else at the bottom.
        var top = new Box(screen.Width / 2 - 448, 8, 896, 90);
        var bottom = new Box(screen.Width / 2 - 448, screen.Height - 8 - 90, 896, 90);
        Assert.True(top.Intersects(TurnBoardLayout.Place(screen, 2, false, BoardCorner.TopLeft, 340, 64).Strip), "the fixture's toolbar must reach the corner");
        var topLeft = TurnBoardLayout.Place(screen, 2, open: true, BoardCorner.TopLeft, 340, 64, avoid: top);
        Assert.False(topLeft.Strip.Intersects(top));
        Assert.True(topLeft.Strip.Y >= top.Bottom);
        Assert.False(topLeft.Panel!.Value.Intersects(topLeft.Strip));
        Assert.True(screen.Contains(topLeft.Panel.Value));
        // The toolbar at the bottom does not touch a strip at the top.
        Assert.Equal(TurnBoardLayout.Margin, TurnBoardLayout.Place(screen, 2, false, BoardCorner.TopLeft, 340, 64, avoid: bottom).Strip.Y);
        var bottomRight = TurnBoardLayout.Place(screen, 6, open: true, BoardCorner.BottomRight, 340, 64, avoid: bottom);
        Assert.False(bottomRight.Strip.Intersects(bottom));
        Assert.True(bottomRight.Strip.Bottom <= bottom.Y);
        Assert.True(screen.Contains(bottomRight.Panel!.Value));
        // On a wide HUD the centred toolbar never reaches the corner, and the strip is left where it was.
        var wide = new Box(0, 0, 1920, 1080);
        var wideTop = new Box(wide.Width / 2 - 448, 8, 896, 90);
        Assert.Equal(TurnBoardLayout.Place(wide, 1, false, BoardCorner.TopLeft, 340, 64).Strip, TurnBoardLayout.Place(wide, 1, false, BoardCorner.TopLeft, 340, 64, avoid: wideTop).Strip);
    }

    [Fact]
    public void A_long_roster_folds_into_a_count_and_a_short_screen_shows_the_rows_that_fit()
    {
        var screen = new Box(0, 0, 1280, 720);
        var p = TurnBoardLayout.Place(screen, 9, open: true, BoardCorner.TopLeft, 340, 64);
        Assert.Equal(5, p.Faces.Count);
        Assert.Equal(4, p.Hidden);
        Assert.NotNull(p.More);
        var six = TurnBoardLayout.Place(screen, 6, open: false, BoardCorner.TopLeft, 340, 64);
        Assert.Equal(6, six.Faces.Count);
        Assert.Null(six.More);
        // 32 farmers do not fit under the strip on a 720-pixel screen: the panel shows what does.
        var tall = TurnBoardLayout.Place(screen, 32, open: true, BoardCorner.TopLeft, 340, 64);
        Assert.True(tall.Rows.Count < 32);
        Assert.True(screen.Contains(tall.Panel!.Value));
    }

    [Fact]
    public void The_strip_stands_up_one_face_above_the_next()
    {
        var screen = new Box(0, 0, 1920, 1080);
        var p = TurnBoardLayout.Place(screen, 4, open: false, BoardCorner.TopLeft, 340, 64);

        // One face wide, four tall: a column, not a row.
        Assert.Equal(TurnBoardLayout.Pad * 2 + TurnBoardLayout.Face, p.Strip.Width);
        Assert.Equal(4, p.Faces.Count);
        Assert.All(p.Faces, f => Assert.Equal(p.Faces[0].X, f.X));
        for (int i = 1; i < p.Faces.Count; i++)
            Assert.True(p.Faces[i].Y > p.Faces[i - 1].Y, "face " + i + " sits below the one before it");
        // Every face is inside the strip that frames them.
        Assert.All(p.Faces, f => Assert.True(p.Strip.Contains(f), "a face escaped the strip"));
    }

    [Fact]
    public void A_long_roster_folds_into_a_plus_box_at_the_foot_of_the_column()
    {
        var screen = new Box(0, 0, 1920, 1080);
        var p = TurnBoardLayout.Place(screen, 12, open: false, BoardCorner.TopLeft, 340, 64);

        Assert.NotNull(p.More);
        Assert.True(p.Hidden > 0);
        Assert.Equal(p.Faces[0].X, p.More!.Value.X);
        Assert.True(p.More.Value.Y > p.Faces[^1].Y, "the +N box is the last cell in the column");
        Assert.True(p.Strip.Contains(p.More.Value), "the +N box is inside the strip");
    }

    [Fact]
    public void A_standing_strip_stays_on_screen_even_with_a_roster_taller_than_the_window()
    {
        // A short window and a full roster: the column must not hang off the bottom.
        foreach (var corner in Enum.GetValues<BoardCorner>())
        {
            var screen = new Box(0, 0, 640, 360);
            var p = TurnBoardLayout.Place(screen, 6, open: true, corner, 340, 64);
            Assert.True(screen.Contains(p.Strip), corner + " strip left the screen");
            Assert.True(screen.Contains(p.Panel!.Value), corner + " panel left the screen");
        }
    }

    [Fact]
    public void The_faces_sit_centred_above_the_waiting_popup_and_never_on_it()
    {
        var screen = new Box(0, 0, 1280, 720);
        var popup = new Box(40, 420, 1200, 236);
        var p = WaitingFacesLayout.Place(screen, popup, count: 3, headWidth: 260, lineHeight: 24)!;
        Assert.Equal(3, p.Cells.Count);
        Assert.Null(p.More);
        Assert.Equal(0, p.Hidden);
        // Centred on the screen, wholly above the popup with the gap between them.
        Assert.Equal(screen.Width, p.Panel.X * 2 + p.Panel.Width);
        Assert.Equal(popup.Y - WaitingFacesLayout.Above, p.Panel.Bottom);
        Assert.All(p.Cells, c => Assert.True(c.X >= p.Panel.X && c.Right <= p.Panel.Right && c.Y > p.Panel.Y && c.Bottom < p.Panel.Bottom));
        Assert.Equal(WaitingFacesLayout.Cell + WaitingFacesLayout.Gap, p.Cells[1].X - p.Cells[0].X);

        // A long headline widens the panel; the faces stay centred in it.
        var wide = WaitingFacesLayout.Place(screen, popup, 1, headWidth: 500, lineHeight: 24)!;
        Assert.Equal(500 + WaitingFacesLayout.Pad * 2, wide.Panel.Width);
        Assert.Equal(wide.Panel.X * 2 + wide.Panel.Width, wide.Cells[0].X * 2 + wide.Cells[0].Width);

        // Nobody to show, or no room above the popup: nothing is drawn rather than covering its words.
        Assert.Null(WaitingFacesLayout.Place(screen, popup, 0, 260, 24));
        Assert.Null(WaitingFacesLayout.Place(screen, new Box(40, 120, 1200, 536), 3, 260, 24));
    }

    [Fact]
    public void A_long_roster_folds_into_plus_N_and_the_panel_clears_the_games_own_question_bubble()
    {
        var screen = new Box(0, 0, 1024, 768);
        var popup = new Box(32, 500, 960, 204);
        var many = WaitingFacesLayout.Place(screen, popup, count: 20, headWidth: 300, lineHeight: 24)!;
        Assert.NotNull(many.More);
        Assert.Equal(20, many.Cells.Count + many.Hidden);
        Assert.True(many.Hidden > 0);
        Assert.True(many.Panel.X >= screen.X + WaitingFacesLayout.Margin && many.Panel.Right <= screen.Right - WaitingFacesLayout.Margin);
        Assert.True(many.More!.Value.Right <= many.Panel.Right);

        // The bubble the game draws above a question's top-right corner: a panel wide enough to
        // reach it goes above it; a narrow one in the middle stays just above the popup.
        var bubble = new Box(popup.Right - 72, popup.Y - 88, 68, 76);
        var over = WaitingFacesLayout.Place(screen, popup, 20, 300, 24, bubble)!;
        Assert.Equal(bubble.Y - WaitingFacesLayout.Above, over.Panel.Bottom);
        var narrow = WaitingFacesLayout.Place(screen, popup, 2, 200, 24, bubble)!;
        Assert.Equal(popup.Y - WaitingFacesLayout.Above, narrow.Panel.Bottom);
    }
}

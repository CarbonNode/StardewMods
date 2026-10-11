namespace SharedDay.Core;

/// <summary>One farmer as the host tells every player about them, in the farm's own order.</summary>
public sealed record RosterFarmer(long Id, string Name, FarmerStatus Status, bool Online, bool InTurn);

/// <summary>What a farmer's face says on the in-game turn board.</summary>
public enum BoardMark
{
    /// <summary>Has not played today and is not taking a turn right now.</summary>
    Waiting,
    /// <summary>In the farm, taking their turn.</summary>
    Playing,
    /// <summary>Finished today.</summary>
    Done,
    /// <summary>Skipped today.</summary>
    SittingOut
}

public sealed record BoardFarmer(long Id, string Name, BoardMark Mark, bool Online, bool IsYou)
{
    /// <summary>Not a farmer: the tile that stands for the farm itself while nobody has joined it.</summary>
    public bool StandsForFarm { get; init; }

    /// <summary>The line beside the face when the board is open.</summary>
    public string Words => StandsForFarm ? "Nobody has joined yet" : Mark switch
    {
        BoardMark.Done => "Played today",
        BoardMark.SittingOut => "Sitting today out",
        BoardMark.Playing => IsYou ? "Your turn" : "Playing now",
        _ => IsYou ? "Still to play" : "Still to play, not here yet"
    };
}

/// <summary>
/// The in-game turn board: who is on the farm, who has taken today's turn and who the day is
/// still waiting on. Built from the host's status alone, so every player sees the same board.
/// </summary>
public sealed record TurnBoardView(IReadOnlyList<BoardFarmer> Farmers, string Headline, int Played, int Total)
{
    public static readonly TurnBoardView Empty = new(Array.Empty<BoardFarmer>(), "", 0, 0);

    /// <summary>"2 of 4 played".</summary>
    public string Count => Total == 0 ? "" : $"{Played} of {Total} played";

    /// <summary>
    /// What the board draws: the farmers, or, on a farm nobody has joined yet, one tile that
    /// stands for the farm. A board that drew nothing on an empty farm read as a board that had
    /// gone missing (the owner, 2026-10-06: "icon is gone in game again? Top left?"), and it took
    /// the click that opens Shared Day's menu with it.
    /// </summary>
    public IReadOnlyList<BoardFarmer> Tiles(string? farmName) => Farmers.Count > 0
        ? Farmers
        : new[] { new BoardFarmer(0, string.IsNullOrWhiteSpace(farmName) ? "The farm" : farmName.Trim(), BoardMark.Waiting, false, false) { StandsForFarm = true } };

    /// <summary>Changes exactly when something on the board changes; the board opens itself for a moment then.</summary>
    public string Signature => string.Join("|", Farmers.Select(f => $"{f.Id}:{f.Name}:{(int)f.Mark}"));

    public static TurnBoardView Build(IEnumerable<RosterFarmer> roster, long you, bool released)
    {
        var farmers = roster.Select(f => new BoardFarmer(f.Id, string.IsNullOrWhiteSpace(f.Name) ? "A farmer" : f.Name,
            f.Status switch
            {
                FarmerStatus.Completed => BoardMark.Done,
                FarmerStatus.Skipped => BoardMark.SittingOut,
                _ => f.Online && f.InTurn ? BoardMark.Playing : BoardMark.Waiting
            }, f.Online, f.Id == you && you != 0)).ToArray();
        int played = farmers.Count(f => f.Mark == BoardMark.Done);
        // Farmers who sit the day out are not owed a turn: the count is of those who are.
        int total = farmers.Count(f => f.Mark != BoardMark.SittingOut);
        return new TurnBoardView(farmers, HeadlineFor(farmers, released), played, total);
    }

    /// <summary>
    /// A board from a host too old to send its roster: names only, no faces to match and nobody
    /// known to be in the farm. Better than no board.
    /// </summary>
    public static TurnBoardView FromNames(IEnumerable<string> waiting, IEnumerable<string> finished, IEnumerable<string> skipped, string? yourName, bool released)
    {
        bool You(string n) => yourName is not null && string.Equals(n, yourName, StringComparison.OrdinalIgnoreCase);
        var farmers = finished.Select(n => new BoardFarmer(0, n, BoardMark.Done, false, You(n)))
            .Concat(waiting.Select(n => new BoardFarmer(0, n, BoardMark.Waiting, false, You(n))))
            .Concat(skipped.Select(n => new BoardFarmer(0, n, BoardMark.SittingOut, false, You(n))))
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
        return new TurnBoardView(farmers, HeadlineFor(farmers, released), farmers.Count(f => f.Mark == BoardMark.Done), farmers.Count(f => f.Mark != BoardMark.SittingOut));
    }

    private static string HeadlineFor(IReadOnlyList<BoardFarmer> farmers, bool released)
    {
        if (farmers.Count == 0) return "No farmers on today's roster yet";
        if (released) return "Everyone has played. The day is ending";
        var playing = farmers.Where(f => f.Mark == BoardMark.Playing).ToArray();
        var waiting = farmers.Where(f => f.Mark == BoardMark.Waiting).ToArray();
        if (playing.Length == 0 && waiting.Length == 0) return "Everyone has played today";
        if (playing.Any(f => f.IsYou)) return playing.Length == 1 ? "It's your turn" : $"You and {Names(playing.Where(f => !f.IsYou))} are playing";
        if (playing.Length > 0) return $"{Names(playing)} {(playing.Length == 1 ? "is" : "are")} playing";
        return $"Waiting on {Names(waiting, youWord: "you")}";
    }

    private static string Names(IEnumerable<BoardFarmer> farmers, string? youWord = null)
    {
        var list = farmers.Select(f => f.IsYou && youWord is not null ? youWord : f.Name).ToList();
        if (list.Count > 3) list = list.Take(2).Append($"{list.Count - 2} more").ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1]
        };
    }
}

public enum BoardCorner { TopLeft, BottomLeft, TopRight, BottomRight }

/// <summary>Where each part of the board is drawn. Plain arithmetic, so it is tested without the game.</summary>
public sealed record TurnBoardPlacement(Box Strip, IReadOnlyList<Box> Faces, Box? More, int Hidden, Box? Panel, IReadOnlyList<Box> Rows)
{
    /// <summary>The pointer is over the board: the strip, the open panel, or the gap between them.</summary>
    public bool Contains(int x, int y)
    {
        if (Strip.Contains(x, y)) return true;
        if (Panel is not { } p) return false;
        if (p.Contains(x, y)) return true;
        // The gap between the two: the panel sits beside the standing strip, to its right or left.
        int left = Math.Min(Strip.Right, p.Right), right = Math.Max(Strip.X, p.X);
        return x >= left && x < right && y >= Math.Min(Strip.Y, p.Y) && y < Math.Max(Strip.Bottom, p.Bottom);
    }
}

/// <summary>
/// The board's two shapes. Closed, it is a strip of faces small enough to live in a corner of
/// the screen all day. Under the pointer it opens a panel with one row per farmer. Both stay
/// on screen at any window size, and a long roster folds its tail into "+N".
/// </summary>
public static class TurnBoardLayout
{
    public const int Margin = 16;
    public const int Pad = 14;
    public const int Face = 40;
    public const int Gap = 8;
    public const int MaxFaces = 6;
    public const int Row = 48;
    public const int Seam = 6;

    /// <summary>
    /// The anchor that leaves the strip exactly where it now is: the inverse of <see cref="Place"/>'s
    /// corner arithmetic, for a board the player has dragged. The corner is whichever one the
    /// strip's middle is nearest, so the board then keeps its distance from the edges it was left
    /// by at any window size or UI scale. Offsets never go negative, so a board is always saved at
    /// or inside the margin.
    /// </summary>
    public static HudAnchor AnchorFor(Box screen, Box strip)
    {
        bool right = strip.X + strip.Width / 2 >= screen.X + screen.Width / 2;
        bool bottom = strip.Y + strip.Height / 2 >= screen.Y + screen.Height / 2;
        int offsetX = right ? screen.Right - Margin - strip.Width - strip.X : strip.X - screen.X - Margin;
        int offsetY = bottom ? screen.Bottom - Margin - strip.Height - strip.Y : strip.Y - screen.Y - Margin;
        var corner = (right, bottom) switch
        {
            (false, false) => BoardCorner.TopLeft,
            (false, true) => BoardCorner.BottomLeft,
            (true, false) => BoardCorner.TopRight,
            _ => BoardCorner.BottomRight,
        };
        return new HudAnchor(corner, Math.Max(0, offsetX), Math.Max(0, offsetY));
    }

    /// <param name="screen">The HUD's area in UI pixels.</param>
    /// <param name="count">Farmers on the board.</param>
    /// <param name="open">Whether the panel is showing.</param>
    /// <param name="corner">The corner of the screen the board lives in.</param>
    /// <param name="panelWidth">How wide the panel's longest line needs, padding included.</param>
    /// <param name="headHeight">Height of the panel's heading lines, above the rows.</param>
    /// <param name="offsetX">Moves the board in from its corner.</param>
    /// <param name="offsetY">Moves the board in from its corner.</param>
    /// <param name="avoid">Something the strip must not cover (the game's toolbar, which sits at the top or the bottom of the screen depending on where the player stands).</param>
    public static TurnBoardPlacement Place(Box screen, int count, bool open, BoardCorner corner, int panelWidth, int headHeight, int offsetX = 0, int offsetY = 0, Box? avoid = null)
    {
        int cells = Math.Max(1, Math.Min(count, MaxFaces));
        int hidden = count > MaxFaces ? count - (MaxFaces - 1) : 0;
        // The strip stands up rather than lying along the top: one face above the next. A column
        // sits in a corner without eating the width a player reads the rest of the HUD across, and
        // it grows into the screen's long side, so a big roster still fits.
        int stripW = Pad * 2 + Face;
        int stripH = Pad * 2 + cells * Face + (cells - 1) * Gap;
        bool right = corner is BoardCorner.TopRight or BoardCorner.BottomRight;
        bool bottom = corner is BoardCorner.BottomLeft or BoardCorner.BottomRight;
        int sx = right ? screen.Right - Margin - stripW - offsetX : screen.X + Margin + offsetX;
        int sy = bottom ? screen.Bottom - Margin - stripH - offsetY : screen.Y + Margin + offsetY;
        // Never off screen, whatever offset a player typed.
        sx = Math.Clamp(sx, screen.X, Math.Max(screen.X, screen.Right - stripW));
        sy = Math.Clamp(sy, screen.Y, Math.Max(screen.Y, screen.Bottom - stripH));
        var strip = new Box(sx, sy, stripW, stripH);
        // The toolbar in the way: step past it, towards the middle of the screen.
        if (avoid is { } bar && strip.Intersects(bar))
        {
            sy = bottom ? bar.Y - Seam - stripH : bar.Bottom + Seam;
            sy = Math.Clamp(sy, screen.Y, Math.Max(screen.Y, screen.Bottom - stripH));
            strip = new Box(sx, sy, stripW, stripH);
        }

        var faces = new List<Box>();
        int shownFaces = hidden > 0 ? MaxFaces - 1 : Math.Min(count, MaxFaces);
        for (int i = 0; i < shownFaces; i++) faces.Add(new Box(sx + Pad, sy + Pad + i * (Face + Gap), Face, Face));
        Box? more = hidden > 0 ? new Box(sx + Pad, sy + Pad + shownFaces * (Face + Gap), Face, Face) : null;
        if (!open || count == 0) return new TurnBoardPlacement(strip, faces, more, hidden, null, Array.Empty<Box>());

        // A standing strip opens its panel BESIDE itself, not under it: beneath a tall column there
        // is usually no room left, and reading across from the faces is the shorter eye movement.
        int roomRight = screen.Right - Margin - (strip.Right + Seam);
        int roomLeft = strip.X - Seam - (screen.X + Margin);
        bool toLeft = right ? roomLeft >= roomRight || roomRight < panelWidth : roomRight < panelWidth && roomLeft > roomRight;
        int side = toLeft ? roomLeft : roomRight;
        int panelW = Math.Clamp(panelWidth, Math.Min(panelWidth, side), Math.Max(1, side));
        // As many rows as the screen's height allows; at least one, and the heading with it.
        int tall = screen.Height - Margin * 2;
        int rows = Math.Min(count, Math.Max(1, (tall - Pad * 2 - headHeight) / Row));
        int panelH = Pad * 2 + headHeight + rows * Row;
        int px = toLeft ? strip.X - Seam - panelW : strip.Right + Seam;
        px = Math.Clamp(px, screen.X, Math.Max(screen.X, screen.Right - panelW));
        // Aligned with the top of the faces it describes, then kept on screen.
        int py = Math.Clamp(strip.Y, screen.Y, Math.Max(screen.Y, screen.Bottom - panelH));
        var panel = new Box(px, py, panelW, panelH);
        var rowBoxes = Enumerable.Range(0, rows).Select(i => new Box(px + Pad, py + Pad + headHeight + i * Row, panelW - Pad * 2, Row)).ToArray();
        return new TurnBoardPlacement(strip, faces, more, hidden, panel, rowBoxes);
    }
}

/// <summary>Where the faces above the waiting popup are drawn. <see cref="More"/> stands for the farmers that did not fit.</summary>
public sealed record WaitingFacesPlacement(Box Panel, IReadOnlyList<Box> Cells, Box? More, int Hidden);

/// <summary>
/// The waiting popup says who the day is waiting on in words; this puts their faces above it
/// (the owner, 2026-10-06: "says waiting for [show faces]"). One row, centred over the popup, every
/// farmer with the mark the turn board gives them and their name underneath. Plain arithmetic,
/// so it is tested without the game.
/// </summary>
public static class WaitingFacesLayout
{
    public const int Face = 64;
    /// <summary>Wide enough for a ten-letter name under the face without cutting it.</summary>
    public const int Cell = 120;
    public const int Gap = 8;
    public const int Pad = 16;
    public const int Margin = 16;
    /// <summary>The gap between the panel and the popup under it.</summary>
    public const int Above = 12;
    public const int MaxWidth = 960;

    /// <summary>
    /// Null when there is nobody to show, or no room above the popup: the popup's own words then
    /// carry it alone, and the panel never covers them. <paramref name="keepClear"/> is something
    /// the game draws above the popup (its question mark bubble); the panel goes above that too
    /// when the two would overlap.
    /// </summary>
    public static WaitingFacesPlacement? Place(Box screen, Box popup, int count, int headWidth, int lineHeight, Box? keepClear = null)
    {
        if (count <= 0) return null;
        int room = Math.Min(screen.Width - Margin * 2, MaxWidth) - Pad * 2;
        int fit = Math.Max(1, (room + Gap) / (Cell + Gap));
        int hidden = count > fit ? count - Math.Max(1, fit - 1) : 0;
        int shown = count - hidden;
        int tiles = shown + (hidden > 0 && fit > 1 ? 1 : 0);
        int rowWidth = tiles * Cell + (tiles - 1) * Gap;
        int width = Math.Max(rowWidth, Math.Min(headWidth, room)) + Pad * 2;
        int cellHeight = Face + 6 + lineHeight;
        int height = Pad + lineHeight + 8 + cellHeight + Pad;
        int x = screen.X + (screen.Width - width) / 2;
        int y = popup.Y - Above - height;
        if (keepClear is { } clear && x < clear.Right && x + width > clear.X && y + height > clear.Y) y = clear.Y - Above - height;
        if (y < screen.Y + Margin) return null;
        int cx = x + (width - rowWidth) / 2, cy = y + Pad + lineHeight + 8;
        var cells = Enumerable.Range(0, shown).Select(i => new Box(cx + i * (Cell + Gap), cy, Cell, cellHeight)).ToArray();
        Box? more = tiles > shown ? new Box(cx + shown * (Cell + Gap), cy, Cell, cellHeight) : null;
        return new WaitingFacesPlacement(new Box(x, y, width, height), cells, more, hidden);
    }
}

namespace SharedDay.Core;

/// <summary>An integer rectangle; the layout below is plain arithmetic so it can be tested without the game.</summary>
public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Intersects(Box other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
    public bool Contains(Box other) => other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
    public Box Inflate(int by) => new(X - by, Y - by, Width + by * 2, Height + by * 2);
}

public enum JoinButtonSpot
{
    /// <summary>Centred in the gap between the logo and the row of title buttons.</summary>
    BetweenLogoAndButtons,
    TopLeft,
    TopRight,
    BottomLeft,
    /// <summary>The compact sign in the margin beside the logo (wide logos on short windows).</summary>
    SideLeft,
    SideRight,
    /// <summary>Nothing fit at any size: the smallest compact sign in the top-left corner, kept on screen.</summary>
    Squeezed
}

public readonly record struct JoinButtonPlacement(Box Bounds, float Scale, JoinButtonSpot Spot)
{
    /// <summary>Two short lines ("Join" over the farm's name) instead of the full button.</summary>
    public bool Compact => Spot is JoinButtonSpot.SideLeft or JoinButtonSpot.SideRight or JoinButtonSpot.Squeezed;
}

/// <summary>
/// Where the title screen's join button goes. It must never cover a vanilla button at any
/// window size or UI scale and must stay on screen. The preferred spot is centred between
/// the logo and the button row; when that gap is too small (short windows) it moves to a
/// free corner, shrinking before it would overlap anything.
/// </summary>
public static class JoinButtonLayout
{
    public const int Margin = 12;
    public static readonly float[] CentreScales = { 1f, 0.85f, 0.7f };
    public static readonly float[] CornerScales = { 1f, 0.85f, 0.7f, 0.55f };

    /// <param name="screen">The title menu's area in UI pixels.</param>
    /// <param name="logo">The game logo once it has settled.</param>
    /// <param name="row">The main title buttons (New, Load, Co-op, Exit).</param>
    /// <param name="avoid">Every other clickable thing on the title screen.</param>
    /// <param name="measure">The full button's size at a given scale.</param>
    /// <param name="measureCompact">The compact sign's size at a given scale.</param>
    public static JoinButtonPlacement Place(Box screen, Box logo, IReadOnlyList<Box> row, IReadOnlyList<Box> avoid,
        Func<float, (int Width, int Height)> measure, Func<float, (int Width, int Height)> measureCompact)
    {
        var clickable = row.Concat(avoid).ToArray();
        var inside = screen.Inflate(-Margin);
        bool Free(Box box) => inside.Contains(box) && !box.Intersects(logo) && clickable.All(c => !box.Intersects(c.Inflate(Margin / 2)));

        if (row.Count > 0)
        {
            int rowTop = row.Min(r => r.Y);
            foreach (float scale in CentreScales)
            {
                var (w, h) = measure(scale);
                int bandTop = logo.Bottom + Margin, bandBottom = rowTop - Margin;
                var box = new Box(screen.X + (screen.Width - w) / 2, bandTop + (bandBottom - bandTop - h) / 2, w, h);
                if (Free(box)) return new(box, scale, JoinButtonSpot.BetweenLogoAndButtons);
            }
        }
        foreach (float scale in CornerScales)
        {
            var (w, h) = measure(scale);
            foreach (var spot in new[] { JoinButtonSpot.TopLeft, JoinButtonSpot.TopRight, JoinButtonSpot.BottomLeft })
            {
                var box = Corner(spot, screen, w, h, clickable);
                if (Free(box)) return new(box, scale, spot);
            }
        }
        // A wide logo on a short window leaves only the margins beside it.
        foreach (float scale in CornerScales)
        {
            var (w, h) = measureCompact(scale);
            int middle = (logo.Y + logo.Bottom) / 2 - h / 2;
            foreach (var spot in new[] { JoinButtonSpot.SideLeft, JoinButtonSpot.SideRight })
            {
                var box = new Box(spot == JoinButtonSpot.SideLeft ? screen.X + Margin : screen.Right - Margin - w, middle, w, h);
                if (Free(box)) return new(box, scale, spot);
            }
        }
        // Last resort: keep it on screen and off the buttons, even if it touches the logo.
        float smallest = CornerScales[^1];
        var (sw, sh) = measureCompact(smallest);
        var squeezed = Corner(JoinButtonSpot.TopLeft, screen, sw, sh, clickable);
        squeezed = squeezed with
        {
            X = Math.Clamp(squeezed.X, screen.X, Math.Max(screen.X, screen.Right - squeezed.Width)),
            Y = Math.Clamp(squeezed.Y, screen.Y, Math.Max(screen.Y, screen.Bottom - squeezed.Height))
        };
        return new(squeezed, smallest, JoinButtonSpot.Squeezed);
    }

    /// <summary>A corner position, pushed past any clickable thing already in that corner.</summary>
    private static Box Corner(JoinButtonSpot spot, Box screen, int w, int h, IReadOnlyList<Box> clickable)
    {
        bool top = spot != JoinButtonSpot.BottomLeft, left = spot != JoinButtonSpot.TopRight;
        var box = new Box(left ? screen.X + Margin : screen.Right - Margin - w, top ? screen.Y + Margin : screen.Bottom - Margin - h, w, h);
        // Move down (top corners) or up (bottom corner) until clear; a handful of steps covers the vanilla icons.
        for (int step = 0; step < 8; step++)
        {
            var hit = clickable.Where(c => box.Intersects(c.Inflate(Margin / 2))).ToArray();
            if (hit.Length == 0) break;
            box = top ? box with { Y = hit.Max(c => c.Bottom) + Margin } : box with { Y = hit.Min(c => c.Y) - Margin - h };
        }
        return box;
    }
}

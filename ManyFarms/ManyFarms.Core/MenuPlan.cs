namespace ManyFarms.Core;

public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>
/// Where the farm menu puts things: a panel in the middle of the screen with a heading, one row
/// per farm, and a foot for its buttons. It takes the screen as given, so it fits a small window
/// and a high UI scale, and turns pages when the farms do not all fit.
/// </summary>
public sealed class MenuPlan
{
    public const int MaxWidth = 920;
    public const int Margin = 32;
    public const int Pad = 28;
    public const int HeadHeight = 76;
    public const int FootHeight = 76;
    public const int RowHeight = 92;
    public const int RowGap = 8;
    public const int ArrowSize = 44;

    public Box Panel { get; }
    public Box Head { get; }
    public Box Foot { get; }
    /// <summary>The rows on this page, top to bottom.</summary>
    public IReadOnlyList<Box> Rows { get; }
    /// <summary>The index of the first item on this page.</summary>
    public int First { get; }
    public int Page { get; }
    public int Pages { get; }
    public int PerPage { get; }
    /// <summary>The page arrows, when there is more than one page.</summary>
    public Box? Up { get; }
    public Box? Down { get; }

    private MenuPlan(Box panel, Box head, Box foot, IReadOnlyList<Box> rows, int first, int page, int pages, int perPage, Box? up, Box? down)
    {
        Panel = panel; Head = head; Foot = foot; Rows = rows; First = first; Page = page; Pages = pages; PerPage = perPage; Up = up; Down = down;
    }

    /// <summary>A row that holds a picture and two lines of words under a name (the kinds of land).</summary>
    public const int TallRowHeight = 128;

    public static MenuPlan For(int screenWidth, int screenHeight, int items, int page) => For(screenWidth, screenHeight, items, page, RowHeight);

    public static MenuPlan For(int screenWidth, int screenHeight, int items, int page, int rowHeight)
    {
        items = Math.Max(items, 0);
        rowHeight = Math.Max(rowHeight, 1);
        int width = Math.Clamp(screenWidth - Margin * 2, 320, MaxWidth);
        int room = screenHeight - Margin * 2 - HeadHeight - FootHeight - Pad * 2;
        int perPage = Math.Max(1, (room + RowGap) / (rowHeight + RowGap));
        int pages = Math.Max(1, (items + perPage - 1) / perPage);
        page = Math.Clamp(page, 0, pages - 1);
        int first = page * perPage;
        int shown = Math.Min(perPage, items - first);
        // Every page of a paged menu is as tall as a full one, so the buttons do not jump about.
        int rowsTall = pages > 1 ? perPage : Math.Max(shown, 1);
        int rowsHeight = rowsTall * rowHeight + (rowsTall - 1) * RowGap;
        int height = HeadHeight + rowsHeight + FootHeight + Pad * 2;
        int x = Math.Max(0, (screenWidth - width) / 2);
        int y = Math.Max(0, (screenHeight - height) / 2);
        var panel = new Box(x, y, width, height);
        var head = new Box(x + Pad, y + Pad, width - Pad * 2, HeadHeight);
        var foot = new Box(x + Pad, panel.Bottom - Pad - FootHeight, width - Pad * 2, FootHeight);
        int arrowRoom = pages > 1 ? ArrowSize + 12 : 0;
        var rows = new List<Box>(shown);
        for (int i = 0; i < shown; i++)
            rows.Add(new Box(head.X, head.Bottom + i * (rowHeight + RowGap), head.Width - arrowRoom, rowHeight));
        Box? up = null, down = null;
        if (pages > 1)
        {
            int ax = head.Right - ArrowSize;
            up = new Box(ax, head.Bottom, ArrowSize, ArrowSize);
            down = new Box(ax, head.Bottom + rowsHeight - ArrowSize, ArrowSize, ArrowSize);
        }
        return new MenuPlan(panel, head, foot, rows, first, page, pages, perPage, up, down);
    }

    /// <summary>The item under a point, or -1.</summary>
    public int ItemAt(int x, int y)
    {
        for (int i = 0; i < Rows.Count; i++)
            if (Rows[i].Contains(x, y)) return First + i;
        return -1;
    }
}

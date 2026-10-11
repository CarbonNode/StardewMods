namespace ManyFarms.Core;

/// <summary>
/// Where a farm's own totem is stood when the farm is started, and where a traveller is set down
/// beside a totem. Both are "the nearest free tile", looked for in an order that keeps the totem
/// and the traveller out of each other's way: beside first, then further out.
/// </summary>
public static class TotemSpot
{
    /// <summary>
    /// Tiles to try for a new farm's totem, nearest first: two tiles out from the arrival tile (the
    /// doorstep and its neighbours stay clear for walking), never on it.
    /// </summary>
    public static IEnumerable<TilePoint> ForTotem(TilePoint arrival)
    {
        for (int ring = 2; ring <= 5; ring++)
        {
            // Left and right before above and below: a totem beside the path, not in it.
            for (int dy = 0; dy <= ring; dy++)
            {
                foreach (int sy in dy == 0 ? new[] { 0 } : new[] { -dy, dy })
                {
                    foreach (int sx in new[] { ring, -ring })
                    {
                        yield return new TilePoint(arrival.X + sx, arrival.Y + sy);
                    }
                }
            }
            for (int dx = -(ring - 1); dx <= ring - 1; dx++)
            {
                yield return new TilePoint(arrival.X + dx, arrival.Y - ring);
                yield return new TilePoint(arrival.X + dx, arrival.Y + ring);
            }
        }
    }

    /// <summary>Tiles to try for a traveller arriving at a totem: in front of it first, then its sides, then behind.</summary>
    public static IEnumerable<TilePoint> ForTraveller(TilePoint totem)
    {
        yield return new TilePoint(totem.X, totem.Y + 1);
        yield return new TilePoint(totem.X - 1, totem.Y);
        yield return new TilePoint(totem.X + 1, totem.Y);
        yield return new TilePoint(totem.X - 1, totem.Y + 1);
        yield return new TilePoint(totem.X + 1, totem.Y + 1);
        yield return new TilePoint(totem.X, totem.Y - 1);
        yield return new TilePoint(totem.X, totem.Y + 2);
    }
}

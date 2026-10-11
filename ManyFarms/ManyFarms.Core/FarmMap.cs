namespace ManyFarms.Core;

/// <summary>What a map pixel is, read off the picture by the menu: the dealing fits each kind of farm to its land.</summary>
public enum Terrain { Other, Water, Sand, Rock, Forest, Grass }

/// <summary>A farm to place: its plot (0 for the game's own farm) and what kind of land it is.</summary>
public readonly record struct FarmPlacement(int Slot, string? KindId, bool MainFarm);

/// <summary>
/// The valley map the totem opens: two panels of pixel art joined into one, 840 by 248, and where on
/// it each farm's little farmstead stands. A spot is dealt once per farm from the save's own id and
/// the farm's plot, so no two saves look alike and a farm never moves; it is only ever on grass,
/// and a kind of farm looks for its own land first (a beach farm by the shore, a riverland farm by
/// water, a forest farm in the woods, a hill-top farm under the mountains, a wilderness farm by
/// either), with several of a kind spread over the fitting places at random. No two farmsteads,
/// faces included, overlap. The numbers are map pixels.
/// </summary>
public static class FarmMap
{
    public const int Width = 840;
    public const int Height = 248;

    // The room a farmstead takes on the map, in map pixels, with the picture drawn at two screen
    // pixels per pixel on a map drawn at about 1.3x or more: up to 48 wide and 48 tall, its feet 8
    // below the spot, and two faces beside it. A spot is the picture's centre, at its feet.
    public const int IconHalf = 24;
    public const int IconUp = 48;
    public const int IconDown = 8;
    public const int FacesRoom = 72;
    public const int Gap = 6;

    /// <summary>The main farm's picture, and the plain homestead it had before 0.3.0 (drawn if the new file is missing).</summary>
    public const string MainIcon = "main";
    public const string MainIconBefore = "home";

    /// <summary>Whether a farmstead at this spot has its faces on its left, because the map's edge is near on the right.</summary>
    public static bool FacesLeft(int x) => x + IconHalf + FacesRoom > Width - 2;

    /// <summary>The ground a farmstead at a spot stands on (what must be land).</summary>
    public static (int X, int Y, int W, int H) Ground(TilePoint spot) => (spot.X - 20, spot.Y - 22, 40, 22 + IconDown);

    /// <summary>Everything a farmstead at a spot takes up, faces included.</summary>
    public static (int X, int Y, int W, int H) Room(TilePoint spot)
    {
        int left = FacesLeft(spot.X) ? spot.X - IconHalf - FacesRoom : spot.X - IconHalf;
        return (left, spot.Y - IconUp, IconHalf * 2 + FacesRoom, IconUp + IconDown);
    }

    /// <summary>The land around a spot that a kind of farm wants to see (a wider box than its ground).</summary>
    public static (int X, int Y, int W, int H) Surround(TilePoint spot) => (spot.X - 56, spot.Y - 48, 112, 80);

    /// <summary>
    /// The picture a kind of land gets (a file under assets/farms). The main farm has one of its own,
    /// whatever its land: the homestead under a tall gold-and-blue banner, so it is told from every
    /// other farm at a glance.
    /// </summary>
    public static string IconFor(string? kindId, bool mainFarm) => mainFarm ? MainIcon : kindId switch
    {
        "0" => "standard",
        "1" => "riverland",
        "2" => "forest",
        "3" => "hilltop",
        "4" => "wilderness",
        "5" => "fourcorners",
        "6" => "beach",
        "MeadowlandsFarm" => "meadowlands",
        _ => "standard",
    };

    /// <summary>What land a kind of farm looks for around it; grass means open country, any kind not listed.</summary>
    public static Terrain[] Wants(string? kindId, bool mainFarm) => mainFarm ? new[] { Terrain.Grass } : kindId switch
    {
        "1" => new[] { Terrain.Water },
        "2" => new[] { Terrain.Forest },
        "3" => new[] { Terrain.Rock },
        "4" => new[] { Terrain.Forest, Terrain.Rock },
        "6" => new[] { Terrain.Sand },
        _ => new[] { Terrain.Grass },
    };

    /// <summary>What a kind of farm may stand on: grass, and for a beach farm sand too.</summary>
    public static bool MayStandOn(string? kindId, bool mainFarm, Terrain t) => t == Terrain.Grass || (!mainFarm && kindId == "6" && t == Terrain.Sand);

    /// <summary>
    /// Deal the spots for these farms, in the order given (the game's own farm first, then the plots
    /// by number): one spot each. A farm's candidates come from the save id and its plot alone, so a
    /// farm started later never moves one started before it; the same save, farms and map deal the
    /// same spots on every machine, with plain arithmetic. <paramref name="terrainAt"/> reads the map.
    /// </summary>
    public static TilePoint[] Deal(ulong saveId, IReadOnlyList<FarmPlacement> farms, Func<int, int, Terrain> terrainAt)
    {
        var spots = new List<TilePoint>(farms.Count);
        int minX = IconHalf + 2, maxX = Width - IconHalf - 2;
        int minY = IconUp + 2, maxY = Height - IconDown - 2;
        foreach (var farm in farms)
        {
            ulong x = (saveId ^ 0x9E3779B97F4A7C15UL) + (ulong)(farm.Slot + 1) * 0xD1B54A32D192ED03UL;
            if (x == 0) x = 1;
            ulong Next()
            {
                x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
                return x * 0x2545F4914F6CDD1DUL;
            }
            var wants = Wants(farm.KindId, farm.MainFarm);
            TilePoint? best = null;
            double bestFit = -1;
            // Up to 1500 draws: the first spot on fitting land with its own kind of country around it is taken;
            // failing that, the best seen; failing any, a second round lets the faces overlap a little, and a last one stands anywhere.
            for (int round = 0; round < 3 && best is null; round++)
            {
                double needLand = round == 0 ? 0.85 : round == 1 ? 0.6 : 0.0;
                int gap = round == 0 ? Gap : round == 1 ? 0 : -IconHalf;
                for (int attempt = 0; attempt < 1500; attempt++)
                {
                    var spot = new TilePoint(minX + (int)((Next() >> 33) % (ulong)(maxX - minX)), minY + (int)((Next() >> 33) % (ulong)(maxY - minY)));
                    if (Share(Ground(spot), t => MayStandOn(farm.KindId, farm.MainFarm, t), terrainAt) < needLand) continue;
                    if (spots.Any(other => Overlaps(Room(spot), Room(other), gap))) continue;
                    double fit = Share(Surround(spot), t => Array.IndexOf(wants, t) >= 0, terrainAt);
                    if (fit > bestFit) { bestFit = fit; best = spot; }
                    // Open country is everywhere, so a farm that wants it takes the first clear spot; the others want a fair share of their land (sand is a thin strip, so a little of it is enough).
                    if (fit >= (wants[0] == Terrain.Grass ? 0.5 : wants[0] == Terrain.Sand ? 0.10 : 0.22)) break;
                }
            }
            spots.Add(best ?? new TilePoint(minX + spots.Count * ((maxX - minX) / Math.Max(1, farms.Count)), maxY - 1));
        }
        return spots.ToArray();
    }

    private static double Share((int X, int Y, int W, int H) box, Func<Terrain, bool> counts, Func<int, int, Terrain> terrainAt)
    {
        int hit = 0, all = 0;
        // Every third pixel is plenty for a share, and keeps nine farms' dealing under a few milliseconds.
        for (int yy = box.Y; yy < box.Y + box.H; yy += 3)
            for (int xx = box.X; xx < box.X + box.W; xx += 3)
            {
                if (xx < 0 || yy < 0 || xx >= Width || yy >= Height) continue;
                all++;
                if (counts(terrainAt(xx, yy))) hit++;
            }
        return all == 0 ? 0 : (double)hit / all;
    }

    private static bool Overlaps((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b, int gap)
        => a.X < b.X + b.W + gap && b.X < a.X + a.W + gap && a.Y < b.Y + b.H + gap && b.Y < a.Y + a.H + gap;
}

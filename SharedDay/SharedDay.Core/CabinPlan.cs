namespace SharedDay.Core;

/// <summary>A tile on the farm, as the plan names one. The adapter turns it into the game's own vector.</summary>
public readonly record struct Spot(int X, int Y);

/// <summary>
/// Cabins for the group (0.1.40). The owner, 2026-10-15: "cabins shouldnt matter". A group member
/// with no farmer holds the day open until they make one (<see cref="DayCoordinator.SetGroup"/>),
/// so a farm with no free cabin would hold it for ever: nobody could make the farmer the day is
/// waiting for, and only a skip would move it on. The host therefore keeps one free cabin for
/// every member who still needs one, building them itself.
///
/// This class decides only HOW MANY and WHERE TO TRY. The game decides whether a spot can be
/// built on: the adapter offers each spot to <c>GameLocation.buildStructure</c> with its own
/// safety checks on, and nothing of anyone's is cleared to make room.
/// </summary>
public static class CabinPlan
{
    /// <summary>A cabin's footprint in tiles, as Data/Buildings has it.</summary>
    public const int Width = 5;
    public const int Height = 3;

    /// <summary>Cabins to add: one per member with no farmer, less the empty cabins already standing. Never negative.</summary>
    public static int HowMany(int membersWithNoFarmer, int freeCabins) => Math.Max(0, membersWithNoFarmer - Math.Max(0, freeCabins));

    /// <summary>
    /// Where to try, best first: rings of candidate corners around <paramref name="origin"/>, a
    /// cabin's width and height apart, so a row of them lands tidily rather than touching. Within
    /// a ring, left to right and top to bottom, so two hosts of the same farm would choose alike.
    /// Spots off the map (a negative corner) are left out; the caller stops at the first the game
    /// accepts.
    /// </summary>
    /// <param name="origin">The corner to grow out from (the farmhouse's left side suits).</param>
    /// <param name="rings">How far out to go. Each ring is one cabin further.</param>
    public static IEnumerable<Spot> Spots(Spot origin, int rings = 6)
    {
        if (rings < 0) rings = 0;
        for (int ring = 0; ring <= rings; ring++)
        {
            foreach (var spot in Ring(origin, ring)) yield return spot;
        }
    }

    private static IEnumerable<Spot> Ring(Spot origin, int ring)
    {
        var seen = new HashSet<Spot>();
        for (int dy = -ring; dy <= ring; dy++)
        {
            for (int dx = -ring; dx <= ring; dx++)
            {
                // A ring's own edge only: the inner ones were offered already.
                if (ring > 0 && Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
                var spot = new Spot(origin.X + dx * (Width + 1), origin.Y + dy * (Height + 1));
                if (spot.X < 0 || spot.Y < 0 || !seen.Add(spot)) continue;
                yield return spot;
            }
        }
    }
}

namespace ManyFarms.Core;

/// <summary>A farmer as a farm's list of people needs them: who they are, and whether they are the host PC's own farmer.</summary>
public readonly record struct Person(long Id, string Name, bool IsHost);

/// <summary>A farmer on a farm's hover panel, and what is said beside their name.</summary>
public readonly record struct PanelRow(long Id, string Name, string Note);

/// <summary>
/// Who farms the main farm, the game's own, where the cabins stand. It is everybody's who has no
/// land of their own: the farmers who picked it, then the ones who have not picked anything yet.
/// The host PC's own farmer is one of them only while somebody plays it.
/// </summary>
public static class MainFarmRoster
{
    /// <param name="mainMembers">The farmers who picked the main farm, in the order they did.</param>
    /// <param name="otherFarms">The farmer lists of every other started farm.</param>
    /// <param name="everyone">Every farmer somebody has made, online or not.</param>
    /// <param name="hostPlays">Somebody plays the host PC's own farmer.</param>
    /// <returns>Each farmer of the main farm, and whether they picked it (false: they have picked nothing yet).</returns>
    public static IReadOnlyList<(Person Who, bool Picked)> For(IReadOnlyList<long> mainMembers, IEnumerable<IReadOnlyList<long>> otherFarms, IReadOnlyList<Person> everyone, bool hostPlays)
    {
        var people = everyone.Where(p => hostPlays || !p.IsHost).ToList();
        var elsewhere = new HashSet<long>(otherFarms.SelectMany(ids => ids));
        var list = new List<(Person, bool)>();
        foreach (long id in mainMembers)
        {
            int at = people.FindIndex(p => p.Id == id);
            if (at >= 0 && !list.Any(e => e.Item1.Id == id)) list.Add((people[at], true));
        }
        foreach (var person in people)
            if (!elsewhere.Contains(person.Id) && !list.Any(e => e.Item1.Id == person.Id)) list.Add((person, false));
        return list;
    }
}

/// <summary>What the valley map says about a farm: the plate under the main farm, and the panel a farmstead shows when it is pointed at.</summary>
public static class MapWords
{
    /// <summary>The plate that always stands under the main farm's farmstead, so nobody has to point at it to know.</summary>
    public const string MainPlate = "Main farm";

    /// <summary>The most farmers a panel lists by name; the rest are counted in one last line.</summary>
    public const int MostRows = 6;

    /// <summary>The line under a farm's name on its panel.</summary>
    /// <param name="mainFarm">It is the game's own farm, the one with the cabins.</param>
    public static string Sub(bool mainFarm, string kind, bool mine, bool here, bool houseHere = false)
    {
        var parts = new List<string>();
        if (mainFarm) parts.Add("The main farm, shared by everyone");
        if (!string.IsNullOrWhiteSpace(kind)) parts.Add(kind);
        if (mine && !mainFarm) parts.Add(houseHere ? "Your farm: your house is here" : "Your farm");
        if (here) parts.Add("You are here");
        return string.Join("  |  ", parts);
    }

    /// <summary>The heading over a panel's faces, or the one line that says there are none.</summary>
    public static string Heading(bool mainFarm, int farmers) => (mainFarm, farmers) switch
    {
        (true, <= 0) => "Nobody farms here: everyone has a farm of their own.",
        (true, _) => "No farm of their own, so they farm here:",
        (false, <= 0) => "Nobody farms here yet.",
        (false, 1) => "Its farmer:",
        (false, _) => "Its farmers:",
    };

    /// <summary>What stands beside a farmer's name on a panel.</summary>
    /// <param name="picked">On the main farm: they picked it. False: they have not picked a farm at all yet.</param>
    public static string Note(bool mainFarm, bool picked, bool online)
    {
        string what = !mainFarm ? "" : picked ? "chose the main farm" : "has not picked a farm yet";
        if (online) return what;
        return what.Length == 0 ? "away" : what + ", away";
    }

    /// <summary>The line under the listed farmers when there are more than the panel names.</summary>
    public static string More(int hidden) => hidden <= 0 ? "" : hidden == 1 ? "and 1 more" : $"and {hidden} more";

    /// <summary>What the panel ends with: what a press does.</summary>
    public static string Foot(bool here) => here ? "You are on this farm." : "Press to go there.";

    /// <summary>The foot after a press on the farm the player is standing on (0.5.1): no trip, the menu stays open.</summary>
    public const string AlreadyHere = "You are already on this farm. Pick another to travel there.";
}

/// <summary>
/// Where a farmstead's panel goes: beside the farmstead it describes, never over it, and wholly on
/// the screen. Right of it first, then left, below and above; on a screen too small for any of
/// those it takes the side with the most room and is pushed in from the edge.
/// </summary>
public static class HoverPanel
{
    public const int Gap = 12;
    public const int Edge = 8;

    /// <param name="about">The farmstead and its faces on screen.</param>
    public static Box Place(int screenWidth, int screenHeight, int width, int height, Box about)
    {
        int maxX = Math.Max(Edge, screenWidth - Edge - width), maxY = Math.Max(Edge, screenHeight - Edge - height);
        int midY = Math.Clamp(about.Y + about.Height / 2 - height / 2, Edge, maxY);
        int midX = Math.Clamp(about.X + about.Width / 2 - width / 2, Edge, maxX);
        var tries = new[]
        {
            new Box(about.Right + Gap, midY, width, height),
            new Box(about.X - Gap - width, midY, width, height),
            new Box(midX, about.Bottom + Gap, width, height),
            new Box(midX, about.Y - Gap - height, width, height),
        };
        foreach (var box in tries)
            if (OnScreen(box, screenWidth, screenHeight)) return box;
        // No side has the whole room: the side with the most, pushed onto the screen.
        int[] room = { screenWidth - about.Right, about.X, screenHeight - about.Bottom, about.Y };
        int best = Array.IndexOf(room, room.Max());
        var pick = tries[best];
        return new Box(Math.Clamp(pick.X, Math.Min(Edge, maxX), maxX), Math.Clamp(pick.Y, Math.Min(Edge, maxY), maxY), width, height);
    }

    /// <summary>
    /// The widest a panel may be and still stand wholly beside this farmstead: the room on its
    /// roomier side. A panel no wider than this, and no taller than the screen less its edges,
    /// always finds a place that is on the screen and not over the farmstead.
    /// </summary>
    public static int WidthRoom(int screenWidth, Box about) => Math.Max(screenWidth - about.Right, about.X) - Gap - Edge;

    public static bool OnScreen(Box box, int screenWidth, int screenHeight) =>
        box.X >= Edge && box.Y >= Edge && box.Right <= screenWidth - Edge && box.Bottom <= screenHeight - Edge;

    public static bool Overlaps(Box a, Box b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}

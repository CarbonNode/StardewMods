namespace ManyFarms.Core;

/// <summary>
/// What the house on a farm shows of its farmers' house upgrades. The rooms behind its door are
/// each farmer's own (their cabin's, which the game upgrades), so only the outside is decided
/// here: one house, one look, the same on every player's screen.
/// </summary>
public static class HouseLook
{
    /// <summary>The game's highest house upgrade (the cellar).</summary>
    public const int TopLevel = 3;

    /// <summary>The house's picture (the farmhouse's since 0.5.0, the cabin's before) has three looks side by side: as built, the first upgrade, and the second (the cellar changes nothing outside).</summary>
    public const int Looks = 3;

    /// <summary>
    /// The upgrade level a farm's house shows: the highest among its farmers. A farm two farmers
    /// share has one house, and the one who has built furthest is the one it shows; a farm nobody
    /// farms shows the house as built.
    /// </summary>
    public static int Level(IEnumerable<int> farmersLevels)
    {
        int best = 0;
        foreach (int level in farmersLevels) best = Math.Max(best, level);
        return Math.Min(best, TopLevel);
    }

    /// <summary>Which of the cabin picture's looks a level is drawn with (0, 1 or 2), as the game picks a cabin's.</summary>
    public static int Look(int level) => Math.Clamp(level, 0, Looks - 1);

    /// <summary>
    /// How far right of the as-built look this level's look starts in the picture, or 0 when the
    /// picture is not wide enough to hold it (another mod's cabin picture with fewer looks): the
    /// house is then drawn as built instead of from beyond the picture's edge.
    /// </summary>
    /// <param name="lookWidth">The width of one look (the building's source rectangle).</param>
    /// <param name="startX">Where the as-built look starts in the picture.</param>
    /// <param name="pictureWidth">The whole picture's width.</param>
    public static int ShiftX(int level, int lookWidth, int startX, int pictureWidth)
    {
        if (lookWidth <= 0) return 0;
        int shift = lookWidth * Look(level);
        return startX + shift + lookWidth <= pictureWidth ? shift : 0;
    }

    /// <summary>The same downwards, for a picture that holds its looks one above the other.</summary>
    public static int ShiftY(int level, int lookHeight, int startY, int pictureHeight)
    {
        if (lookHeight <= 0) return 0;
        int shift = lookHeight * Look(level);
        return startY + shift + lookHeight <= pictureHeight ? shift : 0;
    }

    /// <summary>
    /// How far from the as-built look this level's look is in the picture: to the right when the
    /// picture holds its looks side by side (a cabin's: 80 wide, three times over in 240), else
    /// down when it holds them one above the other (the farmhouse's Buildings\houses: 160 by 144,
    /// three times over in 432 tall; the 0.5.0 lab found the first try, a shift to the right, drew
    /// the as-built look at every level), else not at all. The game draws the main farmhouse at
    /// level 1 and 2 from 0, 144 and 0, 288 of that picture: the -Upgrade lab compares the house
    /// with the main farmhouse's own rectangle at each level, and passed on 1.6.15.
    /// </summary>
    public static (int X, int Y) Shift(int level, int lookWidth, int lookHeight, int startX, int startY, int pictureWidth, int pictureHeight)
    {
        if (Look(level) == 0) return (0, 0);
        int x = ShiftX(level, lookWidth, startX, pictureWidth);
        if (x != 0) return (x, 0);
        return (0, ShiftY(level, lookHeight, startY, pictureHeight));
    }
}

/// <summary>A farmer whose house Robin is upgrading.</summary>
/// <param name="DaysLeft">The game's count of nights left (Farmer.daysUntilHouseUpgrade); zero or less when nothing is being built.</param>
/// <param name="Online">They are in the game now. The game counts an upgrade's nights only for a farmer who is there to sleep through them.</param>
/// <param name="HouseOn">The started plot that is their own farm and has a house standing (its location name), or null: their cabin on the main farm is then where the upgrade is, as in an ordinary game.</param>
/// <param name="PaidToday">They paid Robin today: she is still at her counter, and starts the next morning (as the game sends her to the host's farmhouse).</param>
public readonly record struct Upgrader(long Id, int DaysLeft, bool Online, string? HouseOn, bool PaidToday = false);

/// <summary>
/// Notices the moment a farmer pays for an upgrade: the game's count of nights goes from none to
/// some while the host is looking. An upgrade already under way when the host first looks (a save
/// just loaded) was paid for on some earlier day as far as anyone can tell.
/// </summary>
public sealed class OrderWatch
{
    private readonly Dictionary<long, int> last = new();
    private readonly Dictionary<long, int> paidOn = new();

    /// <summary>Takes a look at one farmer. True when they paid for the upgrade under way on this very day.</summary>
    /// <param name="today">The day's number, counted from the start of the save.</param>
    public bool PaidToday(long farmer, int daysLeft, int today)
    {
        if (last.TryGetValue(farmer, out int was) && was <= 0 && daysLeft > 0) paidOn[farmer] = today;
        last[farmer] = daysLeft;
        if (daysLeft <= 0) paidOn.Remove(farmer);
        return paidOn.TryGetValue(farmer, out int day) && day == today;
    }

    public void Forget()
    {
        last.Clear();
        paidOn.Clear();
    }
}

/// <summary>What the host does about Robin at one look (see <see cref="BuilderRule.Call"/>).</summary>
public enum RobinCall
{
    /// <summary>Nothing: she is where she should be, or she is the game's today.</summary>
    Leave,
    /// <summary>Stand her at the picked farmer's house, hammering.</summary>
    Send,
    /// <summary>Take her back to where her own day would have her by now, the hammering off and her schedule running.</summary>
    Home,
}

/// <summary>At whose house Robin works today.</summary>
public static class BuilderRule
{
    /// <summary>
    /// The farmer whose house on their own farm Robin works at, or null when she is not needed on
    /// any plot. Only a farmer who is in the game: an upgrade's nights are counted for nobody
    /// else, so Robin would stand at an absent farmer's house for good. Not one who paid her
    /// today: she starts the morning after. The host's farmer first
    /// (the game sends Robin to the host's upgrade before anything else), then whoever has the
    /// fewest nights left, then the lowest farmer number, so every PC and every morning agree.
    /// </summary>
    public static Upgrader? Pick(IEnumerable<Upgrader> farmers, long hostId)
    {
        Upgrader? best = null;
        foreach (var farmer in farmers)
        {
            if (!farmer.Online || farmer.DaysLeft <= 0 || farmer.HouseOn is null || farmer.PaidToday) continue;
            if (best is not { } has || Before(farmer, has, hostId)) best = farmer;
        }
        return best;
    }

    private static bool Before(Upgrader a, Upgrader b, long hostId)
    {
        if ((a.Id == hostId) != (b.Id == hostId)) return a.Id == hostId;
        if (a.DaysLeft != b.DaysLeft) return a.DaysLeft < b.DaysLeft;
        return a.Id < b.Id;
    }

    /// <summary>
    /// Robin, standing at the house on this plot, still has work there: a farmer of that farm who is
    /// in the game has an upgrade under way that was not paid for today. Asked every second, so she
    /// goes home when the farmer leaves the game or the upgrade is done, and is never left at a
    /// house for a reason that has gone.
    /// </summary>
    public static bool WantedAt(IEnumerable<Upgrader> farmers, string? plot)
    {
        if (plot is null) return false;
        foreach (var farmer in farmers)
            if (farmer.Online && farmer.DaysLeft > 0 && !farmer.PaidToday && farmer.HouseOn == plot) return true;
        return false;
    }

    /// <summary>What the host does about Robin at one look.</summary>
    /// <param name="atHouse">She stands hammering at a plot's house.</param>
    /// <param name="wantedThere">That house's farm has a farmer whose upgrade keeps her there (<see cref="WantedAt"/>).</param>
    /// <param name="canSend">There is a house to send her to now, and the game has no work of its own for her.</param>
    /// <param name="onPlot">She stands on a started plot, at a house or not.</param>
    /// <param name="buildingThere">The game has a building under construction where she stands: she is the game's there. One elsewhere does not keep her.</param>
    /// <param name="hammering">The game's hammering switch is on.</param>
    /// <param name="gameWork">The game has work of its own for her today: the host's upgrade, a building, the town's upgrade.</param>
    public static RobinCall Call(bool atHouse, bool wantedThere, bool canSend, bool onPlot, bool buildingThere, bool hammering, bool gameWork)
    {
        if (atHouse && wantedThere) return RobinCall.Leave;
        if (canSend) return RobinCall.Send;
        // At a house nobody's upgrade keeps her at, or left standing on a plot by a save or by a day played again.
        // Never while the game has a building under construction: it may be on that farm, beside the house, and then she is the game's.
        if ((atHouse || onPlot) && !buildingThere) return RobinCall.Home;
        // Put back in her own house by something else (a turn mod's morning) with the hammering this mod switched on
        // still on: every game that sees it has taken her off her schedule again, so she is set on her day afresh.
        if (hammering && !gameWork) return RobinCall.Home;
        return RobinCall.Leave;
    }

    /// <summary>
    /// The time of the last thing on a villager's schedule that was due by now, or null when
    /// nothing was: where her day would have her, for sending her home in the middle of it.
    /// </summary>
    public static int? LastDue(IEnumerable<int> times, int now)
    {
        int? best = null;
        foreach (int time in times)
            if (time <= now && (best is not { } has || time > has)) best = time;
        return best;
    }

    /// <summary>
    /// Where Robin stands at a house, nearest the wall first: the left end of its porch row (never
    /// on the doorstep or under the mailbox), then the porch tile beside it (a farm's totem usually
    /// stands right in front of the left end, and would hide her), then the ground beside the house
    /// on its left and right, then in front of its left and right corners. The porch row is the
    /// doorstep's row: a cabin's bottom row, which is its porch and passable; for a house whose
    /// door is on its bottom row (the farmhouse's shape, 0.5.0) the row of open ground in front of
    /// it. Either way every spot is ground a farmer can stand on, not a wall, provided the house's
    /// own collision map lets a farmer on its doorstep row (the caller checks each tile). Never the
    /// mailbox or the tile in front of it: the farmhouse's mailbox stands beside the house, one row
    /// below the door, right where the spots to the right of the house are.
    /// </summary>
    public static IEnumerable<TilePoint> Spots(TilePoint topLeft, TilePoint size, TilePoint doorStep, TilePoint mailbox)
    {
        int porch = doorStep.Y;
        var spots = new[]
        {
            new TilePoint(topLeft.X, porch),
            new TilePoint(topLeft.X + 1, porch),
            new TilePoint(topLeft.X - 1, porch),
            new TilePoint(topLeft.X + size.X, porch),
            new TilePoint(topLeft.X - 1, porch + 1),
            new TilePoint(topLeft.X + size.X, porch + 1),
            new TilePoint(topLeft.X, porch + 1),
        };
        var mailFront = new TilePoint(mailbox.X, mailbox.Y + 1);
        foreach (var spot in spots)
            if (spot != doorStep && spot != mailbox && spot != mailFront) yield return spot;
    }
}

/// <summary>What a farmer with a farm of their own is told about a house upgrade.</summary>
public static class HouseWords
{
    /// <summary>Said once, when they have just paid Robin for an upgrade.</summary>
    /// <param name="nights">The game's count of nights until it is done.</param>
    /// <param name="mainFarm">The main farm's name ("Squeet Farm").</param>
    /// <param name="farmhouse">Their home on the main farm is the farmhouse (the host's farmer), not a cabin.</param>
    /// <param name="nightsCountedWhileAway">A turn mod (Shared Day 0.1.29) counts their nights for them while they are out of the game, so they need not stay for the night.</param>
    public static string Ordered(int nights, string mainFarm, bool farmhouse = false, bool nightsCountedWhileAway = false) =>
        $"Robin builds your house upgrade at the house on your own farm. When she is not busy elsewhere you will find her there. {Ready(nights)} "
        + (nightsCountedWhileAway ? "Your nights are counted for you while you are away, so it goes on being built when you leave. " : "The game counts a night only while you are in it, so on a farm played in turns stay until the night has passed. ")
        + $"Your {(farmhouse ? "farmhouse" : "cabin")} on {mainFarm} is the same home, so it grows too.";

    /// <summary>
    /// Said at Robin's counter on a day she is out at a farm's house: the game's counter answers
    /// nothing when she is not behind it, and a first-time player would take that for a bug.
    /// </summary>
    /// <param name="farm">The farm's name ("Alice's farm").</param>
    public static string RobinOut(string farm) =>
        $"Robin is out building a house upgrade at {farm}. Her order book is on the counter, so you can still buy and order.";

    /// <summary>The same on screen: the game's message box is one line.</summary>
    public const string OrderedShort = "Robin builds it at the house on your own farm.";

    public static string Ready(int nights) => nights <= 1
        ? "It is ready tomorrow morning."
        : $"It is ready after {nights} nights that you sleep through.";

    /// <summary>Said once, the morning an upgrade is done.</summary>
    /// <param name="level">Their new house upgrade level (1 to 3).</param>
    public static string Finished(int level) => level >= HouseLook.TopLevel
        ? "Your cellar is finished. Go in by the house on your farm: the stairs down are in your kitchen."
        : "Your house upgrade is finished. The house on your farm shows it, and the bigger rooms are behind its door.";

    /// <summary>
    /// What to say about a change in a farmer's upgrade since it was last looked at, or null for
    /// nothing. Nothing is said to a farmer with no house on a farm of their own (an ordinary
    /// game says nothing either), nor about what was already so the last time they looked. The
    /// last look is kept on the farmer (<see cref="UpgradeMemory"/>, 0.5.0), so an upgrade that
    /// was finished for them while they were away is told on their first look back.
    /// </summary>
    /// <param name="before">Nights left and level when last looked at; null when this farmer has never looked.</param>
    public static string? About((int DaysLeft, int Level)? before, int daysLeft, int level, bool houseOnOwnFarm, string mainFarm, bool farmhouse = false, bool nightsCountedWhileAway = false)
    {
        if (before is not { } was || !houseOnOwnFarm) return null;
        if (level > was.Level) return Finished(level);
        if (was.DaysLeft <= 0 && daysLeft > 0) return Ordered(daysLeft, mainFarm, farmhouse, nightsCountedWhileAway);
        return null;
    }
}

/// <summary>
/// A farmer's house upgrade as they last saw it (nights left, level), kept on the farmer so it is
/// saved and travels with them (0.5.0). Before, it was kept for the session only, and an upgrade
/// finished in their absence (a turn mod that counts their nights for them) was already so when
/// they came back, and never told.
/// </summary>
public static class UpgradeMemory
{
    public static string Keep(int daysLeft, int level) => $"{daysLeft},{level}";

    /// <summary>What <see cref="Keep"/> wrote, or null for nothing or anything else.</summary>
    public static (int DaysLeft, int Level)? Read(string? kept)
    {
        if (string.IsNullOrWhiteSpace(kept)) return null;
        string[] parts = kept.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int daysLeft) && int.TryParse(parts[1], out int level) ? (daysLeft, level) : null;
    }
}

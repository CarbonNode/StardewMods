using ManyFarms.Core;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;

namespace ManyFarms.Mod;

/// <summary>
/// Robin at work on a house upgrade. The game sends her to the farmhouse for the host's own
/// upgrade and nowhere at all for a farmhand's: a cabin grows with nobody seen building it. A
/// farmer with a farm of their own has their house there, so that is where she works while
/// their upgrade is being built: on its porch, hammering, while they are in the game, until it
/// is done. Her counter answers for her meanwhile (<see cref="AnswerCounter"/>). A farmer with no farm of their own is left exactly as the game has it.
/// Host only; her place and her hammering reach every player through the game's own syncing.
/// </summary>
internal static class Builder
{
    /// <summary>The plot she was last sent to, and on which day, for the log (said once per plot and day).</summary>
    private static (string Plot, int Day)? sent;

    /// <summary>Who paid Robin today (see <see cref="OrderWatch"/>).</summary>
    private static readonly OrderWatch orders = new();

    public static void Forget()
    {
        sent = null;
        night = false;
        orders.Forget();
    }

    /// <summary>
    /// Everybody whose house Robin might be upgrading, as the rule wants them. Each call is one look
    /// of the order watch. When Shared Day says it counts an absent farmer's nights for them (0.5.0,
    /// Shared Day 0.1.29), every farmer counts as in the game for the rule: their upgrade goes on
    /// being built without them, so Robin stays at their house instead of going home when they leave.
    /// </summary>
    internal static List<Upgrader> Upgraders()
    {
        var online = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();
        bool credited = ModEntry.Instance.AbsentNightsCredited;
        var list = new List<Upgrader>();
        foreach (var farmer in Game1.getAllFarmers())
        {
            if (farmer is null || farmer.isUnclaimedFarmhand) continue;
            string? plot = Patches.OwnPlot(farmer.UniqueMultiplayerID) is { } own && Houses.For(own) is not null ? own.Name : null;
            int daysLeft = farmer.daysUntilHouseUpgrade.Value;
            list.Add(new Upgrader(farmer.UniqueMultiplayerID, daysLeft, credited || online.Contains(farmer.UniqueMultiplayerID), plot, orders.PaidToday(farmer.UniqueMultiplayerID, daysLeft, Game1.Date.TotalDays)));
        }
        return list;
    }

    /// <summary>
    /// The plot's house she stands hammering at (on its porch or within a tile of it), or null.
    /// Wherever the house stands: another mod may have moved it off its plot, and she is then at
    /// it there, so which farm she works for is read off the house (<see cref="Houses.PlotOf"/>),
    /// never off the location she stands in.
    /// </summary>
    internal static Building? HouseAt(NPC robin) => robin.shouldPlayRobinHammerAnimation.Value && robin.currentLocation is { } where
        ? where.buildings.FirstOrDefault(b => Houses.IsHouse(b)
            && robin.TilePoint.X >= b.tileX.Value - 1 && robin.TilePoint.X <= b.tileX.Value + b.tilesWide.Value
            && robin.TilePoint.Y >= b.tileY.Value - 1 && robin.TilePoint.Y <= b.tileY.Value + b.tilesHigh.Value)
        : null;

    /// <summary>True when she stands hammering at some plot's house.</summary>
    internal static bool AtAHouse(NPC robin) => HouseAt(robin) is not null;

    /// <summary>The farm whose house she stands hammering at, or null. True on every player's game: the plots are always in step.</summary>
    internal static Farm? OutAt() =>
        Game1.getCharacterFromName("Robin") is { } robin && HouseAt(robin) is { } house && Houses.PlotOf(house) is { } plot
            && Game1.getLocationFromName(plot) is Farm farm && Farms.IsPool(farm) ? farm : null;

    /// <summary>The night is running: from the day's end until the next morning has started (see <see cref="Tend"/>).</summary>
    private static bool night;

    /// <summary>
    /// Told when the day ends and when the next has started. The game drops its own "a new day is
    /// being made" flag in the middle of the night, before it waits for the players and saves; a
    /// look in that window would stand Robin on a plot inside the save.
    /// </summary>
    public static void Night(bool running) => night = running;

    /// <summary>
    /// Just before a save is written on the host: a Robin standing at a plot's house is taken home
    /// first, so no save ever holds her on a plot. The game's own night walks her home before its
    /// save; a save written in the middle of a day (a turn mod's checkpoint) does not, and a host
    /// that loads such a save on an older version of this mod has nothing to fetch her back.
    /// She is sent out again at the next look after the save.
    /// </summary>
    public static void BeforeSave(IMonitor monitor)
    {
        if (!Game1.IsMasterGame || Game1.getCharacterFromName("Robin") is not { } robin) return;
        if (AtAHouse(robin) || (robin.currentLocation is Farm on && Farms.IsPool(on))) Home(robin, monitor);
    }

    /// <summary>
    /// Looked at about once a second on the host, and asked afresh every time (see
    /// <see cref="BuilderRule.Call"/>). She is sent to the house of the farmer the rule picks,
    /// unless the game has her elsewhere for work of its own (a building she is constructing, the
    /// town's upgrade, the host's farmhouse when the host has no farm of their own) or she is away
    /// for the day (a festival, the island, the green rain of year 1). At a house she stays only
    /// while a farmer of that farm who is in the game still has the upgrade under way; otherwise
    /// she is taken home, and so is a Robin found standing on a plot with no work there (a save
    /// written with her on it), or hammering in her own house with nothing to build (a turn mod
    /// played the morning again and put her back, the switch this mod set still on).
    /// A farmer who paid her today gets her from the next morning, as the game has it for the host.
    /// </summary>
    public static void Tend(IMonitor monitor)
    {
        if (!Game1.IsMasterGame || night || Game1.newDay || Game1.newDaySync.hasInstance()) return;
        // Looked at every time, whatever stops her below: this is how a payment made today is noticed.
        var upgraders = Upgraders();
        if (Game1.eventUp || Game1.farmEvent is not null) return;
        if (Game1.getCharacterFromName("Robin") is not { } robin) return;

        long hostId = Game1.MasterPlayer.UniqueMultiplayerID;
        var pick = BuilderRule.Pick(upgraders, hostId);
        bool hostsOwn = Game1.player.daysUntilHouseUpgrade.Value > 0;
        bool building = Game1.IsThereABuildingUnderConstruction();
        bool gameWork = hostsOwn || building || Game1.RequireLocation<Town>("Town").daysUntilCommunityUpgrade.Value > 0;
        bool away = Utility.isFestivalDay() || (Game1.isGreenRain && Game1.year <= 1) || Game1.IsVisitingIslandToday(robin.Name);

        // Where she would be sent now, if anywhere: the house, wherever it stands (another mod may have moved it off
        // its plot). The game's order: the host's own house first, then a building, then the town.
        (Farm Plot, GameLocation In, Building House, Point Spot)? to = null;
        if (!away && pick is { HouseOn: not null } chosen && (hostsOwn ? chosen.Id == hostId : !gameWork)
            && Game1.getLocationFromName(chosen.HouseOn) is Farm plot && Houses.For(plot) is { } stands && Spot(stands.House, stands.In) is { } spot)
            to = (plot, stands.In, stands.House, spot);

        var houseAt = HouseAt(robin);
        bool atHouse = houseAt is not null;
        bool onPlot = robin.currentLocation is Farm on && Farms.IsPool(on);
        // A building under construction where she stands is the game's own work for her there; one elsewhere is not.
        bool buildingThere = robin.currentLocation?.buildings.Any(b => b.isUnderConstruction()) ?? false;
        switch (BuilderRule.Call(atHouse, atHouse && BuilderRule.WantedAt(upgraders, Houses.PlotOf(houseAt!)), to is not null, onPlot, buildingThere, robin.shouldPlayRobinHammerAnimation.Value, gameWork))
        {
            case RobinCall.Send:
                Send(robin, to!.Value.Plot, to.Value.In, to.Value.Spot, pick!.Value, monitor);
                break;
            case RobinCall.Home:
                Home(robin, monitor);
                break;
        }
    }

    private static void Send(NPC robin, Farm plot, GameLocation where, Point spot, Upgrader pick, IMonitor monitor)
    {
        // Whatever she was doing (her morning at the counter, a walk to the door) is over for today, as when the game sends her.
        Still(robin);
        robin.ignoreScheduleToday = true;
        Game1.warpCharacter(robin, where.NameOrUniqueName, new Vector2(spot.X, spot.Y));
        robin.faceDirection(0);
        // The game's own switch for her hammering; every player's game starts the animation from it.
        ModEntry.Instance.Helper.Reflection.GetField<bool>(robin, "isPlayingRobinHammerAnimation", required: false)?.SetValue(false);
        robin.shouldPlayRobinHammerAnimation.Value = true;

        if (sent is not { } was || was.Plot != plot.Name || was.Day != Game1.Date.TotalDays)
        {
            sent = (plot.Name, Game1.Date.TotalDays);
            string who = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == pick.Id)?.Name ?? "a farmer";
            monitor.Log($"Many Farms: Robin works on {who}'s house upgrade at the house of {Farms.Label(plot)} (in {where.NameOrUniqueName}) at {spot.X}, {spot.Y} today; {pick.DaysLeft} night(s) left.", LogLevel.Info);
        }
    }

    private static void Still(NPC robin)
    {
        robin.Halt();
        robin.controller = null;
        robin.temporaryController = null;
        robin.DirectionsToNewLocation = null;
        robin.queuedSchedulePaths.Clear();
    }

    /// <summary>
    /// Takes her back to where her own day would have her by now: the place of the last thing on
    /// today's schedule that was due (her counter, in shop hours), else her own room. Her schedule
    /// runs on from there, and the hammering is off.
    /// </summary>
    private static void Home(NPC robin, IMonitor monitor)
    {
        string from = robin.currentLocation?.NameOrUniqueName ?? "nowhere";
        // The switch alone: each player's game stops the animation when it sees it go off.
        robin.shouldPlayRobinHammerAnimation.Value = false;
        Still(robin);
        // Her day's own lines again, not silence until tomorrow: the game's hammering line is dropped with the rest.
        robin.resetCurrentDialogue();
        robin.ignoreScheduleToday = false;
        string map = robin.DefaultMap;
        var tile = new Point((int)robin.DefaultPosition.X / 64, (int)robin.DefaultPosition.Y / 64);
        int facing = robin.DefaultFacingDirection;
        if (robin.TryLoadSchedule() && robin.Schedule is { } day && BuilderRule.LastDue(day.Keys, Game1.timeOfDay) is { } due
            && day[due] is { targetLocationName: { } there } last && Game1.getLocationFromName(there) is not null)
        {
            map = there;
            tile = last.targetTile;
            facing = last.facingDirection;
        }
        Game1.warpCharacter(robin, map, new Vector2(tile.X, tile.Y));
        robin.faceDirection(facing);
        sent = null;
        monitor.Log($"Many Farms: Robin has no house upgrade to build today (she was in {from}; the farmer left the game, it is done, or the day was played again), so she is in {map} at {tile.X}, {tile.Y}, where her own day has her by now, and not hammering.", LogLevel.Info);
    }

    /// <summary>
    /// Robin's counter on a day she is out at a farm's house. The game's counter answers only when
    /// she stands behind it, and says nothing at all otherwise; she is out because this mod sent
    /// her, so the counter says where she is and then offers what she would: her shop, a house
    /// upgrade, a renovation, a building (none of those needs her in the room). Runs on the PC of
    /// whoever pressed the counter. True when it answered.
    /// </summary>
    internal static bool AnswerCounter(GameLocation shop)
    {
        if (OutAt() is not { } plot || shop.characters.Any(c => c.Name == "Robin")) return false;
        Game1.drawObjectDialogue(HouseWords.RobinOut(Farms.Label(plot)));
        Game1.afterDialogues = () => CounterMenu(shop);
        return true;
    }

    /// <summary>What the game offers at her counter (GameLocation.carpenters), with its own words and its own answers.</summary>
    internal static void CounterMenu(GameLocation shop)
    {
        var who = Game1.player;
        if (who.daysUntilHouseUpgrade.Value >= 0 || Game1.IsThereABuildingUnderConstruction())
        {
            Utility.TryOpenShopMenu("Carpenter", "Robin");
            return;
        }
        static string Word(string key) => Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu" + key);
        var choices = new List<Response> { new("Shop", Word("_Shop")) };
        var host = Game1.MasterPlayer;
        if (who.houseUpgradeLevel.Value < 3) choices.Add(new Response("Upgrade", Word(Game1.IsMasterGame ? "_UpgradeHouse" : "_UpgradeCabin")));
        else if (Game1.IsMasterGame && (host.mailReceived.Contains("ccIsComplete") || host.mailReceived.Contains("JojaMember") || host.hasCompletedCommunityCenter())
            && Game1.RequireLocation<Town>("Town").daysUntilCommunityUpgrade.Value <= 0
            && (!host.mailReceived.Contains("pamHouseUpgrade") || !host.mailReceived.Contains("communityUpgradeShortcuts")))
            choices.Add(new Response("CommunityUpgrade", Word("_CommunityUpgrade")));
        if (who.houseUpgradeLevel.Value >= 2) choices.Add(new Response("Renovate", Word(Game1.IsMasterGame ? "_RenovateHouse" : "_RenovateCabin")));
        choices.Add(new Response("Construct", Word("_Construct")));
        choices.Add(new Response("Leave", Word("_Leave")));
        shop.createQuestionDialogue(Word(""), choices.ToArray(), "carpenter");
    }

    /// <summary>
    /// Where she stands at this house: the first of the rule's spots that is free and in plain
    /// view, with nothing set down on the tile in front of it (a farm's totem stands right before
    /// the porch's left end and is two tiles tall); failing that, the first that is free.
    /// </summary>
    internal static Point? Spot(Building house, GameLocation where)
    {
        var step = Houses.DoorStep(house);
        var mail = Houses.Mailbox(house);
        Point? hidden = null;
        foreach (var t in BuilderRule.Spots(new TilePoint(house.tileX.Value, house.tileY.Value), new TilePoint(house.tilesWide.Value, house.tilesHigh.Value), new TilePoint(step.X, step.Y), new TilePoint(mail.X, mail.Y)))
        {
            var tile = new Vector2(t.X, t.Y);
            var on = where.getBuildingAt(tile);
            // Its own open tiles (its porch) are ground to stand on; any other building is not; and the ground must be
            // standable by the one rule a traveller lands by (Farms.CanLandOn: open, dry, nothing blocking, no farmer on it).
            if (on is not null && on != house) continue;
            if (where.farmers.Any(f => f.TilePoint == new Point(t.X, t.Y)) || !Farms.CanLandOn(where, new Point(t.X, t.Y))) continue;
            if (!where.objects.ContainsKey(new Vector2(t.X, t.Y + 1))) return new Point(t.X, t.Y);
            hidden ??= new Point(t.X, t.Y);
        }
        return hidden;
    }
}

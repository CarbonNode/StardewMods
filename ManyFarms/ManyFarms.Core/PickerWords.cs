namespace ManyFarms.Core;

/// <summary>
/// What the farm picker says. The same menu is a first-time joiner's "Pick your farm" and, opened
/// again weeks later, the way a farmer with a farm changes it, so every line is chosen from what
/// is true for the player at the moment: whether they have a farm, and whether one can be started.
/// </summary>
public static class PickerWords
{
    /// <summary>The menu's heading.</summary>
    public static string Title(bool hasFarm) => hasFarm ? "Change your farm" : "Pick your farm";

    /// <summary>The button that closes the picker without picking. With a farm already, nothing is being put off.</summary>
    public static string CloseButton(bool hasFarm) => hasFarm ? "Keep my farm" : "Decide later";

    /// <summary>The button under the valley map, and what another mod's menu calls the same thing.</summary>
    public static string OpenChoice(bool hasFarm, bool canStart) =>
        hasFarm ? "Change my farm" : canStart ? "Pick or start a farm" : "Pick a farm";

    /// <summary>One or two sentences on what that choice does, for a menu that explains its answers.</summary>
    public static string OpenChoiceHelp(bool hasFarm, bool canStart) =>
        $"{OpenChoice(hasFarm, canStart)} opens the list of farms: {(hasFarm ? "move to another" : "join one")}{(canStart ? ", or start one of your own" : "")}. "
        + "Whatever you pick, you keep your cabin and everything in it; a farm of your own also has a house whose door opens into those same rooms. It does not end your turn.";

    /// <summary>How many farmers a farm has. The host PC's own farmer is not one unless somebody plays it.</summary>
    public static string Farmers(int count) => count switch
    {
        <= 0 => "No farmers yet",
        1 => "1 farmer",
        _ => count + " farmers",
    };

    /// <summary>
    /// First on the row of the game's own farm, the one with the cabins: everybody who picks it
    /// farms the same land. A first-time joiner took "Squeet Farm" for a farm of their own.
    /// </summary>
    public const string MainFarmTag = "Shared main farm";

    /// <summary>
    /// The one line under a farm's name, from its parts in the order they are shown. A row with many
    /// faces on it has little room, so parts are left out whole, in the order given, until the line
    /// fits: a line cut off with "..." read as a fault, and the faces already say who farms there.
    /// </summary>
    /// <param name="parts">What the line says, left to right.</param>
    /// <param name="giveUp">Which parts may be left out, first to go first. The others always stay.</param>
    /// <param name="fits">Whether a line has room on the row, in the menu's own font.</param>
    public static string Detail(IReadOnlyList<string> parts, IReadOnlyList<int> giveUp, Func<string, bool> fits)
    {
        var left = Enumerable.Range(0, parts.Count).ToList();
        string Line() => string.Join("  |  ", left.Select(i => parts[i]));
        foreach (int index in giveUp)
        {
            if (fits(Line())) break;
            left.Remove(index);
        }
        return Line();
    }

    /// <summary>Under "Start a new farm": the one row that gives a player land nobody else farms, and a house on it.</summary>
    public static string NewFarmDetail(int free, int total) =>
        $"Your own land, with a house. {Math.Clamp(free, 0, total)} of {total} plots left.";

    /// <summary>
    /// The foot of "What kind of farm?". A started farm has a house (0.3.0): nobody's cabin moves,
    /// the house is a second front door to the same rooms. A first-time farmer who started one on
    /// 0.2.x went looking for the house.
    /// </summary>
    public const string KindsHint = "Your own land and a house. Its door opens into your own rooms: the same bed and chests as your cabin.";

    /// <summary>The row that gives a farm of their own back (0.5.1), offered only to its one farmer.</summary>
    public const string GiveUpTitle = "Give up my farm";

    /// <summary>Under it: what is lost and what comes back. Buildings, crops and chests on that land go with it.</summary>
    public static string GiveUpDetail(string mainFarm) => $"Its land, buildings and everything on it are gone for good; its plot is free again. You farm {mainFarm} with the others.";

    /// <summary>The word the player types to give the farm up (0.5.2): exactly this, in capitals.</summary>
    public const string GiveUpWord = "DELETE";

    /// <summary>The box that asks once more: "Give up Alice's farm?".</summary>
    public static string GiveUpConfirmTitle(string farmName) => $"Give up {farmName}?";

    /// <summary>Under it: what goes, and how to go on.</summary>
    public const string GiveUpConfirmWords = "Its land, buildings and everything on it are gone for good, and its plot is free again. There is no getting it back. Type DELETE to go on.";

    /// <summary>The box's answer to Give up without the word typed exactly.</summary>
    public const string GiveUpNotTyped = "Type DELETE, in capitals, to give the farm up.";

    public const string GiveUpCancel = "Cancel";

    /// <summary>The farm menu's foot after the box was cancelled.</summary>
    public const string GiveUpCancelled = "Nothing was given up.";

    /// <summary>Said once the host has done it.</summary>
    public static string AfterGiveUp(string mainFarm) => $"Your farm is given up and its plot is free. You farm {mainFarm} with the others; your cabin and everything in it are as they were.";

    /// <summary>The foot's note after a first press on a kind of land, before the second press starts the farm.</summary>
    public static string ArmKindNote(string kind) => $"Press again to start a {kind} of your own, with a house on it.";

    /// <summary>Told in chat once a farm is picked or started: where home is now, and how to reach the main farm.</summary>
    /// <param name="started">A new farm was started for them.</param>
    /// <param name="pickedMainFarm">They picked the main farm, the one with the cabins.</param>
    /// <param name="mainFarm">What the main farm is called.</param>
    /// <param name="house">The farm has its house. Without one (the ground was taken) nothing is promised about a house.</param>
    public static string AfterPick(bool started, bool pickedMainFarm, string mainFarm, bool house = true) =>
        pickedMainFarm ? $"You farm {mainFarm} with the others. Use any Farmstead Totem to visit another farm."
        : !house ? $"{RoadWords.HouseBlocked} The roads from town lead to {(started ? "your farm" : "this farm")} now. To visit {mainFarm}, use any Farmstead Totem."
        : started ? $"Your farm has a house. Its door opens into your own rooms, the same ones as your cabin on {mainFarm}. The roads from town lead to your farm now. To visit {mainFarm}, use any Farmstead Totem."
        : $"The house on this farm opens into your own rooms. The roads from town lead here now. To visit {mainFarm}, use any Farmstead Totem.";

    /// <summary>
    /// The foot's note after a first press on a farm's row, before the second press makes it theirs.
    /// For the main farm it says what picking it means, at the moment it matters.
    /// </summary>
    /// <param name="farmName">The row's name: "Squeet Farm", or "Alice's farm".</param>
    public static string ArmNote(string farmName, bool mainFarm) => mainFarm
        ? $"Press again to share {farmName} with the others. You get no land of your own there."
        : $"Press again to join {farmName}. The house there opens into your own rooms.";

    /// <summary>
    /// The foot of the picker while no row waits for its second press: what this menu changes and
    /// what it does not, and why there is no "Start a new farm" row when there is none. A row above
    /// offers "a house", so the cabin sentence says the cabin is kept with any pick: "Your cabin stays
    /// where it is" beside "with a house" left a new player to work out which of the two they get.
    /// </summary>
    public static string Hint(bool hostAllowsNewFarms, int free, int total)
    {
        const string cabin = "Whatever you pick, you keep your cabin.";
        if (!hostAllowsNewFarms) return $"{cabin} This farm's host has switched off starting new farms, so pick one of these.";
        if (free <= 0) return $"{cabin} All {total} plots are farmed, so pick one of these farms.";
        return $"The main farm is shared: pick it and you get no land of your own. {cabin}";
    }

    /// <summary>
    /// Told to a player who closed the picker without picking: where it is now. Only ways that
    /// exist for them are named.
    /// </summary>
    /// <param name="mainFarm">What the game's own farm is called ("Squeet Farm").</param>
    /// <param name="turnMenuKey">The key of a turn menu that offers the picker (Shared Day's), or null.</param>
    /// <param name="menuKey">Many Farms' own menu key, or null when none is set.</param>
    /// <param name="totemStands">A Farmstead Totem stands somewhere on the game's own farm.</param>
    /// <param name="canStart">A farm can still be started.</param>
    /// <param name="asksOnJoin">This player's own AskOnJoin: the picker opens by itself the next time they join.</param>
    public static string WayBack(string mainFarm, string? turnMenuKey, string? menuKey, bool totemStands, bool canStart, bool asksOnJoin)
    {
        string choice = OpenChoice(hasFarm: false, canStart);
        var ways = new List<string>();
        if (!string.IsNullOrWhiteSpace(turnMenuKey)) ways.Add($"press {turnMenuKey} and choose {choice}");
        if (!string.IsNullOrWhiteSpace(menuKey)) ways.Add($"press {menuKey} and choose {choice}");
        ways.Add(totemStands ? $"use any Farmstead Totem and choose {choice}"
            : $"make a Farmstead Totem (15 wood and 10 fiber), set it down, use it and choose {choice}");
        string how = ways.Count == 1 ? ways[0] : string.Join(", or ", ways);
        return $"No farm picked: you farm {mainFarm} until you pick one. To pick later, {how}.{(asksOnJoin ? " It also asks again next time you join." : "")}";
    }

    /// <summary>The same, short enough for the game's one-line message.</summary>
    public static string WayBackShort(string? turnMenuKey, string? menuKey)
    {
        string? key = !string.IsNullOrWhiteSpace(turnMenuKey) ? turnMenuKey : !string.IsNullOrWhiteSpace(menuKey) ? menuKey : null;
        return key is not null ? $"No farm picked. Press {key} to pick one later." : "No farm picked. Any Farmstead Totem lets you pick later.";
    }
}

/// <summary>When the picker may open, and whether picking a farm takes the player there at once.</summary>
public static class PickerRules
{
    /// <summary>
    /// The picker may open now. Not during a festival or a cutscene (picking would carry the player
    /// off the festival's map with the festival never ended), not while the screen fades or the
    /// player is between maps, and not during a trip by totem.
    /// </summary>
    /// <param name="eventUp">A festival or a cutscene is running.</param>
    /// <param name="fading">The screen is fading, or the player is between two maps.</param>
    /// <param name="travelling">A trip by totem is under way.</param>
    public static bool CanOpenNow(bool eventUp, bool fading, bool travelling) => !eventUp && !fading && !travelling;

    /// <summary>
    /// A farm was just picked: the player is taken to it at once. Only a player who is free to walk
    /// is: one whose day is done is held where they stand by the turn mod, and the trip would run
    /// under its waiting popup, leaving the popup's answers dead and the screen black behind it.
    /// </summary>
    /// <param name="playerFree">No menu or dialogue is up and the player is in the world.</param>
    /// <param name="eventUp">A festival or a cutscene is running.</param>
    /// <param name="travelling">A trip by totem is already under way.</param>
    /// <param name="heldByTurnMod">A turn mod (Shared Day) is holding this player until the next day.</param>
    public static bool GoesThereNow(bool playerFree, bool eventUp, bool travelling, bool heldByTurnMod) =>
        playerFree && !eventUp && !travelling && !heldByTurnMod;

    /// <summary>Told to a player who picked a farm and was not taken to it: how to get there.</summary>
    /// <param name="heldByTurnMod">Their day is done, so they cannot go anywhere until the next one.</param>
    public static string GoLater(bool heldByTurnMod) => heldByTurnMod
        ? "Your day is finished, so you stay where you are. When the next day starts, use any Farmstead Totem to go to your farm."
        : "You were not taken there now. Use any Farmstead Totem to go to your farm.";
}

/// <summary>Whether the host PC's own farmer is one of the farm's people.</summary>
public static class HostVisibility
{
    /// <param name="hostPlaysSetting">Many Farms' own HostPlays switch on the host.</param>
    /// <param name="turnModSays">
    /// What a turn mod that knows who really plays (Shared Day) says about the host's farmer:
    /// true, false, or null when it is not installed, not switched on or cannot say.
    /// </param>
    /// <returns>
    /// True when the host's farmer is counted and shown. The switch alone decides when no turn mod
    /// answers; when one does, a host PC that only hosts is hidden whatever the switch says, and a
    /// host who plays is shown unless the switch was turned off by hand.
    /// </returns>
    public static bool Shown(bool hostPlaysSetting, bool? turnModSays) => hostPlaysSetting && turnModSays != false;
}

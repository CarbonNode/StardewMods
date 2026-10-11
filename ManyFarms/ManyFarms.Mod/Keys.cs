namespace ManyFarms.Mod;

internal static class Keys
{
    public const string ModId = "CarbonNode.ManyFarms";

    /// <summary>On a spare plot: the kind of farm it was started as. A plot without it is not a farm yet.</summary>
    public const string Kind = ModId + "/Kind";

    /// <summary>On any farm: the farmers whose farm it is (see MemberList).</summary>
    public const string Members = ModId + "/Members";

    /// <summary>On a plot's cave: its mushroom boxes have been put in.</summary>
    public const string Mushrooms = ModId + "/Mushrooms";

    /// <summary>On the host's farmer: they have been handed the farm's first totem.</summary>
    public const string GotTotem = ModId + "/GotTotem";

    /// <summary>On a house: the plot it was stood for (the plot's location name). A house is found by this, wherever it stands.</summary>
    public const string HouseOf = ModId + "/HouseOf";

    /// <summary>
    /// On a house: the shape it was stood with (see HouseShape in Core: the entry it was made from, its size, its door).
    /// A house without it was stood by 0.3.0 or 0.4.0 in the cabin's shape, and 0.5.0 stands it again once, in the farmhouse's.
    /// </summary>
    public const string HouseShape = ModId + "/Shape";

    /// <summary>On a farmer: they have been told their farm has a house.</summary>
    public const string ToldHouse = ModId + "/ToldHouse";

    /// <summary>On a farmer: the picker has been put to them on a join once (0.5.2). After that it only opens from the menu or the key.</summary>
    public const string AskedOnJoin = ModId + "/AskedOnJoin";

    /// <summary>On a farmer: their house upgrade as they last saw it (nights left, level; see UpgradeMemory in Core), so a finish in their absence is told on their return.</summary>
    public const string UpgradeSeen = ModId + "/UpgradeSeen";

    /// <summary>The Farmstead Totem: a thing that is set down in the world (a "big craftable"), which anyone may use.</summary>
    public const string TotemId = ModId + "_Totem";
    public const string TotemQualifiedId = "(BC)" + TotemId;
    public const string TotemTexture = "Mods/" + ModId + "/Totem";
}

using StardewModdingAPI;

namespace ManyFarms.Mod;

public sealed class ModConfig
{
    /// <summary>
    /// Host: the farms this PC hosts have room for more farms. Off (the default) leaves a farm exactly
    /// as the game makes it, with nothing added to its save. It stays on by itself for a save that
    /// already has extra farms, because switching it off there would lose them.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Host: players may start a farm on a spare plot. Off leaves them the farms that exist.</summary>
    public bool AllowNewFarms { get; set; } = true;

    /// <summary>Host: a player who has a farm may change to another one later.</summary>
    public bool AllowSwitching { get; set; } = true;

    /// <summary>
    /// Host: somebody plays the host's own farmer. Off for a host PC that only hosts (a seeded server
    /// nobody plays): its farmer is then left out of every farm's farmers and faces, and it is handed
    /// no totem. Default true. With Shared Day running the farm this need not be set: a host PC
    /// Shared Day runs with nobody at it (its IncludeHost false, nobody in the seat) is left out by
    /// itself, and shown again while somebody plays there. Off still hides the host whatever Shared Day says.
    /// </summary>
    public bool HostPlays { get; set; } = true;

    /// <summary>Host: the kinds of farm a player may start (0 to 6, or a modded farm's ID). Empty allows every kind the game has.</summary>
    public string[] FarmTypes { get; set; } = Array.Empty<string>();

    /// <summary>Ask a player who has no farm yet to pick one when they join.</summary>
    public bool AskOnJoin { get; set; } = true;

    /// <summary>
    /// A player with a farm of their own comes home to it: the roads from the Bus Stop, the forest
    /// and the backwoods, a Warp Totem: Farm and the Return Scepter lead to their farm. Off leaves
    /// all of those leading to the game's own farm, as in an ordinary game.
    /// </summary>
    public bool RoadsLeadHome { get; set; } = true;

    /// <summary>Opens the farm menu from anywhere, without walking to a totem. None (the default) leaves that to the totems.</summary>
    public SButton MenuKey { get; set; } = SButton.None;
}

/// <summary>A player asks the host for a farm: one that exists, or a new one of a kind.</summary>
public sealed class ChoiceRequest
{
    public string? Farm { get; set; }
    public string? NewKind { get; set; }
    /// <summary>Give up the farm of their own (0.5.1): its plot is emptied and free again; they farm the main farm.</summary>
    public bool GiveUp { get; set; }
}

public sealed class ChoiceResult
{
    public bool Ok { get; set; }
    public string? Farm { get; set; }
    public bool Started { get; set; }
    /// <summary>The farm has its house. False when the ground was taken and the host could not stand one yet.</summary>
    public bool House { get; set; }
    /// <summary>Their farm was given up (0.5.1): Farm is the main farm, and the plot they left is free.</summary>
    public bool GaveUp { get; set; }
    public string? Error { get; set; }
}

/// <summary>What the host allows, so a player's menu offers only what will be granted.</summary>
public sealed class RulesMessage
{
    public int Protocol { get; set; } = ModEntry.Protocol;
    /// <summary>This farm has room for more farms. When it has not, the totem and the menu are not offered.</summary>
    public bool Enabled { get; set; }
    public bool AllowNewFarms { get; set; } = true;
    public bool AllowSwitching { get; set; } = true;
    public bool HostPlays { get; set; } = true;
    public string[] FarmTypes { get; set; } = Array.Empty<string>();
}

/// <summary>The host to a player it is about to disconnect: why, so their own screen can say.</summary>
public sealed class GoodbyeMessage
{
    public string Reason { get; set; } = "";
    public string HostVersion { get; set; } = "";
}

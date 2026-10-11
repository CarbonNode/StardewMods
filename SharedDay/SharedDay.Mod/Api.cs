namespace SharedDay.Mod;

/// <summary>
/// What Shared Day offers other mods (SMAPI's mod API: <c>helper.ModRegistry.GetApi</c> with the
/// ID <c>CarbonNode.SharedDay</c>). Copy this interface into the other mod; SMAPI matches it by
/// member name, so a consumer may leave out what it does not use. Everything is safe to read
/// every tick.
/// </summary>
public interface ISharedDayApi
{
    /// <summary>Shared Day is switched on and running the farm that is loaded.</summary>
    bool IsActive { get; }

    /// <summary>
    /// On the host: somebody plays the host PC's own farmer. True when that farmer always takes
    /// turns (IncludeHost), when somebody is sitting at the host PC now, or when it is on today's
    /// roster because somebody sat there earlier today. False for a host PC that only hosts.
    /// Only the host knows; on a player's game it is true.
    /// </summary>
    bool HostFarmerPlays { get; }

    /// <summary>The key that opens Shared Day's menu ("F8"), or "" when it has none or is not running.</summary>
    string MenuKeyName { get; }

    /// <summary>Shared Day's menu offers Many Farms' farm picker (Many Farms is installed and has the API for it).</summary>
    bool OffersFarmPicker { get; }

    /// <summary>
    /// Shared Day is holding the player at this PC where they stand until the next day: their day
    /// is done, and its waiting popup comes back whenever another menu closes. A mod that would
    /// move the player (a trip, a warp) must not while this is true.
    /// </summary>
    bool HoldsPlayer { get; }
}

/// <summary>
/// What Shared Day does about the nights of absent farmers (0.1.29), as a second interface for
/// Many Farms to read with a second GetApi call. SMAPI's proxy fails a GetApi whose interface
/// names a member the API object lacks, so a partner that put this member into its copy of
/// <see cref="ISharedDayApi"/> would get no API at all from an older Shared Day; reading it as
/// its own interface, an older Shared Day costs the partner only this one answer.
/// </summary>
public interface ISharedDayNightsApi
{
    /// <summary>
    /// The host credits every absent roster farmer's night (house and tool upgrade days, timed
    /// quests, stamina and the rest of Farmer.dayupdate) inside the farm's one real night, so an
    /// absent farmer's counters move as a connected one's do. A partner that would otherwise
    /// count those nights itself must not.
    /// </summary>
    bool CreditsAbsentNights { get; }
}

/// <inheritdoc cref="ISharedDayApi" />
public sealed class SharedDayApi : ISharedDayApi, ISharedDayNightsApi
{
    private readonly ModEntry mod;

    internal SharedDayApi(ModEntry mod) => this.mod = mod;

    public bool IsActive => mod.Active;
    public bool HostFarmerPlays => mod.HostFarmerPlays;
    public string MenuKeyName => mod.Active && mod.Config.MenuKey != StardewModdingAPI.SButton.None ? mod.Config.MenuKey.ToString() : "";
    public bool OffersFarmPicker => mod.HasFarmPicker;
    public bool HoldsPlayer => mod.HoldsPlayer;
    public bool CreditsAbsentNights => true;
}

/// <summary>
/// The part of Many Farms' API that names an absent farmer's own farm for the night shown to
/// them ("Spring 13's night on Alice's farm"). Read with its own GetApi call; a Many Farms
/// without it leaves the farm unnamed.
/// </summary>
public interface IManyFarmsNightsApi
{
    /// <summary>"Alice's farm" for a farmer with a farm of their own, or null for the main farm.</summary>
    string? FarmLabelFor(long farmerId);
}

/// <summary>
/// The part of Many Farms' API this mod uses. Many Farms is optional: without it (or with one too
/// old to have an API) the turn menu simply has no farm choice.
/// </summary>
public interface IManyFarmsApi
{
    /// <summary>The farm picker can be opened for this player right now.</summary>
    bool CanOpenFarmPicker { get; }

    /// <summary>What to call the choice, for this player now ("Pick or start a farm", "Change my farm").</summary>
    string FarmPickerLabel { get; }

    /// <summary>One or two sentences on what the choice does.</summary>
    string FarmPickerHelp { get; }

    /// <summary>Opens the picker. True when it is now the open menu.</summary>
    bool OpenFarmPicker();
}

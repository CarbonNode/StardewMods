namespace ManyFarms.Mod;

/// <summary>
/// What Many Farms offers other mods (SMAPI's mod API: <c>helper.ModRegistry.GetApi</c> with the
/// ID <c>CarbonNode.ManyFarms</c>). Copy this interface into the other mod; SMAPI matches it by
/// member name, so a consumer may leave out what it does not use. Everything here is about the
/// player at this PC and is safe to read every tick.
/// </summary>
public interface IManyFarmsApi
{
    /// <summary>
    /// The farm picker can be opened for this player right now: a farm is loaded, its host has Many
    /// Farms switched on, the player has made their farmer, and they have no farm yet or the host
    /// lets players change. False during a festival or a cutscene, while the screen fades and during
    /// a trip by totem. False whenever a menu choice for it should not be shown.
    /// </summary>
    bool CanOpenFarmPicker { get; }

    /// <summary>What to call a choice that opens the picker, for this player now ("Pick or start a farm", "Change my farm").</summary>
    string FarmPickerLabel { get; }

    /// <summary>One or two sentences on what that choice does, for a menu that explains its answers.</summary>
    string FarmPickerHelp { get; }

    /// <summary>
    /// Opens the picker (pick a farm or start one). It needs the screen: with another menu open it
    /// does nothing.
    /// </summary>
    /// <returns>True when the picker is now the open menu.</returns>
    bool OpenFarmPicker();
}

/// <summary>
/// What Many Farms offers a turn mod that plays a night for a farmer who is not there (0.5.0; Shared
/// Day 0.1.29 reads it): read with its own <c>GetApi&lt;IManyFarmsNightsApi&gt;("CarbonNode.ManyFarms")</c>.
/// Copy this interface into the other mod. Safe to call at any time; null when there is nothing to say.
/// </summary>
public interface IManyFarmsNightsApi
{
    /// <summary>
    /// The name of the farm this farmer has of their own ("Alice's farm", "Alice and Bob's farm"), to
    /// title a night played for them with ("on Alice's farm"). Null when they have no farm of their
    /// own (the main farm is theirs, as in an ordinary game), when no farm is loaded, or when this
    /// farm's host has Many Farms off.
    /// </summary>
    string? FarmLabelFor(long farmerId);
}

/// <inheritdoc cref="IManyFarmsApi" />
public sealed class ManyFarmsApi : IManyFarmsApi, IManyFarmsNightsApi
{
    private readonly ModEntry mod;

    internal ManyFarmsApi(ModEntry mod) => this.mod = mod;

    public bool CanOpenFarmPicker => mod.CanOpenPicker;
    public string FarmPickerLabel => mod.PickerLabel;
    public string FarmPickerHelp => mod.PickerHelp;
    public bool OpenFarmPicker() => mod.OpenPicker();
    public string? FarmLabelFor(long farmerId) => mod.FarmLabelFor(farmerId);
}

/// <summary>
/// The part of Shared Day's API this mod uses. Shared Day is optional: without it (or with one too
/// old to have an API) Many Farms goes by its own config.
/// </summary>
public interface ISharedDayApi
{
    /// <summary>Shared Day is switched on and running the farm that is loaded.</summary>
    bool IsActive { get; }

    /// <summary>On the host: somebody plays the host PC's own farmer (see Shared Day's README).</summary>
    bool HostFarmerPlays { get; }

    /// <summary>The key that opens Shared Day's menu ("F8"), or "" when it has none or is not running.</summary>
    string MenuKeyName { get; }

    /// <summary>Shared Day's menu offers Many Farms' picker.</summary>
    bool OffersFarmPicker { get; }

    /// <summary>Shared Day is holding the player at this PC where they stand (their day is done).</summary>
    bool HoldsPlayer { get; }
}

/// <summary>
/// The second part of Shared Day's API (0.1.29), read on its own: a Shared Day too old to have it
/// leaves it null, and an absent farmer's house upgrade then waits for them, as the game has it.
/// </summary>
public interface ISharedDayNightsApi
{
    /// <summary>
    /// Shared Day counts the nights of a farmer who is out of the game as if they had slept through
    /// them (their house upgrade among them), so Robin need not wait for them to come back.
    /// </summary>
    bool CreditsAbsentNights { get; }
}

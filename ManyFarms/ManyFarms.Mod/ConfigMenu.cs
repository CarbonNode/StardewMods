using ManyFarms.Core;
using StardewModdingAPI;

namespace ManyFarms.Mod;

/// <summary>
/// Many Farms' page in the game's own Mod Options, through Generic Mod Config Menu, so a player can
/// set the menu key and a host its switches without editing config.json by hand. GMCM is optional:
/// without it this does nothing. The interface below is the slice of GMCM's own API this mod uses;
/// SMAPI matches it by reflection, so the whole block is wrapped and a GMCM that has changed shape
/// costs the settings page, not the mod.
/// </summary>
internal static class ConfigMenu
{
    private const string GmcmId = "spacechase0.GenericModConfigMenu";

    public static void Register(IModHelper helper, IManifest manifest, Func<ModConfig> config, Action save, IMonitor monitor, Func<string> mainFarm)
    {
        var api = helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(GmcmId);
        if (api is null) return;
        try
        {
            api.Register(manifest, reset: () => Reset(config()), save: save);

            api.AddSectionTitle(manifest, () => "On this PC");
            api.AddKeybind(manifest, () => config().MenuKey, v => config().MenuKey = v,
                () => "Farm menu key", () => "Opens Where to? from anywhere, without walking to a totem. None leaves that to the totems.");
            api.AddBoolOption(manifest, () => config().AskOnJoin, v => config().AskOnJoin = v,
                () => "Ask me to pick a farm", () => "When you join a farm that has extra farms and have none of your own yet. Off: pick one at any totem instead.");
            api.AddBoolOption(manifest, () => config().RoadsLeadHome, v => config().RoadsLeadHome = v,
                () => RoadWords.OptionName, () => RoadWords.OptionHelp(mainFarm()));

            api.AddSectionTitle(manifest, () => "For the farms this PC hosts");
            api.AddParagraph(manifest, () => "These are the host's switches: they matter only when this PC hosts the farm, and take effect when the farm is next loaded. Players set nothing here.");
            api.AddBoolOption(manifest, () => config().Enabled, v => config().Enabled = v,
                () => "Room for more farms", () => "Off (the default) leaves a farm exactly as the game makes it. On adds eight spare plots players can start farms on. A save that already has extra farms keeps them whatever this says.");
            api.AddBoolOption(manifest, () => config().AllowNewFarms, v => config().AllowNewFarms = v,
                () => "Players may start farms", () => "Off leaves them the farms that exist.");
            api.AddBoolOption(manifest, () => config().AllowSwitching, v => config().AllowSwitching = v,
                () => "Players may change farms", () => "A player who has a farm may move to another later.");
            api.AddBoolOption(manifest, () => config().HostPlays, v => config().HostPlays = v,
                () => "Somebody plays the host's farmer", () => "Off for a PC that only hosts: its farmer is left out of every farm's farmers and faces, and it is handed no totem. With Shared Day running the farm, a host PC nobody sits at is left out by itself.");
        }
        catch (Exception ex)
        {
            monitor.Log("Many Farms could not add its page to Mod Options. " + ex.Message, LogLevel.Warn);
        }
    }

    private static void Reset(ModConfig config)
    {
        var fresh = new ModConfig();
        config.MenuKey = fresh.MenuKey;
        config.AskOnJoin = fresh.AskOnJoin;
        config.RoadsLeadHome = fresh.RoadsLeadHome;
        config.Enabled = fresh.Enabled;
        config.AllowNewFarms = fresh.AllowNewFarms;
        config.AllowSwitching = fresh.AllowSwitching;
        config.HostPlays = fresh.HostPlays;
    }
}

/// <summary>The part of Generic Mod Config Menu's API this mod uses. GMCM is an optional dependency.</summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);
    void AddParagraph(IManifest mod, Func<string> text);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddKeybind(IManifest mod, Func<SButton> getValue, Action<SButton> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
}

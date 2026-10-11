using SharedDay.Core;
using StardewModdingAPI;

namespace SharedDay.Mod;

/// <summary>
/// Shared Day's page in the game's own Mod Options, through Generic Mod Config Menu.
///
/// The pack already ships GMCM, so the turn board can be switched off and moved without anyone
/// editing config.json by hand. Dragging the board is still the quick way; this page is for the
/// things a drag cannot say (off, and exact numbers), and it says so.
///
/// GMCM is optional: without it this does nothing and the mod is unaffected. Every call below is
/// one whose signature was checked against GMCM's own IGenericModConfigMenuApi; the whole block is
/// still wrapped, because SMAPI matches this interface by reflection at run time and a GMCM that
/// has changed shape must cost the player their settings page, not the mod.
/// </summary>
internal static class ConfigMenu
{
    private const string GmcmId = "spacechase0.GenericModConfigMenu";

    public static void Register(IModHelper helper, IManifest manifest, Func<ModConfig> config, Action save, IMonitor monitor)
    {
        var api = helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(GmcmId);
        if (api is null) return;
        try
        {
            api.Register(manifest, reset: () => Reset(config()), save: save);

            api.AddSectionTitle(manifest, () => "The turn board while you play");
            api.AddParagraph(manifest, () => "Every farmer's face in a corner of the screen, ticked once they have played today. Drag the board with the mouse to move it; these are here for the rest.");
            api.AddBoolOption(manifest, () => config().Hud.Enabled, v => config().Hud.Enabled = v,
                () => "Show the board", () => "Off hides it completely.");
            api.AddBoolOption(manifest, () => config().Hud.OpenOnChange, v => config().Hud.OpenOnChange = v,
                () => "Open it when something changes", () => "Off: the board opens only while the pointer is on it. On: it also opens by itself for a few seconds when someone finishes or a turn begins, for playing on a controller.");
            api.AddBoolOption(manifest, () => config().Hud.PopupBackdrop, v => config().Hud.PopupBackdrop = v,
                () => "Cover the screen behind popups", () => "On: Shared Day's popups stand on a patterned backdrop that hides the world. Off: the world shows around them.");
            api.AddTextOption(manifest, () => config().Hud.Corner.ToString(), v => config().Hud.Corner = Parse(v),
                () => "Corner", () => "Which corner it sits in. Dragging the board sets this for you.",
                allowedValues: Enum.GetNames(typeof(BoardCorner)), formatAllowedValue: Pretty);
            api.AddNumberOption(manifest, () => config().Hud.OffsetX, v => config().Hud.OffsetX = v,
                () => "Across, from that corner", () => "Pixels in from the corner, if another mod's icons sit there.", min: 0, max: 2000);
            api.AddNumberOption(manifest, () => config().Hud.OffsetY, v => config().Hud.OffsetY = v,
                () => "Down, from that corner", () => "Pixels in from the corner.", min: 0, max: 2000);

            api.AddSectionTitle(manifest, () => "Joining a farm");
            api.AddBoolOption(manifest, () => config().TidyJoinList, v => config().TidyJoinList = v,
                () => "Tidy the list of farmers", () => "On: one \"Make a new farmer\" row instead of one \"New Farmer\" row per empty cabin, and your own farmers first. Off: the game's own list.");
        }
        catch (Exception ex)
        {
            // A GMCM that has changed shape must not stop the mod loading.
            monitor.Log("Shared Day could not add its page to Mod Options. " + ex.Message, LogLevel.Warn);
        }
    }

    /// <summary>Reset puts the board back to the top-left corner, flush, and switched on.</summary>
    private static void Reset(ModConfig config)
    {
        config.Hud.Enabled = true;
        config.Hud.Corner = BoardCorner.TopLeft;
        config.Hud.OffsetX = 0;
        config.Hud.OffsetY = 0;
        config.Hud.OpenOnChange = false;
        config.Hud.PopupBackdrop = true;
        config.TidyJoinList = true;
    }

    private static BoardCorner Parse(string value) =>
        Enum.TryParse<BoardCorner>(value, ignoreCase: true, out var corner) ? corner : BoardCorner.TopLeft;

    private static string Pretty(string value) => value switch
    {
        nameof(BoardCorner.TopLeft) => "Top left",
        nameof(BoardCorner.BottomLeft) => "Bottom left",
        nameof(BoardCorner.TopRight) => "Top right",
        nameof(BoardCorner.BottomRight) => "Bottom right",
        _ => value,
    };
}

/// <summary>The part of Generic Mod Config Menu's API this mod uses. GMCM is an optional dependency.</summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);
    void AddParagraph(IManifest mod, Func<string> text);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);
    void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string>? tooltip = null, string[]? allowedValues = null, Func<string, string>? formatAllowedValue = null, string? fieldId = null);
}

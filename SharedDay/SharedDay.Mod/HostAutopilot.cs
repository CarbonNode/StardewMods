using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace SharedDay.Mod;

/// <summary>
/// Stands in for the person an unattended host does not have. A farm that AutoHost loaded has
/// nobody at its keyboard, and the game still asks that PC to press things: OK on the night's
/// shipping totals (with a shared wallet the host is shown everyone's), a level-up, a villager's
/// line, a cutscene, or a popup one of a pack's other mods opens. Until it is pressed the night
/// never ends for anyone, so this presses it. It only ever touches the host's own screen, and it
/// leaves alone the save, the game's ready checks and Shared Day's own popup.
/// </summary>
internal sealed class HostAutopilot
{
    // Long enough for a menu to finish opening, short next to a night that would never end.
    private const int DialogueTicks = 120, QuestionTicks = 600, StrayMenuTicks = 600, EventTicks = 180;
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly Func<bool> unattended;
    private readonly Func<IClickableMenu?> ownMenu;
    private readonly HashSet<string> told = new();
    private IClickableMenu? seen;
    private int seenTicks;
    private int eventTicks;

    /// <summary>How many times it has pressed something, for the log and the tests.</summary>
    internal int Presses { get; private set; }

    internal HostAutopilot(IModHelper helper, IMonitor monitor, Func<bool> unattended, Func<IClickableMenu?> ownMenu)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.unattended = unattended;
        this.ownMenu = ownMenu;
    }

    // The unvalidated tick: it also runs while the night's menus are up.
    internal void OnUpdateTicked(object? sender, UnvalidatedUpdateTickedEventArgs e)
    {
        try
        {
            if (!Game1.hasLoadedGame || !Game1.IsMasterGame || Game1.gameMode != Game1.playingGameMode || !unattended())
            {
                seen = null;
                seenTicks = eventTicks = 0;
                return;
            }
            if (Game1.eventUp && Game1.CurrentEvent is { isFestival: false } cutscene)
            {
                if (++eventTicks < EventTicks) return;
                eventTicks = 0;
                if (cutscene.skippable && !cutscene.skipped)
                {
                    Tell("event:" + cutscene.id, $"Nobody is at this PC, so the cutscene '{cutscene.id}' was skipped.");
                    cutscene.skipEvent();
                    Presses++;
                }
                else if (Game1.activeClickableMenu is DialogueBox line) Press(line);
                return;
            }
            eventTicks = 0;

            var menu = Game1.activeClickableMenu;
            if (menu is null || ReferenceEquals(menu, ownMenu()))
            {
                seen = null;
                seenTicks = 0;
                return;
            }
            if (!ReferenceEquals(menu, seen))
            {
                seen = menu;
                seenTicks = 0;
            }
            seenTicks++;
            switch (menu)
            {
                // The save and the game's own "waiting for players" checks finish by themselves.
                case SaveGameMenu or ReadyCheckDialog or TitleMenu:
                    break;
                case ShippingMenu shipping:
                    if (seenTicks % 30 == 0) AdvanceShipping(shipping);
                    break;
                case LevelUpMenu level:
                    if (seenTicks >= DialogueTicks && seenTicks % 60 == 0) AdvanceLevelUp(level);
                    break;
                case DialogueBox { isQuestion: false } line:
                    if (seenTicks >= DialogueTicks && seenTicks % 30 == 0) Press(line);
                    break;
                case DialogueBox question:
                    if (seenTicks >= QuestionTicks && seenTicks % 60 == 0) AnswerLast(question);
                    break;
                default:
                    if (seenTicks >= StrayMenuTicks && seenTicks % 60 == 0) Close(menu);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Never let the stand-in break the game it is standing in for.
            Tell("error:" + ex.GetType().Name, "The unattended host could not press a menu by itself. " + ex.Message, LogLevel.Warn);
        }
    }

    // The same three steps a person takes: the totals count up, OK appears, OK is pressed.
    private void AdvanceShipping(ShippingMenu menu)
    {
        var intro = helper.Reflection.GetField<int>(menu, "introTimer");
        if (intro.GetValue() > 0) { intro.SetValue(0); return; }
        if (helper.Reflection.GetField<int>(menu, "currentPage").GetValue() != -1
            || helper.Reflection.GetField<bool>(menu, "outro").GetValue()) return;
        Tell("shipping", "Nobody is at this PC, so the night's shipping totals are confirmed by themselves.");
        helper.Reflection.GetMethod(menu, "okClicked").Invoke();
        Presses++;
    }

    private void AdvanceLevelUp(LevelUpMenu menu)
    {
        Tell("levelup", "Nobody is at this PC, so the host's level-up is confirmed by itself.");
        if (menu.isProfessionChooser)
            // The left of the two professions; the host's own farmer plays no part in the farm.
            menu.receiveLeftClick(menu.xPositionOnScreen + menu.width / 4, menu.yPositionOnScreen + Math.Max(200, menu.height / 2));
        else menu.okButtonClicked();
        Presses++;
    }

    private void Press(DialogueBox box)
    {
        if (box.transitioning) return;
        Tell("dialogue", "Nobody is at this PC, so messages on the host's screen are closed by themselves.");
        box.receiveLeftClick(box.x + box.width / 2, box.y + box.height / 2);
        Presses++;
    }

    // Nobody can answer, and an open question holds the night. The last answer is the game's
    // habit for "no", "leave" or "cancel".
    private void AnswerLast(DialogueBox box)
    {
        if (box.transitioning || box.responses is not { Length: > 0 } answers) return;
        string asked = string.Join(" ", box.dialogues ?? new List<string>());
        Tell("question:" + asked, $"Nobody is at this PC to answer \"{asked}\", so it was answered \"{answers[^1].responseText}\".", LogLevel.Warn);
        box.selectedResponse = answers.Length - 1;
        box.receiveLeftClick(box.x + box.width / 2, box.y + box.height / 2);
        Presses++;
    }

    private void Close(IClickableMenu menu)
    {
        Tell("menu:" + menu.GetType().FullName, $"Nobody is at this PC, so the menu {menu.GetType().FullName} was closed after ten seconds.", LogLevel.Warn);
        menu.exitThisMenu(playSound: false);
        if (ReferenceEquals(Game1.activeClickableMenu, menu)) Game1.activeClickableMenu = null;
        Presses++;
    }

    // Once per kind: a host presses OK every night, and the log should not fill with it.
    private void Tell(string key, string message, LogLevel level = LogLevel.Info)
    {
        if (told.Add(key)) monitor.Log(message, level);
    }
}

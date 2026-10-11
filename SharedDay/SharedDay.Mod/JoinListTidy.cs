using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharedDay.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace SharedDay.Mod;

/// <summary>
/// The game's own join screen (the list of farmers after connecting to a farm), made plain; see
/// <see cref="JoinList"/> for what a player sees and why. It is this PC's screen only: the host
/// sends the same list as ever and is told the same choice as ever, so it works against any host,
/// with or without Shared Day.
///
/// One patch, after the game has filled the list. Every row stays the game's own kind of row (a
/// subclass that changes three pieces of text), and the row a press activates holds the same
/// farmer the game's row held. The one row that is not the host's is the line a full farm shows
/// ("No room for a new farmer"): it is locked the way the game locks another player's farmer, so
/// a press on it does nothing. If the patch cannot be put on, or anything in it throws, the list
/// is left exactly as the game made it and the log says so once.
/// </summary>
internal static class JoinListTidy
{
    private static IMonitor monitor = null!;
    private static Func<bool> enabled = () => true;
    private static bool failed;
    private static System.Reflection.FieldInfo? hoverField;

    public static void Install(Harmony harmony, IMonitor log, Func<bool> isEnabled)
    {
        monitor = log;
        enabled = isEnabled;
        try
        {
            var target = AccessTools.DeclaredMethod(typeof(FarmhandMenu), "checkListPopulation")
                ?? throw new MissingMethodException(nameof(FarmhandMenu), "checkListPopulation");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(JoinListTidy), nameof(AfterListFilled)));
        }
        catch (Exception ex)
        {
            failed = true;
            monitor.Log("Shared Day could not tidy the game's join screen on this version of the game; it is the game's own list. " + ex.Message, LogLevel.Warn);
            return;
        }
        try
        {
            // The game's hover line for a locked row says it is another player's farmer. The full-farm line says its own.
            hoverField = AccessTools.Field(typeof(LoadGameMenu), "hoverText") ?? throw new MissingFieldException(nameof(LoadGameMenu), "hoverText");
            var hover = AccessTools.DeclaredMethod(typeof(FarmhandMenu), nameof(FarmhandMenu.performHoverAction))
                ?? throw new MissingMethodException(nameof(FarmhandMenu), nameof(FarmhandMenu.performHoverAction));
            harmony.Patch(hover, postfix: new HarmonyMethod(typeof(JoinListTidy), nameof(AfterHover)));
        }
        catch (Exception ex)
        {
            hoverField = null;
            monitor.Log("Shared Day could not word the hover line of a full farm on the join screen; the row itself still says it. " + ex.Message, LogLevel.Trace);
        }
    }

    /// <summary>Over the full-farm line, the hover text says what to do about it.</summary>
    private static void AfterHover(FarmhandMenu __instance, int x, int y)
    {
        if (failed || hoverField is null) return;
        try
        {
            var slots = __instance.MenuSlots;
            if (slots is null) return;
            for (int i = 0; i < __instance.slotButtons.Count; i++)
            {
                int at = __instance.currentItemIndex + i;
                if (at < slots.Count && slots[at] is Row { NoRoom: true } && __instance.slotButtons[i].containsPoint(x, y))
                    hoverField.SetValue(__instance, JoinList.NoRoomHover);
            }
        }
        catch (Exception ex)
        {
            hoverField = null;
            monitor.Log("Shared Day could not word the hover line of a full farm on the join screen. " + ex.Message, LogLevel.Trace);
        }
    }

    private static void AfterListFilled(FarmhandMenu __instance)
    {
        if (failed || !enabled()) return;
        try
        {
            var slots = __instance.MenuSlots;
            // Nothing yet, or already ours (the game fills the list again whenever the host sends it again).
            if (slots is null || slots.Count == 0 || slots.All(slot => slot is Row)) return;
            // A row of a kind this does not know: leave the whole list to the game.
            if (slots.Any(slot => slot is not FarmhandMenu.FarmhandSlot || slot is Row)) return;
            var theirs = slots.Cast<FarmhandMenu.FarmhandSlot>().ToList();
            if (theirs.Any(slot => slot.Farmer is null)) return;
            var plan = JoinList.Plan(theirs.Select(slot => new JoinRow(slot.Farmer.isCustomized.Value, slot.BelongsToAnotherPlayer())).ToList());
            if (plan.Rows.Count == 0) return;
            var tidy = new List<LoadGameMenu.MenuSlot>(plan.Rows.Count);
            foreach (int index in plan.Rows)
                tidy.Add(index == JoinList.NoRoomRow ? Row.ForNoRoom(__instance, theirs[0].Farmer)
                    : new Row(__instance, theirs[index].Farmer, index == plan.NewFarmerRow ? plan.FreeCabins : 0));
            __instance.MenuSlots = tidy;
            // The list is shorter now: the game laid its four rows and its scroll arrows out for the long one.
            __instance.currentItemIndex = 0;
            __instance.UpdateButtons();
            if (Game1.options.SnappyMenus)
            {
                __instance.populateClickableComponentList();
                __instance.snapToDefaultClickableComponent();
            }
        }
        catch (Exception ex)
        {
            failed = true;
            monitor.Log("Shared Day could not tidy the game's join screen and leaves it as the game made it. " + ex, LogLevel.Warn);
        }
    }

    /// <summary>
    /// The game's own row for a farmer, with its words put right. Picking it is the game's own
    /// <see cref="FarmhandMenu.FarmhandSlot.Activate"/> on the same farmer.
    /// </summary>
    private sealed class Row : FarmhandMenu.FarmhandSlot
    {
        private readonly LoadGameMenu list;
        /// <summary>For the one "make a new farmer" row: how many empty cabins it stands for. 0 on a made farmer's row.</summary>
        private readonly int freeCabins;

        /// <summary>The line a full farm shows in place of the new-farmer row. It holds no farmer of its own and cannot be picked.</summary>
        public bool NoRoom { get; private init; }

        public Row(FarmhandMenu menu, Farmer farmer, int freeCabins)
            : base(menu, farmer)
        {
            list = menu;
            this.freeCabins = freeCabins;
        }

        /// <param name="anyFarmer">
        /// Any farmer of the list: the game's row cannot be made without one. Nothing of it is drawn,
        /// and it is never joined as, because the row is locked.
        /// </param>
        public static Row ForNoRoom(FarmhandMenu menu, Farmer anyFarmer) =>
            // Locked the game's own way: its click handler refuses a row that "belongs to another player".
            new(menu, anyFarmer, 0) { NoRoom = true, _belongsToAnotherPlayer = true };

        private bool IsNew => !Farmer.isCustomized.Value;

        public override void Activate()
        {
            if (!NoRoom) base.Activate();
        }

        public override float getSlotAlpha() => NoRoom ? 0.5f : base.getSlotAlpha();

        public override void Draw(SpriteBatch b, int i)
        {
            if (!NoRoom) { base.Draw(b, i); return; }
            // No number, picture, date or play time: it is a line of words, not a farmer.
            var box = list.slotButtons[i].bounds;
            SpriteText.drawString(b, JoinList.NoRoomTitle, box.X + 128 + 36, box.Y + 36, 999999, -1, 999999, getSlotAlpha());
            Utility.drawTextWithShadow(b, JoinList.NoRoomDetail, Game1.dialogueFont, new Vector2(box.X + 128 + 32, box.Y + 64 + 40), Game1.textColor * getSlotAlpha());
        }

        protected override void drawSlotName(SpriteBatch b, int i)
        {
            if (!IsNew) { base.drawSlotName(b, i); return; }
            var box = list.slotButtons[i].bounds;
            SpriteText.drawString(b, JoinList.NewFarmerTitle, box.X + 128 + 36, box.Y + 36);
        }

        // An empty cabin has no farmer to show the farm's date beside; say what the row is for instead.
        protected override void drawSlotDate(SpriteBatch b, int i)
        {
            if (!IsNew) { base.drawSlotDate(b, i); return; }
            var box = list.slotButtons[i].bounds;
            Utility.drawTextWithShadow(b, JoinList.CabinsFree(Math.Max(freeCabins, 1)), Game1.dialogueFont, new Vector2(box.X + 128 + 32, box.Y + 64 + 40), Game1.textColor * getSlotAlpha());
        }

        // "Squeet Farm", not "Squeet Farm Farm".
        protected override string slotSubName() => JoinList.FarmTitle(Farmer.farmName.Value, base.slotSubName());
    }
}

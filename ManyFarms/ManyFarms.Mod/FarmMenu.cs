using ManyFarms.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace ManyFarms.Mod;

internal enum MenuMode
{
    /// <summary>Where to go: every farm, with the faces of the farmers whose farm it is.</summary>
    Travel,
    /// <summary>Which farm is yours: one that exists, or a new one.</summary>
    Choose,
    /// <summary>What kind of land a new farm is.</summary>
    Kinds,
}

/// <summary>
/// The farm menu. The totem opens it to travel; a player with no farm yet gets it to pick one.
/// A farm is a row: its name, what kind of land it is, and the faces of its farmers.
/// Travelling takes one press. Anything that changes whose farm is whose takes two on the same
/// row, with the foot of the menu saying what the second press will do.
/// </summary>
internal sealed class FarmMenu : IClickableMenu
{
    /// <param name="Parts">A farm's line in parts, and which of them go first when the faces leave little room (PickerWords.Detail).</param>
    private sealed record Row(string Title, string Detail, Farm? Farm, FarmKind? Kind, IReadOnlyList<Farmer> Faces, bool NewFarm, IReadOnlyList<string>? Parts = null, IReadOnlyList<int>? GiveUp = null, bool GivesUp = false);

    // The same wooden frame the game uses for its small panels.
    private static readonly Rectangle Frame = new(384, 396, 15, 15);
    private static readonly Rectangle UpArrow = new(421, 459, 11, 12);
    private static readonly Rectangle DownArrow = new(421, 472, 11, 12);
    private const int FaceSize = 64;
    private const int FaceGap = 8;
    private const int ButtonHeight = 56;
    private const int FirstRowId = 100;
    private const int RightButtonId = 200;
    private const int LeftButtonId = 201;
    private static readonly TimeSpan HostPatience = TimeSpan.FromSeconds(12);

    private readonly ModEntry mod;
    private readonly bool openedToChoose;
    private MenuMode mode;
    private List<Row> rows = new();
    private MenuPlan plan = MenuPlan.For(1280, 720, 0, 0);
    private int page;
    private int armed = -1;
    private string note = "";
    private bool waiting;
    private DateTime askedAt;
    private int ticks;
    private bool faceFailed;
    private bool snapNext = true;
    private int hoverRow = -1;
    private string? hoverText;
    private Rectangle leftButton, rightButton;
    private string leftText = "", rightText = "";
    /// <summary>Where to? as the valley map, when its pictures are there; else the list of rows.</summary>
    private FarmMapView? map;
    private Box head, foot;

    public MenuMode Mode => mode;
    public int RowCount => rows.Count;
    public string Note => note;
    /// <summary>What a row says under its title, as it is drawn on a row that is on this page (the lab reads it).</summary>
    public string RowDetail(int item)
    {
        if (item < 0 || item >= rows.Count) return "";
        var row = rows[item];
        int at = item - plan.First;
        if (row.Kind is not null || row.Detail.Length == 0 || map is not null || at < 0 || at >= plan.Rows.Count) return row.Detail;
        return DetailLine(row, Game1.smallFont, TextRoom(plan.Rows[at], row));
    }
    /// <summary>How many lines the foot's words take beside its buttons; it has room for two (the lab reads it).</summary>
    public int FootLines => FootText(Game1.smallFont) is { Length: > 0 } text ? text.Split('\n').Count(l => l.Trim().Length > 0) : 0;
    /// <summary>Whether a row shows a picture of its kind of land (the lab reads it).</summary>
    public bool RowHasPicture(int item) => item >= 0 && item < rows.Count && rows[item].Kind?.Picture is not null;

    /// <summary>A row's first line, the row that starts a farm, and the kind of land a row offers (the lab reads them).</summary>
    public string RowTitle(int item) => item >= 0 && item < rows.Count ? rows[item].Title : "";

    public int StartRow => rows.FindIndex(row => row.NewFarm);
    /// <summary>The row that gives the player's farm back (0.5.1), or -1 when it is not offered (the lab reads it).</summary>
    public int GiveUpRow => rows.FindIndex(row => row.GivesUp);

    public string? RowKind(int item) => item >= 0 && item < rows.Count ? rows[item].Kind?.Id : null;
    /// <summary>Where to? is drawn as the valley map (the lab reads it).</summary>
    public bool HasMap => map is not null;
    /// <summary>The map itself, when Where to? is drawn as one (the lab reads its farmsteads).</summary>
    internal FarmMapView? Map => map;
    /// <summary>The farmstead whose panel shows now: the one pointed at, or the one a controller has selected; -1 for none.</summary>
    public int PanelItem => map is null || waiting ? -1 : shown >= 0 ? shown : hoverRow;
    /// <summary>The lab points at a farmstead without a mouse; -1 gives the mouse back its say.</summary>
    public void PointAt(int item) => shown = item;
    private int shown = -1;

    public FarmMenu(ModEntry mod, MenuMode mode)
        : base(0, 0, 0, 0, showUpperRightCloseButton: true)
    {
        this.mod = mod;
        this.mode = mode;
        openedToChoose = mode != MenuMode.Travel;
        Rebuild();
    }

    private string Title => mode switch
    {
        MenuMode.Travel => "Where to?",
        // "Pick your farm" for someone with none; opened again later by a farmer who has one, it changes it.
        MenuMode.Choose => PickerWords.Title(HasFarm),
        _ => "What kind of farm?",
    };

    /// <summary>What the foot says while no row is waiting for its second press: what this menu changes, and what it does not.</summary>
    private string Hint => mode switch
    {
        MenuMode.Choose => PickerWords.Hint(mod.HostAllowsNewFarms, Farms.FreeCount(), FarmSlots.Count),
        MenuMode.Kinds => PickerWords.KindsHint,
        _ => "",
    };

    private static bool HasFarm => Farms.ChosenBy(Game1.player.UniqueMultiplayerID) is not null;

    /// <summary>What the foot says with no note (the lab reads it).</summary>
    public string FootHint => Hint;
    /// <summary>The words on the right-hand button (the lab reads it).</summary>
    public string CloseText => rightText;

    /// <summary>Reads the farms again, so a farmer who joins or a farm that is started shows while the menu is open.</summary>
    private void Rebuild()
    {
        long me = Game1.player.UniqueMultiplayerID;
        var chosen = Farms.ChosenBy(me);
        var mine = chosen ?? Farms.Main;
        var here = Game1.currentLocation as Farm;
        var next = new List<Row>();
        if (mode == MenuMode.Kinds)
        {
            foreach (var kind in mod.AllowedKinds())
                next.Add(new Row(kind.Label, kind.Description, null, kind, Array.Empty<Farmer>(), false));
        }
        else
        {
            foreach (var farm in Farms.Started())
            {
                var faces = Farms.Farmers(farm);
                var parts = new List<string> { Farms.KindLabel(farm) };
                var giveUp = new List<int>();
                if (mode == MenuMode.Travel)
                {
                    if (farm == mine) parts.Add("your farm");
                    if (farm == here) parts.Add("you are here");
                }
                else
                {
                    // The farm with the cabins is everybody's: say so first, where a cut-off line still shows it.
                    if (mode == MenuMode.Choose && farm == Farms.Main) parts.Insert(0, PickerWords.MainFarmTag);
                    // With little room beside the faces the kind of land goes first, then the count the faces already show.
                    giveUp.Add(parts.Count - 1);
                    parts.Add(PickerWords.Farmers(faces.Count));
                    giveUp.Add(parts.Count - 1);
                    if (farm == chosen)
                    {
                        parts.Add("yours now");
                        // Last of all, and only on the main farm: its row must still say what it is.
                        if (farm == Farms.Main) giveUp.Add(parts.Count - 1);
                    }
                }
                next.Add(new Row(Farms.Label(farm), string.Join("  |  ", parts), farm, null, faces, false, parts, giveUp));
            }
            if (mode == MenuMode.Choose && mod.CanStartFarm)
            {
                int free = Farms.FreeCount();
                next.Add(new Row("Start a new farm", PickerWords.NewFarmDetail(free, FarmSlots.Count), null, null, Array.Empty<Farmer>(), true));
            }
            // 0.5.1: the one farmer of a farm of their own may give it back. Last, under the farms it is not one of.
            if (mode == MenuMode.Choose && chosen is { } own && Farms.IsPool(own) && Farms.MemberIds(own).Count == 1)
                next.Add(new Row(PickerWords.GiveUpTitle, PickerWords.GiveUpDetail(mod.MainFarmName()), null, null, Array.Empty<Farmer>(), false, GivesUp: true));
        }
        if (next.Count != rows.Count) armed = -1;
        rows = next;
        Relayout();
    }

    private void Relayout()
    {
        // A kind of land is a picture, a name and two lines of words: a taller row than a farm's.
        map = mode == MenuMode.Travel
            ? FarmMapView.TryBuild(mod, rows.Select(r => r.Farm!).ToList(), rows.Select(r => r.Faces).ToList(), Farms.ChosenBy(Game1.player.UniqueMultiplayerID) ?? Farms.Main, Game1.currentLocation as Farm)
            : null;
        if (map is not null)
        {
            plan = MenuPlan.For(Game1.uiViewport.Width, Game1.uiViewport.Height, 0, 0);
            page = 0;
            xPositionOnScreen = map.Panel.X;
            yPositionOnScreen = map.Panel.Y;
            width = map.Panel.Width;
            height = map.Panel.Height;
            head = map.Head;
            foot = map.Foot;
        }
        else
        {
            plan = MenuPlan.For(Game1.uiViewport.Width, Game1.uiViewport.Height, rows.Count, page, mode == MenuMode.Kinds ? MenuPlan.TallRowHeight : MenuPlan.RowHeight);
            page = plan.Page;
            xPositionOnScreen = plan.Panel.X;
            yPositionOnScreen = plan.Panel.Y;
            width = plan.Panel.Width;
            height = plan.Panel.Height;
            head = plan.Head;
            foot = plan.Foot;
        }
        initializeUpperRightCloseButton();

        (leftText, rightText) = mode switch
        {
            // A player with no farm yet has none to change: the button says what it will do for them.
            MenuMode.Travel => (mod.CanChoose ? PickerWords.OpenChoice(HasFarm, mod.CanStartFarm) : "", "Close"),
            MenuMode.Choose => ("", openedToChoose ? PickerWords.CloseButton(HasFarm) : "Back"),
            _ => ("", "Back"),
        };
        var font = Game1.smallFont;
        int y = foot.Y + (foot.Height - ButtonHeight) / 2 + 8;
        int rightWidth = (int)font.MeasureString(rightText).X + 40;
        rightButton = new Rectangle(foot.Right - rightWidth, y, rightWidth, ButtonHeight);
        int leftWidth = leftText.Length == 0 ? 0 : (int)font.MeasureString(leftText).X + 40;
        leftButton = leftWidth == 0 ? Rectangle.Empty : new Rectangle(rightButton.X - 12 - leftWidth, y, leftWidth, ButtonHeight);

        // For a controller: the rows top to bottom (or the farms on the map, nearest first in each direction), then the buttons.
        var components = new List<ClickableComponent>();
        if (map is not null)
        {
            foreach (var pin in map.Pins)
                components.Add(new ClickableComponent(pin.Hit, "farm" + pin.Item) { myID = FirstRowId + pin.Item });
            foreach (var c in components)
            {
                c.leftNeighborID = Nearest(components, c, dx: -1, dy: 0);
                c.rightNeighborID = Nearest(components, c, dx: 1, dy: 0);
                c.upNeighborID = Nearest(components, c, dx: 0, dy: -1);
                c.downNeighborID = Nearest(components, c, dx: 0, dy: 1) is var below && below != -1 ? below : RightButtonId;
            }
        }
        else
        {
            for (int i = 0; i < plan.Rows.Count; i++)
            {
                var box = plan.Rows[i];
                components.Add(new ClickableComponent(new Rectangle(box.X, box.Y, box.Width, box.Height), "row" + i)
                {
                    myID = FirstRowId + i,
                    upNeighborID = i > 0 ? FirstRowId + i - 1 : -1,
                    downNeighborID = i < plan.Rows.Count - 1 ? FirstRowId + i + 1 : RightButtonId,
                });
            }
        }
        int lastRow = map is not null ? (components.Count > 0 ? components.OrderBy(c => c.bounds.Bottom).Last().myID : -1) : plan.Rows.Count > 0 ? FirstRowId + plan.Rows.Count - 1 : -1;
        components.Add(new ClickableComponent(rightButton, "right") { myID = RightButtonId, upNeighborID = lastRow, leftNeighborID = leftButton.IsEmpty ? -1 : LeftButtonId });
        if (!leftButton.IsEmpty)
            components.Add(new ClickableComponent(leftButton, "left") { myID = LeftButtonId, upNeighborID = lastRow, rightNeighborID = RightButtonId });
        allClickableComponents = components;
        if (snapNext && Game1.options.SnappyMenus)
        {
            snapNext = false;
            snapToDefaultClickableComponent();
        }
    }

    public override void snapToDefaultClickableComponent()
    {
        currentlySnappedComponent = getComponentWithID(plan.Rows.Count > 0 || (map is not null && map.Pins.Count > 0) ? FirstRowId : RightButtonId);
        snapCursorToCurrentSnappedComponent();
    }

    /// <summary>The component nearest to this one in a direction, for a controller on the map; -1 when there is none.</summary>
    private static int Nearest(List<ClickableComponent> all, ClickableComponent from, int dx, int dy)
    {
        int best = -1;
        double bestScore = double.MaxValue;
        foreach (var other in all)
        {
            if (other == from) continue;
            int ox = other.bounds.Center.X - from.bounds.Center.X, oy = other.bounds.Center.Y - from.bounds.Center.Y;
            int along = dx * ox + dy * oy, across = Math.Abs(dy * ox) + Math.Abs(dx * oy);
            if (along <= 0) continue;
            double score = along + across * 2.5;
            if (score < bestScore) { bestScore = score; best = other.myID; }
        }
        return best;
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Relayout();

    public override void update(GameTime time)
    {
        base.update(time);
        if (waiting && DateTime.UtcNow - askedAt > HostPatience)
        {
            waiting = false;
            armed = -1;
            note = "The host did not answer. Try again in a moment.";
        }
        if (++ticks % 30 == 0) Rebuild();
    }

    public override void receiveScrollWheelAction(int direction)
    {
        base.receiveScrollWheelAction(direction);
        Turn(direction > 0 ? -1 : 1);
    }

    private void Turn(int by)
    {
        int to = Math.Clamp(page + by, 0, plan.Pages - 1);
        if (to == page) return;
        page = to;
        armed = -1;
        note = "";
        snapNext = true;
        Game1.playSound("shwip");
        Relayout();
    }

    private void Go(MenuMode to)
    {
        mode = to;
        page = 0;
        armed = -1;
        note = "";
        snapNext = true;
        Game1.playSound("smallSelect");
        Rebuild();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);
        if (Game1.activeClickableMenu != this || waiting) return;
        if (plan.Up is { } up && up.Contains(x, y)) { Turn(-1); return; }
        if (plan.Down is { } down && down.Contains(x, y)) { Turn(1); return; }
        if (rightButton.Contains(x, y))
        {
            if (mode == MenuMode.Kinds) Go(MenuMode.Choose);
            else if (mode == MenuMode.Choose && !openedToChoose) Go(MenuMode.Travel);
            else exitThisMenu();
            return;
        }
        if (!leftButton.IsEmpty && leftButton.Contains(x, y))
        {
            if (mode == MenuMode.Travel && mod.CanChoose) Go(MenuMode.Choose);
            return;
        }
        int item = map is not null ? map.ItemAt(x, y) : plan.ItemAt(x, y);
        if (item >= 0 && item < rows.Count) Press(item);
    }

    /// <summary>A press on a row. Public so the lab can press rows without a mouse.</summary>
    public void Press(int item)
    {
        if (waiting || item < 0 || item >= rows.Count) return;
        var row = rows[item];
        switch (mode)
        {
            case MenuMode.Travel:
                if (row.Farm is { } destination)
                {
                    // A press on the farm they stand on (0.5.1) is not a trip: the totem would only set them
                    // down beside itself. The foot says so and the menu stays open for another pick.
                    if (destination == Game1.currentLocation)
                    {
                        armed = -1;
                        note = MapWords.AlreadyHere;
                        Game1.playSound("cancel");
                        break;
                    }
                    exitThisMenuNoSound();
                    mod.TravelTo(destination);
                }
                break;

            case MenuMode.Choose:
                if (row.NewFarm) { Go(MenuMode.Kinds); break; }
                if (row.GivesUp)
                {
                    // 0.5.2: a box of its own that asks for DELETE typed, instead of a second press (the owner, 2026-10-08).
                    armed = item;
                    note = "";
                    Game1.playSound("smallSelect");
                    Game1.activeClickableMenu = new GiveUpConfirm(this, Farms.ChosenBy(Game1.player.UniqueMultiplayerID) is { } own ? Farms.Label(own) : "your farm");
                    break;
                }
                if (row.Farm is not { } farm) break;
                if (farm == Farms.ChosenBy(Game1.player.UniqueMultiplayerID))
                {
                    armed = -1;
                    note = "That is your farm already.";
                    break;
                }
                if (armed != item) { Arm(item, PickerWords.ArmNote(row.Title, row.Farm == Farms.Main)); break; }
                Ask(new ChoiceRequest { Farm = farm.Name });
                break;

            case MenuMode.Kinds:
                if (row.Kind is not { } kind) break;
                if (armed != item) { Arm(item, PickerWords.ArmKindNote(row.Title)); break; }
                Ask(new ChoiceRequest { NewKind = kind.Id });
                break;
        }
    }

    /// <summary>The DELETE box was answered with the word: the host is asked to give the farm up.</summary>
    internal void GiveUpConfirmed()
    {
        if (Game1.activeClickableMenu != this) return;
        Ask(new ChoiceRequest { GiveUp = true });
    }

    /// <summary>The DELETE box was cancelled: nothing changes, and the foot says so.</summary>
    internal void GiveUpCancelled()
    {
        armed = -1;
        note = PickerWords.GiveUpCancelled;
    }

    private void Arm(int item, string what)
    {
        armed = item;
        note = what;
        Game1.playSound("smallSelect");
    }

    private void Ask(ChoiceRequest request)
    {
        waiting = true;
        askedAt = DateTime.UtcNow;
        note = "Asking the host...";
        Game1.playSound("bigSelect");
        mod.Choose(request);
    }

    /// <summary>The host's answer. A yes closes the menu from outside; a no says why and lets the player pick again.</summary>
    public void OnResult(ChoiceResult result)
    {
        waiting = false;
        armed = -1;
        if (result.Ok) return;
        note = result.Error ?? "The host said no.";
        Game1.playSound("cancel");
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        hoverText = null;
        // "Decide later" and the X close the picker: before they are pressed, they say where it will be.
        if (mode == MenuMode.Choose && openedToChoose && !HasFarm
            && (rightButton.Contains(x, y) || upperRightCloseButton?.containsPoint(x, y) == true))
        {
            hoverRow = -1;
            hoverText = Game1.parseText(mod.WayBack(), Game1.smallFont, 560);
            return;
        }
        if (map is not null)
        {
            hoverRow = map.ItemAt(x, y);
            // A controller has no pointer over the map: the farmstead it has selected shows the same panel.
            if (hoverRow < 0 && Game1.options.SnappyMenus && currentlySnappedComponent is { myID: >= FirstRowId and < RightButtonId } snapped)
                hoverRow = snapped.myID - FirstRowId;
            return;
        }
        hoverRow = plan.ItemAt(x, y);
        if (hoverRow < 0 || hoverRow >= rows.Count) return;
        var row = rows[hoverRow];
        var faces = FaceBoxes(plan.Rows[hoverRow - plan.First], row.Faces.Count, out _);
        for (int i = 0; i < faces.Count; i++)
        {
            if (!faces[i].Contains(x, y)) continue;
            var farmer = row.Faces[i];
            hoverText = Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == farmer.UniqueMultiplayerID) ? farmer.Name : farmer.Name + " (away)";
            break;
        }
    }

    /// <summary>Where a row's faces go: from its right edge, as many as leave the name room.</summary>
    private static List<Rectangle> FaceBoxes(Box row, int count, out int hidden)
    {
        int fit = Math.Max(1, (row.Width / 2) / (FaceSize + FaceGap));
        int shown = Math.Min(count, fit);
        hidden = count - shown;
        var boxes = new List<Rectangle>(shown);
        int x = row.Right - 20 - shown * (FaceSize + FaceGap) - (hidden > 0 ? 48 : 0);
        int y = row.Y + (row.Height - FaceSize) / 2;
        for (int i = 0; i < shown; i++) boxes.Add(new Rectangle(x + i * (FaceSize + FaceGap), y, FaceSize, FaceSize));
        return boxes;
    }

    public override void draw(SpriteBatch b)
    {
        if (!Game1.options.showClearBackgrounds)
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        var font = Game1.smallFont;
        int lineHeight = (int)font.MeasureString("Ay").Y;
        var titleSize = Game1.dialogueFont.MeasureString(Title);
        Utility.drawTextWithShadow(b, Title, Game1.dialogueFont, new Vector2(head.X + (head.Width - titleSize.X) / 2, head.Y + (head.Height - titleSize.Y) / 2), Game1.textColor);

        if (map is not null) map.Draw(b, PanelItem, DrawFace);
        for (int i = 0; i < plan.Rows.Count; i++)
        {
            int item = plan.First + i;
            if (item >= rows.Count) break;
            var row = rows[item];
            var box = plan.Rows[i];
            bool lit = item == armed || (item == hoverRow && !waiting);
            drawTextureBox(b, Game1.mouseCursors, Frame, box.X, box.Y, box.Width, box.Height, item == armed ? Color.Gold : lit ? Color.Wheat : Color.White, 4f, drawShadow: false);

            var faces = FaceBoxes(box, row.Faces.Count, out int hidden);
            // A kind of land shows its picture, as the game's own New Farm screen does, and what it is known for in two lines.
            int textX = TextX(box, row);
            if (row.Kind is { Picture: { } picture } kind)
                b.Draw(picture, new Vector2(box.X + 16, box.Y + (box.Height - 80) / 2), kind.PictureRect, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.9f);
            int textRoom = TextRoom(box, row);
            var details = row.Kind is not null && row.Detail.Length > 0 ? Lines(row.Detail, font, textRoom, 2) : row.Detail.Length == 0 ? new List<string>() : new List<string> { DetailLine(row, font, textRoom) };
            int textY = box.Y + (box.Height - (lineHeight * (1 + details.Count) + 2 * details.Count)) / 2 + 2;
            Utility.drawTextWithShadow(b, Fit(row.Title, font, textRoom), font, new Vector2(textX, textY), Game1.textColor);
            for (int d = 0; d < details.Count; d++)
                Utility.drawTextWithShadow(b, details[d], font, new Vector2(textX, textY + (lineHeight + 2) * (d + 1)), Game1.textColor * 0.65f);

            for (int f = 0; f < faces.Count; f++) DrawFace(b, row.Faces[f], faces[f]);
            if (hidden > 0 && faces.Count > 0)
                Utility.drawTextWithShadow(b, "+" + hidden, font, new Vector2(faces[^1].Right + 10, box.Y + (box.Height - lineHeight) / 2 + 2), Game1.textColor * 0.8f);
        }

        if (plan.Up is { } up && plan.Down is { } down)
        {
            b.Draw(Game1.mouseCursors, new Vector2(up.X, up.Y), UpArrow, Color.White * (page > 0 ? 1f : 0.35f), 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.9f);
            b.Draw(Game1.mouseCursors, new Vector2(down.X, down.Y), DownArrow, Color.White * (page < plan.Pages - 1 ? 1f : 0.35f), 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.9f);
            string pages = $"{page + 1}/{plan.Pages}";
            var size = font.MeasureString(pages);
            Utility.drawTextWithShadow(b, pages, font, new Vector2(up.X + (up.Width - size.X) / 2, (up.Bottom + down.Y - size.Y) / 2), Game1.textColor * 0.7f);
        }

        // The foot: what the next press does (or why the last one did not), then the buttons.
        if (FootText(font) is { Length: > 0 } wrapped)
        {
            var size = font.MeasureString(wrapped);
            Utility.drawTextWithShadow(b, wrapped, font, new Vector2(foot.X, rightButton.Y + (rightButton.Height - size.Y) / 2), Game1.textColor * (note.Length > 0 ? 1f : 0.7f));
        }
        DrawButton(b, rightButton, rightText);
        if (!leftButton.IsEmpty) DrawButton(b, leftButton, leftText);

        base.draw(b);
        if (map is not null && PanelItem >= 0 && map.PanelFor(PanelItem) is { } panel) map.DrawPanel(b, panel, DrawFace);
        if (hoverText is not null) drawHoverText(b, hoverText, font);
        drawMouse(b);
    }

    /// <summary>The foot's words, wrapped to the room left of its buttons; empty when there are none or no room.</summary>
    private string FootText(SpriteFont font)
    {
        int noteRoom = (leftButton.IsEmpty ? rightButton.X : leftButton.X) - foot.X - 16;
        string footText = note.Length > 0 ? note : Hint;
        return footText.Length > 0 && noteRoom > 80 ? Game1.parseText(footText, font, noteRoom) : "";
    }

    /// <summary>Where a row's words start: after its picture, when it has one.</summary>
    private static int TextX(Box box, Row row) => row.Kind is { Picture: not null } ? box.X + 16 + 88 + 16 : box.X + 24;

    /// <summary>The room a row's words have, up to its first face.</summary>
    private static int TextRoom(Box box, Row row)
    {
        var faces = FaceBoxes(box, row.Faces.Count, out _);
        return (faces.Count > 0 ? faces[0].X : box.Right) - TextX(box, row) - 20;
    }

    /// <summary>A farm's one line: its parts as far as they fit whole, and only then cut with "...".</summary>
    private static string DetailLine(Row row, SpriteFont font, int room) =>
        Fit(row.Parts is null ? row.Detail : PickerWords.Detail(row.Parts, row.GiveUp ?? Array.Empty<int>(), line => font.MeasureString(line).X <= room), font, room);

    private void DrawButton(SpriteBatch b, Rectangle box, string text)
    {
        bool over = box.Contains(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true)) && !waiting;
        drawTextureBox(b, Game1.mouseCursors, Frame, box.X, box.Y, box.Width, box.Height, over ? Color.Wheat : Color.White, 4f, drawShadow: false);
        var size = Game1.smallFont.MeasureString(text);
        Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(box.X + (box.Width - size.X) / 2, box.Y + (box.Height - size.Y) / 2 + 2), Game1.textColor * (waiting ? 0.5f : 1f));
    }

    /// <summary>A farmer's face, as the game draws it in its own multiplayer list; their initial if that cannot be drawn.</summary>
    private void DrawFace(SpriteBatch b, Farmer farmer, Rectangle cell)
    {
        b.Draw(Game1.staminaRect, cell, Game1.textColor * 0.12f);
        bool online = Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == farmer.UniqueMultiplayerID);
        if (!faceFailed)
        {
            try
            {
                farmer.FarmerRenderer.drawMiniPortrat(b, new Vector2(cell.X, cell.Y), 0.89f, cell.Width / 16f, 2, farmer, online ? 1f : 0.6f);
                return;
            }
            catch (Exception)
            {
                // A mod that replaces how farmers are drawn must not take the menu with it.
                faceFailed = true;
            }
        }
        string initial = farmer.Name.Length > 0 ? char.ToUpperInvariant(farmer.Name[0]).ToString() : "?";
        var size = Game1.dialogueFont.MeasureString(initial);
        Utility.drawTextWithShadow(b, initial, Game1.dialogueFont, new Vector2(cell.X + (cell.Width - size.X) / 2, cell.Y + (cell.Height - size.Y) / 2 + 2), Game1.textColor);
    }

    /// <summary>Text wrapped to the room, at most so many lines, the last cut with "..." if there was more.</summary>
    private static List<string> Lines(string text, SpriteFont font, int room, int most)
    {
        var lines = Game1.parseText(text, font, Math.Max(room, 40)).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count <= most) return lines;
        var kept = lines.Take(most).ToList();
        kept[most - 1] = Fit(kept[most - 1] + " " + string.Join(" ", lines.Skip(most)), font, room);
        return kept;
    }

    private static string Fit(string text, SpriteFont font, int room)
    {
        if (room <= 0 || font.MeasureString(text).X <= room) return text;
        while (text.Length > 1 && font.MeasureString(text + "...").X > room) text = text[..^1];
        return text.TrimEnd() + "...";
    }
}

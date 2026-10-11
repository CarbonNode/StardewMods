using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ManyFarms.Core;
using StardewValley;
using StardewValley.Menus;

namespace ManyFarms.Mod;

/// <summary>
/// "Give up my farm" asks once more, in a box of its own, and only goes on when the player has
/// typed DELETE (the owner, 2026-10-08: a popup that asks you to type DELETE instead of the second
/// press). Cancel, Escape or B close it with nothing done and put the farm menu back. It takes
/// the farm menu's place while it is open and hands it back after, so the menu's own state (its
/// page, its rows) is as it was.
/// </summary>
internal sealed class GiveUpConfirm : IClickableMenu
{
    private const int CancelId = 7391101, GiveUpId = 7391102;
    private static readonly Rectangle Frame = new(384, 396, 15, 15);
    private readonly FarmMenu owner;
    private readonly string farmName;
    private readonly TextBox box;
    public ClickableComponent cancelButton = new(Rectangle.Empty, "ManyFarms.GiveUp.Cancel") { myID = CancelId, rightNeighborID = GiveUpId };
    public ClickableComponent giveUpButton = new(Rectangle.Empty, "ManyFarms.GiveUp.GiveUp") { myID = GiveUpId, leftNeighborID = CancelId };
    private ClickableComponent? hovered;
    private string? notice;

    public string Title => PickerWords.GiveUpConfirmTitle(farmName);
    /// <summary>What has been typed so far (the lab reads it).</summary>
    public string Typed => box.Text ?? "";
    /// <summary>The word is typed exactly, so Give up is lit.</summary>
    public bool Armed => Typed == PickerWords.GiveUpWord;
    public Rectangle CancelBounds => cancelButton.bounds;
    public Rectangle GiveUpBounds => giveUpButton.bounds;
    /// <summary>What the menu last drew as its lines of text, for the lab.</summary>
    public IReadOnlyList<string> Lines { get; private set; } = Array.Empty<string>();

    public GiveUpConfirm(FarmMenu owner, string farmName)
        : base(0, 0, 0, 0)
    {
        this.owner = owner;
        this.farmName = farmName;
        box = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), null, Game1.smallFont, Game1.textColor) { textLimit = 16, limitWidth = false };
        box.OnEnterPressed += _ => Confirm();
        Layout();
        box.SelectMe();
        if (Game1.options.SnappyMenus)
        {
            populateClickableComponentList();
            snapToDefaultClickableComponent();
        }
    }

    private void Layout()
    {
        var view = Game1.uiViewport;
        width = Math.Min(760, view.Width - 64);
        height = Math.Min(360, view.Height - 64);
        xPositionOnScreen = (view.Width - width) / 2;
        yPositionOnScreen = (view.Height - height) / 2;
        int pad = 36, gap = 16, h = 64;
        int half = (width - pad * 2 - gap) / 2;
        int y = yPositionOnScreen + height - pad - h;
        cancelButton.bounds = new Rectangle(xPositionOnScreen + pad, y, half, h);
        giveUpButton.bounds = new Rectangle(xPositionOnScreen + pad + half + gap, y, half, h);
        box.X = xPositionOnScreen + pad;
        box.Width = width - pad * 2;
        box.Y = y - 48 - 24;
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();

    public override void snapToDefaultClickableComponent()
    {
        currentlySnappedComponent = getComponentWithID(CancelId);
        snapCursorToCurrentSnappedComponent();
    }

    /// <summary>The game's own Escape must not close this over the player's head: Cancel is the way out.</summary>
    public override bool readyToClose() => false;

    /// <summary>Type into the box without a keyboard (the lab).</summary>
    public void Type(string text) => box.Text = text;

    /// <summary>Give up: only with the word typed exactly. False, with the reason shown, otherwise.</summary>
    public bool Confirm()
    {
        if (!Armed)
        {
            Game1.playSound("cancel");
            notice = PickerWords.GiveUpNotTyped;
            return false;
        }
        Game1.playSound("bigSelect");
        Close();
        owner.GiveUpConfirmed();
        return true;
    }

    /// <summary>Cancel: nothing given up, back to the farm menu.</summary>
    public void Cancel()
    {
        Game1.playSound("bigDeSelect");
        Close();
        owner.GiveUpCancelled();
    }

    private void Close()
    {
        // Lets go of the keyboard (Game1.keyboardDispatcher) when the box held it.
        box.Selected = false;
        if (Game1.activeClickableMenu == this) Game1.activeClickableMenu = owner;
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (cancelButton.containsPoint(x, y)) Cancel();
        else if (giveUpButton.containsPoint(x, y)) Confirm();
        else if (new Rectangle(box.X, box.Y, box.Width, box.Height).Contains(x, y)) box.SelectMe();
    }

    public override void receiveRightClick(int x, int y, bool playSound = true) { }

    public override void performHoverAction(int x, int y)
    {
        var now = new[] { cancelButton, giveUpButton }.FirstOrDefault(c => c.containsPoint(x, y) && (c != giveUpButton || Armed));
        if (now is not null && now != hovered) Game1.playSound("Cowboy_Footstep");
        hovered = now;
    }

    public override void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys key)
    {
        // The box has the keyboard; only Escape means anything here, and it is Cancel.
        if (key == Microsoft.Xna.Framework.Input.Keys.Escape) Cancel();
    }

    public override void receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons button)
    {
        if (button == Microsoft.Xna.Framework.Input.Buttons.B) Cancel();
    }

    public override void draw(SpriteBatch b)
    {
        if (!Game1.options.showClearBackgrounds)
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);
        var lines = new List<string>();
        int x = xPositionOnScreen, y = yPositionOnScreen, pad = 36, inner = width - pad * 2;
        drawTextureBox(b, Game1.mouseCursors, Frame, x, y, width, height, Color.White, 4f, drawShadow: true);
        int cy = y + 28;
        string title = Fit(Title, Game1.dialogueFont, inner);
        Utility.drawTextWithShadow(b, title, Game1.dialogueFont, new Vector2(x + pad, cy), Game1.textColor);
        lines.Add(title);
        cy += (int)Game1.dialogueFont.MeasureString(title).Y + 8;
        b.Draw(Game1.staminaRect, new Rectangle(x + pad, cy, inner, 3), Game1.textColor * 0.25f);
        cy += 14;
        string words = Game1.parseText(PickerWords.GiveUpConfirmWords, Game1.smallFont, inner);
        Utility.drawTextWithShadow(b, words, Game1.smallFont, new Vector2(x + pad, cy), Game1.textColor);
        lines.Add(PickerWords.GiveUpConfirmWords);
        cy += (int)Game1.smallFont.MeasureString(words).Y + 10;
        // The box goes under the words and never over the buttons.
        box.Y = Math.Min(cy, cancelButton.bounds.Y - box.Height - 40);
        box.Draw(b);
        lines.Add("[box: " + Typed + "]");
        if (notice is not null)
        {
            Utility.drawTextWithShadow(b, Game1.parseText(notice, Game1.smallFont, inner), Game1.smallFont, new Vector2(x + pad, box.Y + box.Height + 8), Game1.textColor * 0.8f);
            lines.Add(notice);
        }
        Button(b, lines, cancelButton, PickerWords.GiveUpCancel, true);
        Button(b, lines, giveUpButton, PickerWords.GiveUpTitle, Armed);
        Lines = lines;
        drawMouse(b);
    }

    private void Button(SpriteBatch b, List<string> lines, ClickableComponent c, string label, bool enabled)
    {
        bool hot = enabled && hovered == c;
        var tint = !enabled ? Color.White * 0.45f : hot ? Color.White : new Color(228, 218, 204);
        drawTextureBox(b, Game1.mouseCursors, Frame, c.bounds.X, c.bounds.Y, c.bounds.Width, c.bounds.Height, tint, 4f, drawShadow: enabled);
        float scale = 0.8f;
        string text = Fit(label, Game1.dialogueFont, c.bounds.Width - 24, scale);
        var size = Game1.dialogueFont.MeasureString(text) * scale;
        Utility.drawTextWithShadow(b, text, Game1.dialogueFont, new Vector2(c.bounds.X + (c.bounds.Width - size.X) / 2, c.bounds.Y + (c.bounds.Height - size.Y) / 2 + 2),
            Game1.textColor * (enabled ? 1f : 0.45f), scale);
        lines.Add((enabled ? "[" : "(") + text + (enabled ? "]" : ")"));
    }

    private static string Fit(string text, SpriteFont font, float max, float scale = 1f)
    {
        if (font.MeasureString(text).X * scale <= max) return text;
        while (text.Length > 1 && font.MeasureString(text + "...").X * scale > max) text = text[..^1];
        return text.TrimEnd() + "...";
    }
}

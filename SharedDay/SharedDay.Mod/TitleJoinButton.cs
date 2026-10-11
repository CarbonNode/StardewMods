using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharedDay.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;

namespace SharedDay.Mod;

/// <summary>
/// The title screen's button for the player's farm. It is drawn over the vanilla title (no
/// menu replacement for other mods to fight) and placed by <see cref="JoinButtonLayout"/> so
/// it never covers a vanilla button. When the farm's turn tracker is known it shows the farm's
/// day and who it is waiting on, and opens the hub (<see cref="TitleHub"/>). When only an
/// address or invite code is known it is a one-click Join, exactly as the game's own
/// Co-op → Join LAN Game (or invite code) flow.
/// </summary>
public sealed class TitleJoinButton
{
    /// <summary>The snapping ID for controllers; far from the game's own title IDs (811xx).</summary>
    public const int ComponentId = 7381001;
    private static readonly Rectangle Frame = new(384, 396, 15, 15);
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly Func<HubLink> link;
    private readonly TitleHub? hub;
    private readonly List<(ClickableComponent Component, string Field, int Original)> rewired = new();
    private ClickableComponent? component;
    private float hover;
    private bool hovered;
    private string? notice;
    private DateTime noticeUntil;

    public JoinButtonPlacement? Placement { get; private set; }
    public Rectangle? Bounds => Placement is { Bounds: var b } ? new Rectangle(b.X, b.Y, b.Width, b.Height) : null;

    public TitleJoinButton(IModHelper helper, IMonitor monitor, Func<HubLink> link, TitleHub? hub = null)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.link = link;
        this.hub = hub;
    }

    private HubLink Link => link() ?? HubLink.Empty;
    private string? Address => Link.JoinAddress;
    private string? InviteCode => Link.InviteCode;
    /// <summary>Something to show: a farm to follow, a way in, or at least the player's group.</summary>
    private bool HasTarget => Link.Any;
    /// <summary>The button opens the hub when there is more to say than "join": a farm on a tracker, a ModRelay group, or no way in yet.</summary>
    public bool OpensHub => hub is not null && (Link.HasFarm || Link.GroupName is not null || !Link.CanJoin);
    private string FarmName => Link.Name ?? "the farm";
    // A host install has no launcher link file, so Link.Name is null there and the button used to
    // read "Your farm" while every other player saw the group's name. The hub already knows the
    // farm's name from its tracker; use that before falling back (the owner, 2026-10-06).
    private string Title => OpensHub
        ? Link.Name ?? hub?.Farm?.FarmName ?? "Your farm"
        : Link.Name is null ? "Join the farm" : "Join " + Link.Name;
    private string Subtitle => OpensHub
        ? hub!.Farm?.Summary ?? (Link.HasFarm ? hub.FarmError is null ? "Asking ModRelay about the farm..." : "Turns, who has played, join"
            : Link.CanJoin ? "The farm is up. Open to join" : hub!.Waking ? WakeProgress.Short(hub.WakingFor) : Link.HostState switch
            {
                "asleep" => "The farm is asleep. Open for what to do",
                "starting" => WakeProgress.Short(hub!.WakingFor),
                "offline" => "The host PC is off",
                _ => "No farm yet. Open for what to do",
            })
        : Address ?? "Invite code " + InviteCode;
    /// <summary>Why the last join did not start, while that is still worth showing.</summary>
    public string? Notice => notice is not null && DateTime.UtcNow < noticeUntil ? notice : null;

    /// <summary>The main title screen, settled, with its buttons showing and no sub-menu open.</summary>
    private static TitleMenu? MainTitle => Game1.activeClickableMenu is TitleMenu { titleInPosition: true, isTransitioningButtons: false,
        globalXOffset: 0, transitioningCharacterCreationMenu: false, logoFadeTimer: <= 0, fadeFromWhiteTimer: <= 0, buttonsToShow: >= 4 } title
        && TitleMenu.subMenu is null && title.buttons.Count > 0 ? title : null;

    public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        var title = HasTarget ? MainTitle : null;
        Restore();
        if (title is null)
        {
            // Hidden (sub-menu open, intro still playing, nothing configured): not snappable either.
            if (component is not null && Game1.activeClickableMenu is TitleMenu { allClickableComponents: { } list }) list.Remove(component);
            Placement = null;
            hovered = false;
            hover = 0;
            return;
        }
        Placement = Place(title);
        var cursor = helper.Input.GetCursorPosition().GetScaledScreenPixels();
        bool now = Bounds!.Value.Contains((int)cursor.X, (int)cursor.Y);
        if (now && !hovered) Game1.playSound("Cowboy_Footstep");
        hovered = now;
        hover = MathHelper.Clamp(hover + (hovered ? 0.15f : -0.15f), 0f, 1f);
        Rewire(title);
    }

    public void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (Placement is null || e.Button is not (SButton.MouseLeft or SButton.ControllerA)) return;
        var cursor = e.Cursor.GetScaledScreenPixels();
        if (!Bounds!.Value.Contains((int)cursor.X, (int)cursor.Y)) return;
        helper.Input.Suppress(e.Button);
        Click(new Point((int)cursor.X, (int)cursor.Y));
    }

    /// <summary>Act if <paramref name="point"/> (UI pixels) is on the button: open the hub, or join. Used by input and by the LAN tests.</summary>
    public bool Click(Point point)
    {
        var title = MainTitle;
        if (title is null || Placement is null || !Bounds!.Value.Contains(point)) return false;
        return OpensHub ? hub!.Open(title) : Join(title);
    }

    /// <summary>Start joining the farm through the game's own flow. Used by the button and by the hub.</summary>
    public bool Join(TitleMenu title, bool replaceSubmenu = false)
    {
        try
        {
            Client? client;
            var sdk = helper.Reflection.GetProperty<StardewValley.SDKs.SDKHelper>(typeof(Program), "sdk").GetValue();
            // With both known, an invite code reaches the farm from anywhere on Steam or GOG, while
            // an address may be the host's home network. Without invite-code support the address is all there is.
            bool codeFirst = InviteCode is not null && Address is not null && sdk?.Networking?.SupportsInviteCodes() == true;
            if (Address is { } address && !codeFirst)
            {
                // The game's own Join LAN Game path (CoopMenu.enterIPPressed).
                var multiplayer = helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
                client = multiplayer.InitClient(new LidgrenClient(address));
            }
            else if (InviteCode is not null)
            {
                // The game's own invite-code path (CoopMenu.enterInviteCodePressed): Steam or GOG only.
                if (sdk?.Networking?.SupportsInviteCodes() != true)
                    return Fail("Invite codes need the Steam or GOG version of the game. Use an address instead.");
                object? lobby = sdk.Networking.GetLobbyFromInviteCode(InviteCode);
                if (lobby is null) return Fail("That invite code didn't find a farm. Check it with the host.");
                client = sdk.Networking.CreateClient(lobby);
            }
            else return Fail("There is no address or invite code for this farm yet.");
            Game1.playSound("bigSelect");
            // From the hub the title is already showing a sub-menu: the farmhand menu takes its place.
            if (replaceSubmenu) TitleMenu.subMenu = new FarmhandMenu(client);
            else title.ForceSubmenu(new FarmhandMenu(client));
            monitor.Log($"Joining {FarmName} from the title screen.", LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            monitor.Log("The title-screen join could not connect: " + ex, LogLevel.Warn);
            return Fail("Couldn't start joining: " + ex.Message);
        }
    }

    private bool Fail(string message)
    {
        Game1.playSound("cancel");
        notice = message;
        noticeUntil = DateTime.UtcNow.AddSeconds(5);
        return false;
    }

    public void OnRendered(object? sender, RenderedActiveMenuEventArgs e)
    {
        if (Placement is not { } placed || Game1.activeClickableMenu is not TitleMenu title) return;
        var b = e.SpriteBatch;
        float s = placed.Scale, grow = 1f + 0.05f * hover;
        double seconds = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
        // A soft idle bob draws the eye; it settles while hovered so the target holds still.
        float bob = (float)Math.Sin(seconds * 2.2) * 2f * s * (1f - hover);
        var box = placed.Bounds;
        int w = (int)(box.Width * grow), h = (int)(box.Height * grow);
        int x = box.X - (w - box.Width) / 2, y = (int)(box.Y - (h - box.Height) / 2 + bob);

        var wood = Color.Lerp(new Color(228, 218, 204), Color.White, hover);
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, Frame, x, y, w, h, wood, 4f * s * grow, drawShadow: true);

        var lines = Lines(s * grow, placed.Compact);
        int padX = (int)((placed.Compact ? 20 : 32) * s * grow), padY = (int)((placed.Compact ? 18 : 22) * s * grow);
        if (placed.Compact)
        {
            // Two centred lines: "Join" (or "Farm", when it opens the hub) over the farm's name.
            var join = Game1.dialogueFont.MeasureString(lines.Title) * s * grow;
            var name = Game1.smallFont.MeasureString(lines.Subtitle) * 0.9f * s * grow;
            Utility.drawTextWithShadow(b, lines.Title, Game1.dialogueFont, new Vector2(x + (w - join.X) / 2, y + padY), Game1.textColor, s * grow);
            Utility.drawTextWithShadow(b, lines.Subtitle, Game1.smallFont, new Vector2(x + (w - name.X) / 2, y + padY + join.Y + 2 * s * grow),
                Game1.textColor * 0.75f, 0.9f * s * grow);
        }
        else
        {
            var titlePos = new Vector2(x + padX, y + padY);
            Utility.drawTextWithShadow(b, lines.Title, Game1.dialogueFont, titlePos, Game1.textColor, s * grow);
            // The arrow leans toward the click while hovered.
            float nudge = hover * (float)(2 + Math.Sin(seconds * 8) * 3) * s;
            Utility.drawTextWithShadow(b, ">", Game1.dialogueFont, new Vector2(x + w - padX - lines.ArrowWidth + nudge, titlePos.Y), Game1.textColor, s * grow);
            Utility.drawTextWithShadow(b, lines.Subtitle, Game1.smallFont, new Vector2(x + padX, titlePos.Y + lines.TitleHeight + 4 * s * grow),
                Game1.textColor * 0.7f, 0.9f * s * grow);
        }

        if (hover > 0)
        {
            // A light sweep across the plank while hovered.
            int band = (int)(10 * s);
            float phase = (float)(seconds * 0.9 % 1.0);
            int bandX = x + padX / 2 + (int)(phase * Math.Max(1, w - padX - band));
            b.Draw(Game1.staminaRect, new Rectangle(bandX, y + padY / 2, band, h - padY), Color.White * (0.2f * hover));
        }
        if (notice is not null && DateTime.UtcNow < noticeUntil)
        {
            var size = Game1.smallFont.MeasureString(notice);
            float nx = Math.Clamp(x + w / 2f - size.X / 2f, 8, title.width - size.X - 8);
            float ny = y + h + 8 + size.Y <= title.height ? y + h + 8 : y - size.Y - 8;
            Utility.drawTextWithShadow(b, notice, Game1.smallFont, new Vector2(nx, ny), Color.Red);
        }
        // The title menu drew the cursor before this; draw it again on top, as a hand over the button.
        title.drawMouse(b, false, hovered ? Game1.cursor_grab : -1);
    }

    private (string Title, string Subtitle, float TitleHeight, float ArrowWidth, int Width, int Height) Lines(float scale, bool compact = false)
    {
        if (compact)
        {
            // The compact sign: "Join" (or "Farm") over the farm's name (the address and the day are left out).
            string verb = OpensHub ? "Farm" : "Join";
            string name = Fit(FarmName, Game1.smallFont, 0.9f * scale, 84 * scale);
            var join = Game1.dialogueFont.MeasureString(verb) * scale;
            var label = Game1.smallFont.MeasureString(name) * 0.9f * scale;
            return (verb, name, join.Y, 0, (int)(Math.Max(join.X, label.X) + 40 * scale), (int)(join.Y + 2 * scale + label.Y + 36 * scale));
        }
        string text = Title, sub = Subtitle;
        float maxText = 560 * scale;
        text = Fit(text, Game1.dialogueFont, scale, maxText);
        sub = Fit(sub, Game1.smallFont, 0.9f * scale, maxText);
        var t = Game1.dialogueFont.MeasureString(text) * scale;
        var a = Game1.dialogueFont.MeasureString(">") * scale;
        var u = Game1.smallFont.MeasureString(sub) * 0.9f * scale;
        int width = (int)(Math.Max(t.X + 16 * scale + a.X, u.X) + 64 * scale);
        int height = (int)(t.Y + 4 * scale + u.Y + 44 * scale);
        return (text, sub, t.Y, a.X, width, height);
    }

    private static string Fit(string text, SpriteFont font, float scale, float max)
    {
        if (font.MeasureString(text).X * scale <= max) return text;
        while (text.Length > 1 && font.MeasureString(text + "...").X * scale > max) text = text[..^1];
        return text.TrimEnd() + "...";
    }

    private JoinButtonPlacement Place(TitleMenu title)
    {
        static Box Of(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
        int n = title.ShouldShrinkLogo() ? 2 : 3;
        var logo = new Box(title.globalXOffset + title.width / 2 - 200 * n, (int)(-300 * n - title.viewportY / 3f * n), 400 * n, 187 * n);
        var row = title.buttons.Select(button => Of(button.bounds)).ToArray();
        var avoid = new[] { title.muteMusicButton, title.windowedButton, title.aboutButton, title.languageButton }
            .Where(c => c is not null).Select(c => Of(c.bounds)).ToList();
        // Anything another mod added to the title's clickable list is avoided too.
        if (title.allClickableComponents is { } all)
            avoid.AddRange(all.Where(c => c.myID != ComponentId && c.visible && !title.buttons.Contains(c)).Select(c => Of(c.bounds)));
        return JoinButtonLayout.Place(new Box(0, 0, title.width, title.height), logo, row, avoid,
            scale => { var lines = Lines(scale); return (lines.Width, lines.Height); },
            scale => { var lines = Lines(scale, compact: true); return (lines.Width, lines.Height); });
    }

    /// <summary>
    /// Gamepad: a snappable component wired into the title's own neighbours, so the stick or
    /// d-pad reaches it from the nearest vanilla button and leaves it the same way.
    /// </summary>
    private void Rewire(TitleMenu title)
    {
        if (title.allClickableComponents is not { } all || Placement is not { } placed) return;
        component ??= new ClickableComponent(Rectangle.Empty, "SharedDay.Join") { myID = ComponentId };
        component.bounds = Bounds!.Value;
        component.upNeighborID = component.downNeighborID = component.leftNeighborID = component.rightNeighborID = -1;
        if (!all.Contains(component)) all.Add(component);
        ClickableComponent? Find(int id) => all.FirstOrDefault(c => c.myID == id);
        switch (placed.Spot)
        {
            case JoinButtonSpot.BetweenLogoAndButtons:
                component.upNeighborID = TitleMenu.region_muteMusic;
                component.downNeighborID = TitleMenu.region_loadButton;
                Point(Find(TitleMenu.region_loadButton), "upNeighborID");
                Point(Find(TitleMenu.region_coopButton), "upNeighborID");
                break;
            case JoinButtonSpot.TopRight:
                component.upNeighborID = TitleMenu.region_windowedButton;
                component.downNeighborID = TitleMenu.region_exitButton;
                Point(Find(TitleMenu.region_windowedButton), "downNeighborID");
                break;
            case JoinButtonSpot.SideLeft:
                component.upNeighborID = TitleMenu.region_muteMusic;
                component.downNeighborID = TitleMenu.region_newButton;
                Point(Find(TitleMenu.region_muteMusic), "downNeighborID");
                break;
            case JoinButtonSpot.SideRight:
                component.upNeighborID = TitleMenu.region_windowedButton;
                component.downNeighborID = TitleMenu.region_exitButton;
                Point(Find(TitleMenu.region_windowedButton), "downNeighborID");
                break;
            case JoinButtonSpot.BottomLeft:
                component.upNeighborID = TitleMenu.region_muteMusic;
                component.rightNeighborID = TitleMenu.region_newButton;
                Point(Find(TitleMenu.region_newButton), "leftNeighborID");
                break;
            default: // top-left and squeezed
                component.upNeighborID = TitleMenu.region_muteMusic;
                component.downNeighborID = TitleMenu.region_newButton;
                Point(Find(TitleMenu.region_muteMusic), "downNeighborID");
                break;
        }
    }

    private void Point(ClickableComponent? neighbour, string field)
    {
        if (neighbour is null) return;
        var info = AccessTools.Field(typeof(ClickableComponent), field);
        rewired.Add((neighbour, field, (int)info.GetValue(neighbour)!));
        info.SetValue(neighbour, ComponentId);
    }

    /// <summary>Undo the neighbour links (each tick, and whenever the button is hidden) so nothing points at a missing target.</summary>
    private void Restore()
    {
        foreach (var (neighbour, field, original) in rewired)
        {
            var info = AccessTools.Field(typeof(ClickableComponent), field);
            if ((int)info.GetValue(neighbour)! == ComponentId) info.SetValue(neighbour, original);
        }
        rewired.Clear();
    }
}

public sealed class JoinServerConfig
{
    /// <summary>The host's address for the title-screen Join button: an IP or host name, optionally :port (default 24642). Empty hides the button unless an invite code is set.</summary>
    public string Address { get; set; } = "";

    /// <summary>A Steam or GOG invite code, used when Address is empty.</summary>
    public string InviteCode { get; set; } = "";

    /// <summary>The farm's name on the button ("Join Sunny Farm"). Optional.</summary>
    public string Label { get; set; } = "";
}

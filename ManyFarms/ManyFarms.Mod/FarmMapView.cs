using ManyFarms.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace ManyFarms.Mod;

/// <summary>
/// Where to? as a map: the valley drawn as pixel art, a little farmstead standing where each farm
/// is, the faces of its farmers beside it, and one press on a farm to go there. The game's own
/// farm has its place in the fields; each plot is dealt a spot of open land once per save.
/// Without the pictures (a file missing) the menu falls back to its list of rows.
/// </summary>
internal sealed class FarmMapView
{
    /// <param name="Picked">On the main farm, for each face: that farmer picked it (false: they have picked no farm yet).</param>
    /// <param name="Plate">Where the main farm's standing name plate is drawn; empty on any other farm.</param>
    internal sealed record Pin(int Item, Farm Farm, Texture2D Icon, string IconName, Rectangle IconRect, Rectangle Hit, IReadOnlyList<Farmer> Faces, List<Rectangle> FaceCells, int HiddenFaces, string Label, string Kind, bool Here, bool Mine, bool Main, IReadOnlyList<bool> Picked, Rectangle Plate);

    /// <summary>What a farmstead says when it is pointed at (or selected with a controller): the farm, and who farms it, face by face.</summary>
    internal sealed record FarmPanel(Box Box, string Title, IReadOnlyList<string> TitleLines, IReadOnlyList<string> Sub, IReadOnlyList<string> Heading, IReadOnlyList<(Farmer Who, string Line, IReadOnlyList<string> Lines)> Rows, string More, string Foot)
    {
        /// <summary>Every word on the panel, top to bottom (the lab reads it).</summary>
        public string Text => string.Join("\n", new[] { Title }.Concat(Sub).Concat(Heading).Concat(Rows.Select(r => r.Line)).Append(More).Append(Foot).Where(l => l.Length > 0));
    }

    private const int PanelPad = 20;
    private const int PanelFace = 32;
    private static readonly Rectangle PanelFrame = new(0, 256, 60, 60);

    private static readonly Rectangle HereArrow = new(421, 472, 11, 12);
    private static Texture2D? mapTexture;
    /// <summary>What each map pixel is, read off the picture once: the dealing stands farmsteads on grass and fits each kind to its country.</summary>
    private static Terrain[]? terrain;
    private static Terrain TerrainAt(int x, int y) => terrain is null ? Terrain.Other : terrain[y * FarmMap.Width + x];
    private static readonly Dictionary<string, Texture2D?> icons = new();
    private static bool loadFailed;

    public Box Panel { get; }
    public Box Head { get; }
    public Box Foot { get; }
    public Rectangle MapRect { get; }
    /// <summary>Screen pixels per map pixel; whole numbers where the screen allows, else a fraction down to 1.</summary>
    public float Scale { get; }
    /// <summary>Screen pixels per farmstead pixel (2 on any map drawn larger than about 1.2x).</summary>
    public int IconScale { get; }
    public IReadOnlyList<Pin> Pins { get; }

    private FarmMapView(Box panel, Box head, Box foot, Rectangle mapRect, float scale, int iconScale, List<Pin> pins)
    {
        Panel = panel; Head = head; Foot = foot; MapRect = mapRect; Scale = scale; IconScale = iconScale; Pins = pins;
    }

    /// <summary>The map for these farms, or null when the pictures are not there.</summary>
    public static FarmMapView? TryBuild(ModEntry mod, IReadOnlyList<Farm> farms, IReadOnlyList<IReadOnlyList<Farmer>> faces, Farm? mine, Farm? here)
    {
        var map = Map(mod);
        if (map is null) return null;
        int screenW = Game1.uiViewport.Width, screenH = Game1.uiViewport.Height;
        // As large as the screen allows, up to 2x; a whole number when it fits, else the fraction (the map is drawn point-sampled, as the game draws its world at any zoom).
        float fitW = (screenW - MenuPlan.Margin * 2 - MenuPlan.Pad * 2) / (float)FarmMap.Width;
        float fitH = (screenH - MenuPlan.Margin * 2 - MenuPlan.HeadHeight - MenuPlan.FootHeight - MenuPlan.Pad * 2) / (float)FarmMap.Height;
        float scale = Math.Clamp(Math.Min(fitW, fitH), 1f, 2f);
        if (scale >= 2f) scale = 2f;
        int mapW = (int)(FarmMap.Width * scale), mapH = (int)(FarmMap.Height * scale);
        int width = mapW + MenuPlan.Pad * 2;
        int height = MenuPlan.HeadHeight + mapH + MenuPlan.FootHeight + MenuPlan.Pad * 2;
        var panel = new Box(Math.Max(0, (screenW - width) / 2), Math.Max(0, (screenH - height) / 2), width, height);
        var head = new Box(panel.X + MenuPlan.Pad, panel.Y + MenuPlan.Pad, width - MenuPlan.Pad * 2, MenuPlan.HeadHeight);
        var foot = new Box(panel.X + MenuPlan.Pad, panel.Bottom - MenuPlan.Pad - MenuPlan.FootHeight, width - MenuPlan.Pad * 2, MenuPlan.FootHeight);
        var mapRect = new Rectangle(head.X, head.Bottom, mapW, mapH);

        // The farmsteads are small against the map, so the land reads as miles; the faces a little smaller than in the rows.
        int iconScale = scale >= 1.2f ? 2 : 1;
        int face = scale >= 1.5f ? 40 : 32;
        var placements = farms.Select(f => new FarmPlacement(Farms.IsPool(f) ? (FarmSlots.TryFarmSlot(f.Name, out int n) ? n : 1) : 0, Farms.KindOf(f)?.Id, !Farms.IsPool(f))).ToList();
        var spots = FarmMap.Deal(Game1.uniqueIDForThisGame, placements, TerrainAt);
        var pins = new List<Pin>();
        for (int i = 0; i < farms.Count; i++)
        {
            var farm = farms[i];
            bool main = !Farms.IsPool(farm);
            string iconName = FarmMap.IconFor(Farms.KindOf(farm)?.Id, main);
            var icon = Icon(mod, iconName);
            // A pack that has the mod's code but not its newest picture still gets a map.
            if (icon is null && main) icon = Icon(mod, iconName = FarmMap.MainIconBefore);
            if (icon is null) icon = Icon(mod, iconName = "standard");
            if (icon is null) return null;
            var spot = spots[i];
            int w = icon.Width * iconScale, h = icon.Height * iconScale;
            int x = mapRect.X + (int)(spot.X * scale) - w / 2, y = mapRect.Y + (int)((spot.Y + FarmMap.IconDown) * scale) - h;
            x = Math.Clamp(x, mapRect.X + 4, mapRect.Right - w - 4);
            y = Math.Clamp(y, mapRect.Y + 4, mapRect.Bottom - h - 4);
            var iconRect = new Rectangle(x, y, w, h);
            var who = faces[i];
            int shown = Math.Min(who.Count, 2);
            var cells = new List<Rectangle>();
            // The faces stand to the right of the farmstead, or to its left when the map's edge is near (the spot was dealt with that room).
            bool left = FarmMap.FacesLeft(spot.X);
            for (int f = 0; f < shown; f++)
                cells.Add(new Rectangle(left ? iconRect.X - 4 - (f + 1) * (face + 4) + 4 : iconRect.Right + 4 + f * (face + 4), iconRect.Center.Y - face / 2, face, face));
            var hit = iconRect;
            foreach (var cell in cells) hit = Rectangle.Union(hit, cell);
            hit.Inflate(6, 6);
            var picked = main ? Farms.MainRoster().Select(entry => entry.Picked).ToList() : who.Select(_ => true).ToList();
            // The main farm wears its name: a plate under its farmstead, kept on the map.
            var plate = Rectangle.Empty;
            if (main)
            {
                var size = Game1.smallFont.MeasureString(MapWords.MainPlate);
                plate = new Rectangle(iconRect.Center.X - (int)size.X / 2 - 8, iconRect.Bottom + 2, (int)size.X + 16, (int)size.Y + 4);
                plate.X = Math.Clamp(plate.X, mapRect.X + 2, Math.Max(mapRect.X + 2, mapRect.Right - plate.Width - 2));
                if (plate.Bottom > mapRect.Bottom - 2) plate.Y = iconRect.Y - plate.Height - 2;
            }
            pins.Add(new Pin(i, farm, icon, iconName, iconRect, hit, who, cells, who.Count - shown, Farms.Label(farm), Farms.KindLabel(farm), farm == here, farm == mine, main, picked, plate));
        }
        return new FarmMapView(panel, head, foot, mapRect, scale, iconScale, pins);
    }

    public int ItemAt(int x, int y)
    {
        // The last drawn is on top, so the last hit wins.
        for (int i = Pins.Count - 1; i >= 0; i--)
            if (Pins[i].Hit.Contains(x, y)) return Pins[i].Item;
        return -1;
    }

    /// <summary>
    /// What a farmstead's panel must stand beside and never cover: the farmstead with its faces and,
    /// on the main farm, its "Main farm" plate (in the first lab photograph the panel lay over the plate).
    /// </summary>
    internal static Box About(Pin pin)
    {
        var all = pin.Plate.IsEmpty ? pin.Hit : Rectangle.Union(pin.Hit, pin.Plate);
        return new Box(all.X, all.Y, all.Width, all.Height);
    }

    /// <summary>
    /// The panel for a farmstead: the farm's name, what it is, and who farms it with their faces. On
    /// the main farm those are the farmers with no land of their own (the ones who picked it and the
    /// ones who have picked nothing yet, each said so); the host PC's own farmer is never among them
    /// unless somebody plays it. It stands beside the farmstead, never over it, and on the screen.
    /// </summary>
    public FarmPanel? PanelFor(int item)
    {
        var pin = Pins.FirstOrDefault(p => p.Item == item);
        if (pin is null) return null;
        var font = Game1.smallFont;
        int screenW = Game1.uiViewport.Width, screenH = Game1.uiViewport.Height;
        var about = About(pin);
        // No wider than the room beside the farmstead, so the panel never has to lie over it; long lines wrap.
        int textRoom = Math.Clamp(HoverPanel.WidthRoom(screenW, about) - PanelPad * 2, 160, 400);
        List<string> Wrap(string text) => text.Length == 0 ? new List<string>() : Game1.parseText(text, font, textRoom).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        var online = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();
        // A farmer's line is wrapped beside their face, never cut: "Bob (has not picked a farm y..." hid the one thing the panel is for.
        var rows = new List<(Farmer, string, IReadOnlyList<string>)>();
        var rowFont = font;
        int rowRoom = textRoom - PanelFace - 10;
        int listed = Math.Min(pin.Faces.Count, MapWords.MostRows);
        for (int i = 0; i < listed; i++)
        {
            var farmer = pin.Faces[i];
            string note = MapWords.Note(pin.Main, i < pin.Picked.Count && pin.Picked[i], online.Contains(farmer.UniqueMultiplayerID));
            string said = note.Length == 0 ? farmer.Name : $"{farmer.Name} ({note})";
            var parts = Game1.parseText(said, rowFont, rowRoom).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (parts.Count == 0) parts.Add(said);
            rows.Add((farmer, said, parts));
        }
        bool houseHere = pin.Mine && !pin.Main && Houses.On(pin.Farm) is not null;
        var sub = Wrap(MapWords.Sub(pin.Main, pin.Kind, pin.Mine, pin.Here, houseHere));
        var heading = Wrap(MapWords.Heading(pin.Main, pin.Faces.Count));
        string more = MapWords.More(pin.Faces.Count - listed), foot = MapWords.Foot(pin.Here);

        int line = (int)font.MeasureString("Ay").Y;
        // A long name ("Alice, Bob and Carol's farm") takes a second line rather than being cut.
        var title = Game1.parseText(pin.Label, Game1.dialogueFont, textRoom).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (title.Count == 0) title.Add(pin.Label);
        float widest = title.Max(l => Game1.dialogueFont.MeasureString(l).X);
        foreach (string l in sub.Concat(heading).Append(more).Append(foot)) widest = Math.Max(widest, font.MeasureString(l).X);
        foreach (var (_, _, parts) in rows) widest = Math.Max(widest, PanelFace + 10 + parts.Max(l => font.MeasureString(l).X));
        int width = Math.Min((int)widest, textRoom) + PanelPad * 2;
        int height = PanelPad * 2 + title.Count * (int)Game1.dialogueFont.MeasureString("Ay").Y + 4
            + sub.Count * line + 10 + heading.Count * line + rows.Sum(r => RowHeight(r.Item3.Count, line)) + (more.Length > 0 ? line : 0) + 8 + line;
        return new FarmPanel(HoverPanel.Place(screenW, screenH, width, height, about), pin.Label, title, sub, heading, rows, more, foot);
    }

    public void DrawPanel(SpriteBatch b, FarmPanel panel, Action<SpriteBatch, Farmer, Rectangle> drawFace)
    {
        var font = Game1.smallFont;
        int line = (int)font.MeasureString("Ay").Y;
        var box = panel.Box;
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, PanelFrame, box.X, box.Y, box.Width, box.Height, Color.White);
        int x = box.X + PanelPad, y = box.Y + PanelPad, room = box.Width - PanelPad * 2;
        foreach (string l in panel.TitleLines)
        {
            Utility.drawTextWithShadow(b, Short(l, Game1.dialogueFont, room), Game1.dialogueFont, new Vector2(x, y), Game1.textColor);
            y += (int)Game1.dialogueFont.MeasureString("Ay").Y;
        }
        y += 4;
        foreach (string l in panel.Sub) { Utility.drawTextWithShadow(b, l, font, new Vector2(x, y), Game1.textColor * 0.7f); y += line; }
        y += 10;
        foreach (string l in panel.Heading) { Utility.drawTextWithShadow(b, l, font, new Vector2(x, y), Game1.textColor); y += line; }
        foreach (var (who, _, parts) in panel.Rows)
        {
            int tall = RowHeight(parts.Count, line) - 6;
            drawFace(b, who, new Rectangle(x, y + 3 + (tall - PanelFace) / 2, PanelFace, PanelFace));
            int textY = y + 3 + (tall - parts.Count * line) / 2 + 2;
            foreach (string l in parts) { Utility.drawTextWithShadow(b, l, font, new Vector2(x + PanelFace + 10, textY), Game1.textColor); textY += line; }
            y += tall + 6;
        }
        if (panel.More.Length > 0) { Utility.drawTextWithShadow(b, panel.More, font, new Vector2(x, y), Game1.textColor * 0.7f); y += line; }
        y += 8;
        Utility.drawTextWithShadow(b, panel.Foot, font, new Vector2(x, y), Game1.textColor * 0.7f);
    }

    /// <summary>The height of one farmer's row: their face, or their wrapped line when that is taller.</summary>
    private static int RowHeight(int lines, int line) => Math.Max(PanelFace, lines * line) + 6;

    private static string Short(string text, SpriteFont font, int room)
    {
        if (room <= 0 || font.MeasureString(text).X <= room) return text;
        while (text.Length > 1 && font.MeasureString(text + "...").X > room) text = text[..^1];
        return text.TrimEnd() + "...";
    }

    public void Draw(SpriteBatch b, int hovered, Action<SpriteBatch, Farmer, Rectangle> drawFace)
    {
        b.Draw(mapTexture, MapRect, new Rectangle(0, 0, FarmMap.Width, FarmMap.Height), Color.White);
        // The main farm's plate goes down first, so a farmstead that stands close is drawn over it and not under.
        foreach (var pin in Pins)
        {
            if (pin.Plate.IsEmpty) continue;
            b.Draw(Game1.staminaRect, new Rectangle(pin.Plate.X - 2, pin.Plate.Y - 2, pin.Plate.Width + 4, pin.Plate.Height + 4), new Color(91, 43, 42));
            b.Draw(Game1.staminaRect, pin.Plate, new Color(255, 232, 170));
            Utility.drawTextWithShadow(b, MapWords.MainPlate, Game1.smallFont, new Vector2(pin.Plate.X + 8, pin.Plate.Y + 3), Game1.textColor);
        }
        foreach (var pin in Pins)
        {
            bool lit = pin.Item == hovered;
            if (lit)
            {
                var glow = pin.Hit;
                b.Draw(Game1.staminaRect, glow, Color.White * 0.22f);
            }
            // A shadow under the farmstead, so it sits on the land rather than floating.
            b.Draw(Game1.staminaRect, new Rectangle(pin.IconRect.X + 4, pin.IconRect.Bottom - 3, pin.IconRect.Width - 8, 4), Color.Black * 0.25f);
            b.Draw(pin.Icon, pin.IconRect, Color.White);
            for (int f = 0; f < pin.FaceCells.Count; f++) drawFace(b, pin.Faces[f], pin.FaceCells[f]);
            if (pin.HiddenFaces > 0 && pin.FaceCells.Count > 0)
                Utility.drawTextWithShadow(b, "+" + pin.HiddenFaces, Game1.smallFont, new Vector2(pin.FaceCells[^1].Right + 4, pin.FaceCells[^1].Y + 8), Game1.textColor);
            if (pin.Here)
            {
                // The game's own "you are here": a little arrow bobbing above the farm.
                float bob = (float)Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 250.0) * 3f;
                int s = IconScale + 1;
                b.Draw(Game1.mouseCursors, new Vector2(pin.IconRect.Center.X - HereArrow.Width * s / 2f, pin.IconRect.Y - HereArrow.Height * s - 6 + bob), HereArrow, Color.White, 0f, Vector2.Zero, s, SpriteEffects.None, 0.95f);
            }
        }
    }

    private static Texture2D? Map(ModEntry mod)
    {
        if (mapTexture is not null || loadFailed) return mapTexture;
        try
        {
            mapTexture = mod.Helper.ModContent.Load<Texture2D>("assets/map.png");
            var data = new Color[mapTexture.Width * mapTexture.Height];
            mapTexture.GetData(data);
            terrain = ReadTerrain(data, mapTexture.Width, mapTexture.Height);
        }
        catch (Exception ex)
        {
            loadFailed = true;
            mod.Monitor.Log("Many Farms has no map picture (assets/map.png), so Where to? is a list. " + ex.Message, LogLevel.Warn);
        }
        return mapTexture;
    }

    /// <summary>
    /// What each pixel of the map is, by its colour: blue is water, grey and white are rock (the mountains),
    /// dark green is forest, lighter green grass, and a tan pixel is sand only where water is near (else it is a road).
    /// </summary>
    internal static Terrain[] ReadTerrain(Color[] data, int width, int height)
    {
        var first = new Terrain[FarmMap.Width * FarmMap.Height];
        for (int y = 0; y < FarmMap.Height && y < height; y++)
            for (int x = 0; x < FarmMap.Width && x < width; x++)
            {
                var c = data[y * width + x];
                int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
                first[y * FarmMap.Width + x] =
                    c.B > c.G + 12 && c.B > c.R + 30 ? Terrain.Water :
                    max - min < 30 && c.R > 70 ? Terrain.Rock :
                    c.G > c.R + 10 && c.G > c.B + 10 ? (c.G < 100 ? Terrain.Forest : Terrain.Grass) :
                    c.R > 170 && c.G > 140 && c.B < 170 && c.R >= c.G && c.R - c.B > 40 ? Terrain.Sand :
                    Terrain.Other;
            }
        var result = (Terrain[])first.Clone();
        for (int y = 0; y < FarmMap.Height; y++)
            for (int x = 0; x < FarmMap.Width; x++)
            {
                if (first[y * FarmMap.Width + x] != Terrain.Sand) continue;
                bool nearWater = false;
                for (int dy = -24; dy <= 24 && !nearWater; dy += 3)
                    for (int dx = -24; dx <= 24; dx += 3)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < FarmMap.Width && yy < FarmMap.Height && first[yy * FarmMap.Width + xx] == Terrain.Water) { nearWater = true; break; }
                    }
                if (!nearWater) result[y * FarmMap.Width + x] = Terrain.Other;
            }
        return result;
    }

    private static Texture2D? Icon(ModEntry mod, string name)
    {
        if (icons.TryGetValue(name, out var known)) return known;
        Texture2D? texture = null;
        try { texture = mod.Helper.ModContent.Load<Texture2D>($"assets/farms/{name}.png"); }
        catch (Exception ex) { mod.Monitor.Log($"Many Farms has no picture for a {name} farm (assets/farms/{name}.png). {ex.Message}", LogLevel.Warn); }
        icons[name] = texture;
        return texture;
    }
}

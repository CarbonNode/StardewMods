using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.GameData;

namespace ManyFarms.Mod;

/// <summary>A kind of farm the game can make: one of its seven, or one a mod or the game added to Data/AdditionalFarms (Meadowlands is one of those).</summary>
internal sealed record FarmKind(string Id, int WhichFarm, ModFarmType? Mod, string Label, string Description, Texture2D? Picture, Rectangle PictureRect)
{
    public string MapAsset => "Maps\\" + (Mod?.MapName ?? WhichFarm switch
    {
        1 => "Farm_Fishing",
        2 => "Farm_Foraging",
        3 => "Farm_Mining",
        4 => "Farm_Combat",
        5 => "Farm_FourCorners",
        6 => "Farm_Island",
        _ => "Farm",
    });

    /// <summary>The Data/Locations entry that holds this kind's fish, forage and weeds, as the game names it.</summary>
    public string DataKey => "Farm_" + (Mod?.Id ?? WhichFarm switch
    {
        1 => "Riverland",
        2 => "Forest",
        3 => "Hilltop",
        4 => "Wilderness",
        5 => "FourCorners",
        6 => "Beach",
        _ => "Standard",
    });

    public bool MonstersAtNight => Mod?.SpawnMonstersByDefault ?? WhichFarm == Farm.combat_layout;
}

internal static class FarmKinds
{
    private static readonly (string Key, string Fallback)[] Vanilla =
    {
        ("Character_FarmStandard", "Standard Farm"),
        ("Character_FarmFishing", "Riverland Farm"),
        ("Character_FarmForaging", "Forest Farm"),
        ("Character_FarmMining", "Hill-top Farm"),
        ("Character_FarmCombat", "Wilderness Farm"),
        ("Character_FarmFourCorners", "Four Corners Farm"),
        ("Character_FarmBeach", "Beach Farm"),
    };

    private static List<FarmKind>? all;

    public static void Forget() => all = null;

    public static IReadOnlyList<FarmKind> All()
    {
        if (all is not null) return all;
        var kinds = new List<FarmKind>();
        for (int i = 0; i < Vanilla.Length; i++)
        {
            var (name, description) = Words("Strings\\UI:" + Vanilla[i].Key, Vanilla[i].Fallback);
            kinds.Add(new FarmKind(i.ToString(), i, null, name, description, Game1.mouseCursors, VanillaPicture(i)));
        }
        try
        {
            foreach (var mod in DataLoader.AdditionalFarms(Game1.content))
            {
                if (string.IsNullOrWhiteSpace(mod?.Id) || string.IsNullOrWhiteSpace(mod.MapName) || kinds.Any(k => k.Id == mod.Id)) continue;
                var (name, description) = Words(mod.TooltipStringPath, mod.Id);
                kinds.Add(new FarmKind(mod.Id, Farm.mod_layout, mod, name, description, ModPicture(mod), new Rectangle(0, 0, 22, 20)));
            }
        }
        catch (Exception)
        {
            // Without the list of added farms the game's own seven are still there.
        }
        return all = kinds;
    }

    public static FarmKind? Find(string? id) => id is null ? null : All().FirstOrDefault(k => k.Id == id);

    /// <summary>The kind of the game's own farm.</summary>
    public static FarmKind? OfMainFarm() => Find(Game1.GetFarmTypeID());

    /// <summary>The pictures the game's own New Farm screen shows for its seven kinds, in LooseSprites\Cursors.</summary>
    private static Rectangle VanillaPicture(int whichFarm) => whichFarm switch
    {
        5 => new Rectangle(0, 345, 22, 20),
        6 => new Rectangle(22, 345, 22, 20),
        _ => new Rectangle(22 * whichFarm, 324, 22, 20),
    };

    /// <summary>A modded kind's picture, when it brings one (the game's New Farm screen uses the same); else the Standard farm's.</summary>
    private static Texture2D? ModPicture(ModFarmType mod)
    {
        if (string.IsNullOrWhiteSpace(mod.IconTexture)) return null;
        try { return Game1.content.Load<Texture2D>(mod.IconTexture); }
        catch (Exception) { return null; }
    }

    // The game keeps a farm's name and its description in one string, "Name_Description".
    private static (string Name, string Description) Words(string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path)) return (fallback, "");
        try
        {
            string text = Game1.content.LoadString(path);
            // A string that is not there comes back as its own address.
            if (text.Contains("Strings", StringComparison.OrdinalIgnoreCase) && text.Contains(':')) return (fallback, "");
            string[] parts = text.Split('_', 2);
            string name = parts[0].Trim();
            string description = parts.Length > 1 ? parts[1].Trim() : "";
            return (name.Length == 0 ? fallback : name, description);
        }
        catch (Exception)
        {
            return (fallback, "");
        }
    }
}

/// <summary>The game's farm type while one farm of another kind does its work, and what it was before.</summary>
internal readonly struct KindScope
{
    public readonly bool Active;
    public readonly int WhichFarm;
    public readonly ModFarmType? Mod;
    public readonly bool Monsters;

    public KindScope(int whichFarm, ModFarmType? mod, bool monsters)
    {
        Active = true;
        WhichFarm = whichFarm;
        Mod = mod;
        Monsters = monsters;
    }

    /// <summary>
    /// The game has one farm type for the whole save and its farm code reads it (which forage, ore
    /// in the quarry, monsters at night, where the greenhouse stands). A second farm of another
    /// kind borrows that setting for the length of one call and puts it back, whatever happens.
    /// </summary>
    public static KindScope Enter(Farm? farm)
    {
        if (farm is null || !Farms.IsPool(farm) || Farms.KindOf(farm) is not { } kind) return default;
        var before = new KindScope(Game1.whichFarm, Game1.whichModFarm, Game1.spawnMonstersAtNight);
        Game1.whichFarm = kind.WhichFarm;
        Game1.whichModFarm = kind.Mod;
        if (Game1.spawnMonstersAtNight != kind.MonstersAtNight) Game1.spawnMonstersAtNight = kind.MonstersAtNight;
        return before;
    }

    public void Exit()
    {
        if (!Active) return;
        Game1.whichFarm = WhichFarm;
        Game1.whichModFarm = Mod;
        if (Game1.spawnMonstersAtNight != Monsters) Game1.spawnMonstersAtNight = Monsters;
    }
}

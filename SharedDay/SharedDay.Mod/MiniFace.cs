using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharedDay.Core;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace SharedDay.Mod;

/// <summary>
/// A farmer's face as the farm sees them (0.1.44): the game's own small portrait, plus the two
/// things it leaves out - their accessory (a beard, glasses, earrings) and the hat they wear.
///
/// The owner, 2026-10-16, looking at the turn board on the first real day of the group roster: "NOT
/// Sure this is fully accurate as to his characters looks". He was right. FarmerRenderer's
/// drawMiniPortrat paints the base head and the hair and nothing else, so MOSS (accessory 12)
/// lost his, and OAKLEY (accessory 0, facial hair) was drawn clean-shaven.
///
/// The head and the hair are still the game's own call: it alone can recolour a farmer's skin and
/// eyes (its baseTexture and executeRecolorActions are not ours to use), and leaving them to it
/// keeps the face the game's face through a game update. The pieces it skips are laid on top from
/// <see cref="FaceLayout"/>, which carries the same arithmetic the world draw uses. One difference
/// stands: an accessory the world tucks under the hair is painted over it here, which shows only
/// where a long style would have crossed it.
/// </summary>
internal static class MiniFace
{
    /// <summary>The picture is this many sprite pixels square: a head with the room a hat needs around it.</summary>
    internal const int Span = FaceLayout.Span;

    /// <summary>
    /// Draws the face into a square <see cref="Span"/> by <see cref="Span"/> sprite pixels wide at
    /// <paramref name="scale"/> screen pixels each, from <paramref name="topLeft"/>.
    /// </summary>
    internal static void Draw(SpriteBatch b, Farmer who, Vector2 topLeft, float scale, float alpha = 1f, float depth = 0.89f)
    {
        var head = topLeft + new Vector2(FaceLayout.HatOverhang * scale, FaceLayout.HatOverhang * scale);
        who.FarmerRenderer.drawMiniPortrat(b, head, depth, scale, 2, who, alpha);

        var hat = HatOf(who);
        var look = new FaceLook(who.IsMale, -1, who.hair.Value, who.accessory.Value, hat?.Index ?? -1, HatHairOffset(who, hat));
        foreach (var layer in FaceLayout.Front(look, FarmerRenderer.hairStylesTexture.Width, FarmerRenderer.accessoriesTexture.Width, hat?.Texture.Width ?? 1))
        {
            var texture = layer.Piece switch
            {
                FacePiece.Accessory => FarmerRenderer.accessoriesTexture,
                FacePiece.Hat => hat?.Texture,
                _ => null
            };
            if (texture is null) continue;
            var source = layer.Piece == FacePiece.Hat && hat is { Odd: true } odd ? odd.Source : layer.Source;
            var tint = layer.TintWithHair ? HairColour(who) : layer.Piece == FacePiece.Hat && who.hat.Value?.isPrismatic.Value == true ? Utility.GetPrismaticColor() : Color.White;
            b.Draw(texture, topLeft + new Vector2(layer.X * scale, layer.Y * scale),
                new Rectangle(source.X, source.Y, source.Width, source.Height), tint * alpha,
                0f, Vector2.Zero, scale, SpriteEffects.None, depth);
        }
    }

    private static Color HairColour(Farmer who) => who.prismaticHair.Value ? Utility.GetPrismaticColor() : who.hairstyleColor.Value;

    /// <summary>The hat's place on its sheet. <paramref name="Odd"/> marks one the game could not look up, which carries its own rectangle.</summary>
    private sealed record WornHat(Texture2D Texture, int Index, Box Source, bool Odd);

    private static WornHat? HatOf(Farmer who)
    {
        if (who.hat.Value is not { } worn || who.bathingClothes.Value) return null;
        ParsedItemData data = ItemRegistry.GetDataOrErrorItem(worn.QualifiedItemId);
        var texture = data.GetTexture();
        if (texture is null) return null;
        if (!data.IsErrorItem) return new WornHat(texture, data.SpriteIndex, default, false);
        var rect = data.GetSourceRect();
        return new WornHat(texture, data.SpriteIndex, new Box(rect.X, rect.Y, rect.Width, rect.Height), true);
    }

    /// <summary>How far this farmer's hairstyle pushes their hat down, as the game decides it.</summary>
    private static int HatHairOffset(Farmer who, WornHat? hat)
    {
        if (hat is null || who.hat.Value?.ignoreHairstyleOffset.Value == true) return 0;
        int style = who.hair.Value;
        if (style < 0) return 0;
        return FaceLayout.HatOffsetByHairstyle[style % FaceLayout.HatOffsetByHairstyle.Length];
    }
}

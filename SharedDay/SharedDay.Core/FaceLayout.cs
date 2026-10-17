namespace SharedDay.Core;

/// <summary>One piece of a farmer's head, in the order the game paints them.</summary>
public enum FacePiece
{
    /// <summary>Skin, eyes and eyebrows: the farmer's own recoloured sprite sheet.</summary>
    Base,

    /// <summary>A beard, glasses, earrings: whichever accessory they chose.</summary>
    Accessory,

    /// <summary>The hairstyle, tinted with the colour they picked.</summary>
    Hair,

    /// <summary>The hat they are wearing, which overhangs the head on three sides.</summary>
    Hat
}

/// <summary>
/// Where one piece comes from and where it goes, in sprite pixels from the top-left of the
/// picture. <see cref="TintWithHair"/> marks the pieces the game paints in the farmer's hair
/// colour (the hair itself, and facial hair); the others are drawn as they are.
/// </summary>
public readonly record struct FaceLayer(FacePiece Piece, Box Source, int X, int Y, bool TintWithHair);

/// <summary>What a farmer looks like, as far as a picture of their head is concerned.</summary>
/// <param name="IsMale">Male heads are a pixel shorter than female ones.</param>
/// <param name="Hair">The hairstyle to draw (Farmer.getHair, so a hat that hides hair gives -1).</param>
/// <param name="HairChoice">The hairstyle they chose (Farmer.hair), which decides the hair's own offset.</param>
/// <param name="Accessory">Farmer.accessory, or -1 for none.</param>
/// <param name="Hat">The hat's sprite index in its sheet, or -1 when they wear none.</param>
/// <param name="HatHairOffset">How far their hairstyle pushes a hat down (FarmerRenderer.hairstyleHatOffset), 0 when the hat ignores it.</param>
public readonly record struct FaceLook(bool IsMale, int Hair, int HairChoice, int Accessory, int Hat, int HatHairOffset);

/// <summary>
/// A farmer's face for the turn board and the tracker (0.1.44).
///
/// The game's own small portrait (FarmerRenderer.drawMiniPortrat, what the multiplayer list
/// uses and what this mod used until now) paints two layers only: the base head and the hair.
/// It never paints the accessory or the hat, so a bearded farmer came out clean-shaven and a
/// hat never showed at all - the owner, 2026-10-16, of MOSS (accessory 12) and OAKLEY (accessory
/// 0, facial hair): "NOT Sure this is fully accurate as to his characters looks". This lays the
/// pieces out the way the game lays them out in the world, facing the viewer, so the face on the
/// board is the face on the farm.
///
/// All of it is plain arithmetic in sprite pixels (the game draws four screen pixels to one at
/// its usual scale), taken from FarmerRenderer.drawHairAndAccesories for a farmer standing and
/// facing down, so it can be checked without the game.
/// </summary>
public static class FaceLayout
{
    /// <summary>The head itself.</summary>
    public const int Head = 16;

    /// <summary>A hat sits two pixels out from the head on the left, the right and the top...</summary>
    public const int HatOverhang = 2;

    /// <summary>...so a picture that holds a head AND its hat is this wide, with the head that far in.</summary>
    public const int Span = Head + HatOverhang * 2;

    /// <summary>How far down the standing frame pushes everything that sits on the head (featureYOffsetPerFrame[0]).</summary>
    private const int StandingOffset = 1;

    /// <summary>FarmerRenderer.hairstyleHatOffset: how far a hat sits down the head, per hairstyle.</summary>
    public static readonly int[] HatOffsetByHairstyle = { 0, 0, 0, 4, 0, 0, 3, 0, 4, 0, 0, 0, 0, 0, 0, 0 };

    /// <summary>Accessories the game paints in the farmer's hair colour: beards and moustaches.</summary>
    public static bool IsFacialHair(int accessory) => accessory < 6 || (accessory >= 19 && accessory <= 22);

    /// <summary>Accessories that go under the hair rather than over it.</summary>
    public static bool UnderHair(int accessory) => accessory < 8 || IsFacialHair(accessory);

    /// <summary>Where a hairstyle is on its sheet. The head takes the top 15 rows: the rest of the style hangs past it.</summary>
    public static Box HairSource(int hair, int sheetWidth) =>
        new(hair * Head % sheetWidth, hair * Head / sheetWidth * 96, Head, 15);

    /// <summary>Where an accessory is on accessories.png.</summary>
    public static Box AccessorySource(int accessory, int sheetWidth) =>
        new(accessory * Head % sheetWidth, accessory * Head / sheetWidth * 32, Head, Head);

    /// <summary>Where a hat is on its sheet: 20 pixels square, with the four facings stacked below each other.</summary>
    public static Box HatSource(int hat, int sheetWidth) =>
        new(20 * hat % sheetWidth, 20 * hat / sheetWidth * 20 * 4, 20, 20);

    /// <summary>
    /// The face, front on, in paint order. The caller supplies the sheets' widths (a content pack
    /// may have widened them) and draws each layer at its own offset, scaled as it likes.
    /// </summary>
    public static IReadOnlyList<FaceLayer> Front(FaceLook look, int hairSheetWidth, int accessorySheetWidth, int hatSheetWidth)
    {
        var layers = new List<FaceLayer>(4);
        int inset = HatOverhang;

        // The head. A male head is a row shorter, which is how the game crops it.
        layers.Add(new FaceLayer(FacePiece.Base, new Box(0, 0, Head, look.IsMale ? 15 : Head), inset, inset, false));

        // The accessory, a pixel below where the hair sits, under the hair or over it.
        FaceLayer? accessory = look.Accessory >= 0
            ? new FaceLayer(FacePiece.Accessory, AccessorySource(look.Accessory, accessorySheetWidth),
                inset, inset + StandingOffset + 1, IsFacialHair(look.Accessory))
            : null;
        if (accessory is { } under && UnderHair(look.Accessory)) layers.Add(under);

        // The hair. The game nudges it a pixel for the styles drawn for the other gender.
        if (look.Hair >= 0)
        {
            int nudge = look.IsMale && look.HairChoice >= 16 ? -1 : !look.IsMale && look.HairChoice < 16 ? 1 : 0;
            layers.Add(new FaceLayer(FacePiece.Hair, HairSource(look.Hair, hairSheetWidth), inset, inset + StandingOffset + nudge, true));
        }

        if (accessory is { } over && !UnderHair(look.Accessory)) layers.Add(over);

        // The hat, over everything, hanging off the head by the room this picture leaves for it.
        if (look.Hat >= 0)
            layers.Add(new FaceLayer(FacePiece.Hat, HatSource(look.Hat, hatSheetWidth),
                inset - HatOverhang, inset - HatOverhang + look.HatHairOffset / 4, false));

        return layers;
    }
}

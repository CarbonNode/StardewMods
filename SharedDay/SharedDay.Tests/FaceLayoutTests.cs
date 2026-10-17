using Xunit;
using SharedDay.Core;

namespace SharedDay.Tests;

public sealed class FaceLayoutTests
{
    private const int Hairs = 128;
    private const int Accessories = 128;
    private const int Hats = 240;

    private static FaceLook Look(int hair = 0, int accessory = -1, int hat = -1, bool male = true, int hatHairOffset = 0) =>
        new(male, hair, hair, accessory, hat, hatHairOffset);

    private static IReadOnlyList<FaceLayer> Front(FaceLook look) => FaceLayout.Front(look, Hairs, Accessories, Hats);

    [Fact]
    public void APlainFaceIsTheHeadAndTheHair()
    {
        var layers = Front(Look());
        Assert.Equal(new[] { FacePiece.Base, FacePiece.Hair }, layers.Select(l => l.Piece));
        var head = layers[0];
        Assert.Equal(new Box(0, 0, 16, 15), head.Source);
        Assert.Equal(FaceLayout.HatOverhang, head.X);
        Assert.Equal(FaceLayout.HatOverhang, head.Y);
        // The hair sits one pixel lower, as the game's own small portrait draws it.
        Assert.Equal(head.Y + 1, layers[1].Y);
        Assert.True(layers[1].TintWithHair);
    }

    [Fact]
    public void AFemaleHeadIsAFullSquareAndAMansStyleOnHerSitsLower()
    {
        Assert.Equal(new Box(0, 0, 16, 16), Front(Look(male: false)).First(l => l.Piece == FacePiece.Base).Source);
        // Her own styles (0-15) are drawn a pixel down; a style from the other sheet is not.
        Assert.Equal(FaceLayout.HatOverhang + 2, Front(Look(hair: 4, male: false)).First(l => l.Piece == FacePiece.Hair).Y);
        Assert.Equal(FaceLayout.HatOverhang + 1, Front(Look(hair: 20, male: false)).First(l => l.Piece == FacePiece.Hair).Y);
        // A man wearing one of her styles is nudged the other way.
        Assert.Equal(FaceLayout.HatOverhang, Front(Look(hair: 20)).First(l => l.Piece == FacePiece.Hair).Y);
    }

    [Fact]
    public void ABeardIsPaintedInTheHairColourUnderTheHair()
    {
        var layers = Front(Look(accessory: 0));
        Assert.Equal(new[] { FacePiece.Base, FacePiece.Accessory, FacePiece.Hair }, layers.Select(l => l.Piece));
        var beard = layers[1];
        Assert.True(beard.TintWithHair);
        Assert.Equal(new Box(0, 0, 16, 16), beard.Source);
        Assert.Equal(FaceLayout.HatOverhang + 2, beard.Y);
        // OAKLEY, the farmer made on 2026-10-16, wears accessory 0: the board showed him shaved.
        Assert.Contains(layers, l => l.Piece == FacePiece.Accessory);
    }

    [Fact]
    public void GlassesGoOverTheHairAndKeepTheirOwnColours()
    {
        // MOSS wears accessory 12, which is not facial hair: over the hair, painted as it is.
        var layers = Front(Look(accessory: 12));
        Assert.Equal(new[] { FacePiece.Base, FacePiece.Hair, FacePiece.Accessory }, layers.Select(l => l.Piece));
        var worn = layers[2];
        Assert.False(worn.TintWithHair);
        Assert.Equal(new Box(12 * 16 % Accessories, 12 * 16 / Accessories * 32, 16, 16), worn.Source);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(18, false)]
    [InlineData(19, true)]
    [InlineData(22, true)]
    [InlineData(23, false)]
    public void TheGamesOwnRuleSaysWhichAccessoriesAreFacialHair(int accessory, bool facial)
    {
        Assert.Equal(facial, FaceLayout.IsFacialHair(accessory));
        // Facial hair always goes under the hair; so does everything below the eighth accessory.
        Assert.Equal(facial || accessory < 8, FaceLayout.UnderHair(accessory));
    }

    [Fact]
    public void AHatHangsOffTheHeadAndTheRoomIsThere()
    {
        var layers = Front(Look(hat: 3));
        Assert.Equal(FacePiece.Hat, layers[^1].Piece);
        var hat = layers[^1];
        Assert.Equal(new Box(20 * 3 % Hats, 20 * 3 / Hats * 80, 20, 20), hat.Source);
        // It starts in the picture's own corner: the room left around the head is exactly its overhang.
        Assert.Equal(0, hat.X);
        Assert.Equal(0, hat.Y);
        Assert.Equal(FaceLayout.Head + FaceLayout.HatOverhang * 2, FaceLayout.Span);
        Assert.True(hat.X + hat.Source.Width <= FaceLayout.Span);
        Assert.True(hat.Y + hat.Source.Height <= FaceLayout.Span);
    }

    [Fact]
    public void AHairstyleThatPushesAHatDownPushesItDownHere()
    {
        // FarmerRenderer.hairstyleHatOffset: style 3 wears its hat a pixel lower.
        Assert.Equal(4, FaceLayout.HatOffsetByHairstyle[3]);
        Assert.Equal(1, Front(Look(hair: 3, hat: 7, hatHairOffset: 4)).First(l => l.Piece == FacePiece.Hat).Y);
        Assert.Equal(0, Front(Look(hair: 0, hat: 7)).First(l => l.Piece == FacePiece.Hat).Y);
    }

    [Fact]
    public void AHatThatHidesTheHairLeavesNoHairToDraw()
    {
        // Farmer.getHair answers -1 for a hat that hides it; nothing is drawn in its place.
        var layers = Front(new FaceLook(true, -1, 12, -1, 5, 0));
        Assert.Equal(new[] { FacePiece.Base, FacePiece.Hat }, layers.Select(l => l.Piece));
    }

    [Fact]
    public void EveryLayerStaysInsideThePicture()
    {
        foreach (int accessory in new[] { -1, 0, 7, 12, 20 })
            foreach (int hat in new[] { -1, 0, 40 })
                foreach (bool male in new[] { true, false })
                    foreach (var layer in Front(Look(hair: male ? 0 : 20, accessory: accessory, hat: hat, male: male)))
                    {
                        Assert.True(layer.X >= 0 && layer.Y >= 0, $"{layer.Piece} starts outside the picture");
                        Assert.True(layer.X + layer.Source.Width <= FaceLayout.Span, $"{layer.Piece} runs off the side");
                    }
    }

    [Fact]
    public void ASheetWideEnoughForSeveralRowsWrapsTheWayTheGameWrapsIt()
    {
        // accessories.png is eight across: the ninth is the first of the second row, 32 pixels down.
        Assert.Equal(new Box(0, 32, 16, 16), FaceLayout.AccessorySource(8, 128));
        Assert.Equal(new Box(16, 32, 16, 16), FaceLayout.AccessorySource(9, 128));
        // hairstyles.png is eight across with 96 pixels per row, and the head takes the top 15.
        Assert.Equal(new Box(0, 96, 16, 15), FaceLayout.HairSource(8, 128));
        // hats.png is twelve across at 20 pixels, with the four facings stacked under each other.
        Assert.Equal(new Box(0, 80, 20, 20), FaceLayout.HatSource(12, 240));
    }
}

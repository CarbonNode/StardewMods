using SharedDay.Core;
using Xunit;

namespace SharedDay.Tests;

/// <summary>
/// The title screen's join button against the vanilla title layout (TitleMenu.setUpIcons and
/// draw in Stardew 1.6.15) at every common resolution and UI scale: it must stay on screen and
/// never cover a vanilla button, whoever the player is and whatever their screen.
/// </summary>
public sealed class JoinButtonLayoutTests
{
    private static readonly (int W, int H)[] Resolutions =
    {
        (1024, 768), (1280, 720), (1280, 800), (1280, 1024), (1366, 768), (1440, 900), (1600, 900), (1680, 1050),
        (1920, 1080), (1920, 1200), (2560, 1080), (2560, 1440), (3440, 1440), (3840, 2160)
    };

    private static readonly float[] UiScales = { 0.75f, 1f, 1.25f, 1.5f, 2f };

    /// <summary>A long label at full size; the real button is measured from the game's fonts.</summary>
    private static (int, int) Measure(float scale) => ((int)(460 * scale), (int)(124 * scale));

    /// <summary>The compact sign: "Join" over a long farm name.</summary>
    private static (int, int) MeasureCompact(float scale) => ((int)(120 * scale), (int)(104 * scale));

    private sealed record Title(Box Screen, Box Logo, Box[] Row, Box[] Avoid);

    /// <summary>The vanilla title screen at a menu size of <paramref name="w"/> by <paramref name="h"/> UI pixels.</summary>
    private static Title Vanilla(int w, int h)
    {
        const int zoom = 3; // TitleMenu.pixelZoom
        int buttonsWidth = 74 * 4 * zoom + 24 * zoom;
        int x = w / 2 - buttonsWidth / 2, y = h - 58 * zoom - 8 * zoom;
        var row = Enumerable.Range(0, 4).Select(i => new Box(x + i * (74 + 8) * zoom, y, 74 * zoom, 58 * zoom)).ToArray();
        int n = h <= 850 ? 2 : 3; // ShouldShrinkLogo
        int logoY = (int)(-300 * n - -1000f / 3f * n); // viewportY settles at -1000
        var logo = new Box(w / 2 - 200 * n, logoY, 400 * n, 187 * n);
        var avoid = new[]
        {
            new Box(16, 16, 36, 36), // mute
            new Box(w - 36 - 16, 16, 36, 36), // windowed
            new Box(w - 22 * zoom - 8 * zoom * 2, h - 25 * zoom - 8 * zoom, 22 * zoom, 25 * zoom), // about
            new Box(w - 22 * zoom - 8 * zoom * 2, h - 25 * zoom * 2 - 16 * zoom, 27 * zoom, 25 * zoom) // language
        };
        return new(new Box(0, 0, w, h), logo, row, avoid);
    }

    public static IEnumerable<object[]> Screens()
    {
        foreach (var (w, h) in Resolutions)
            foreach (float scale in UiScales)
            {
                int menuW = (int)(w / scale), menuH = (int)(h / scale);
                // Below this the vanilla title itself does not fit (its button row is 960 wide).
                if (menuW < 1000 || menuH < 560) continue;
                yield return new object[] { w, h, scale, menuW, menuH };
            }
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void StaysOnScreenAndOffEveryVanillaButton(int w, int h, float uiScale, int menuW, int menuH)
    {
        var title = Vanilla(menuW, menuH);
        var placed = JoinButtonLayout.Place(title.Screen, title.Logo, title.Row, title.Avoid, Measure, MeasureCompact);
        var where = $"{w}x{h} at UI scale {uiScale} ({menuW}x{menuH}): {placed}";
        Assert.True(title.Screen.Contains(placed.Bounds), "off screen: " + where);
        foreach (var button in title.Row.Concat(title.Avoid))
            Assert.False(placed.Bounds.Intersects(button), $"covers a vanilla button {button}: {where}");
        Assert.True(placed.Scale >= 0.55f, where);
        // Where the vanilla buttons already overlap the logo (very short windows) nothing can
        // avoid both; everywhere else the logo stays clear too.
        bool cramped = title.Row.Any(r => r.Intersects(title.Logo));
        if (!cramped)
        {
            Assert.NotEqual(JoinButtonSpot.Squeezed, placed.Spot);
            Assert.False(placed.Bounds.Intersects(title.Logo), "covers the logo: " + where);
        }
    }

    [Fact]
    public void FullHdPutsAFullSizeButtonBetweenTheLogoAndTheButtons()
    {
        var title = Vanilla(1920, 1080);
        var placed = JoinButtonLayout.Place(title.Screen, title.Logo, title.Row, title.Avoid, Measure, MeasureCompact);
        Assert.Equal(JoinButtonSpot.BetweenLogoAndButtons, placed.Spot);
        Assert.Equal(1f, placed.Scale);
        Assert.InRange(placed.Bounds.X + placed.Bounds.Width / 2, 1920 / 2 - 1, 1920 / 2 + 1);
    }

    [Fact]
    public void ShortWindowsUseACornerInsteadOfCoveringTheLogo()
    {
        // 1600x900: the logo is full size and only 41 pixels separate it from the buttons.
        var title = Vanilla(1600, 900);
        var placed = JoinButtonLayout.Place(title.Screen, title.Logo, title.Row, title.Avoid, Measure, MeasureCompact);
        Assert.NotEqual(JoinButtonSpot.BetweenLogoAndButtons, placed.Spot);
        Assert.NotEqual(JoinButtonSpot.Squeezed, placed.Spot);
        Assert.True(placed.Bounds.Y >= 16 + 36, "clears the mute button");
    }

    [Fact]
    public void AModMovingTheButtonsIsRespected()
    {
        // Another mod drops a fifth button into the gap: the join button must go elsewhere.
        var title = Vanilla(1920, 1080);
        var intruder = new Box(1920 / 2 - 150, 700, 300, 120);
        var placed = JoinButtonLayout.Place(title.Screen, title.Logo, title.Row, title.Avoid.Append(intruder).ToArray(), Measure, MeasureCompact);
        Assert.False(placed.Bounds.Intersects(intruder));
        Assert.True(title.Screen.Contains(placed.Bounds));
    }

    [Fact]
    public void AWideLogoOnAShortWindowUsesTheCompactSignBesideIt()
    {
        // 1440x900: the full-size logo leaves 17 pixels above the buttons and 120 beside it.
        var title = Vanilla(1440, 900);
        var placed = JoinButtonLayout.Place(title.Screen, title.Logo, title.Row, title.Avoid, Measure, MeasureCompact);
        Assert.True(placed.Compact, placed.ToString());
        Assert.NotEqual(JoinButtonSpot.Squeezed, placed.Spot);
        Assert.False(placed.Bounds.Intersects(title.Logo));
    }

    [Fact]
    public void TinyWindowsStillKeepTheButtonOnScreenAndOffTheButtons()
    {
        var title = Vanilla(1000, 560);
        var placed = JoinButtonLayout.Place(title.Screen, title.Logo, title.Row, title.Avoid, Measure, MeasureCompact);
        Assert.True(title.Screen.Contains(placed.Bounds), placed.ToString());
        Assert.All(title.Row.Concat(title.Avoid), b => Assert.False(placed.Bounds.Intersects(b)));
    }
}

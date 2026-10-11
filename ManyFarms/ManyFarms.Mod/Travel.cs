using Microsoft.Xna.Framework;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Extensions;

namespace ManyFarms.Mod;

/// <summary>
/// The trip itself: what the game does for its own warp totems (the farmer raises their arms, three
/// totems rise, a flash, and they are there), with a farm of the player's choosing at the other end.
/// </summary>
internal static class Travel
{
    private static readonly Color Glow = new(255, 190, 90);

    /// <summary>A trip by totem is under way: the warp it ends with is not a walk.</summary>
    public static bool InProgress { get => inProgress.Value; private set => inProgress.Value = value; }

    // Per screen: in split-screen one player's trip is not the other's.
    private static readonly PerScreen<bool> inProgress = new();

    public static void Start(Farm destination, Multiplayer multiplayer)
    {
        var who = Game1.player;
        var here = who.currentLocation;
        if (here is null) return;
        string target = destination.Name;
        InProgress = true;

        who.jitterStrength = 1f;
        here.playSound("warrior");
        who.faceDirection(2);
        who.CanMove = false;
        who.temporarilyInvincible = true;
        who.temporaryInvincibilityTimer = -4000;
        Game1.changeMusicTrack("silence");
        who.FarmerSprite.animateOnce(new[]
        {
            new FarmerSprite.AnimationFrame(57, 2000, secondaryArm: false, flip: false),
            new FarmerSprite.AnimationFrame((short)who.FarmerSprite.CurrentFrame, 0, secondaryArm: false, flip: false, _ => Vanish(target, multiplayer), behaviorAtEndOfFrame: true),
        });
        Rise(here, multiplayer, who.Position + new Vector2(0f, -96f), 1f, 0.01f, 1f, 0, 1f);
        Rise(here, multiplayer, who.Position + new Vector2(-64f, -96f), 0.5f, 0.005f, 0.5f, 10, 0.9999f);
        Rise(here, multiplayer, who.Position + new Vector2(64f, -96f), 0.5f, 0.005f, 0.5f, 20, 0.9988f);
        Game1.screenGlowOnce(Glow, hold: false);
        Utility.addSprinklesToLocation(here, who.TilePoint.X, who.TilePoint.Y, 16, 16, 1300, 20, Color.White, null, motionTowardCenter: true);
    }

    private static void Rise(GameLocation here, Multiplayer multiplayer, Vector2 from, float speed, float grow, float scale, int delay, float depth)
    {
        var sprite = new TemporaryAnimatedSprite(0, 9999f, 1, 999, from, flicker: false, flipped: false, verticalFlipped: false, 0f)
        {
            motion = new Vector2(0f, -speed),
            scaleChange = grow,
            scale = scale,
            alpha = 1f,
            alphaFade = 0.0075f,
            shakeIntensity = 1f,
            delayBeforeAnimationStart = delay,
            initialPosition = from,
            xPeriodic = true,
            xPeriodicLoopTime = 1000f,
            xPeriodicRange = 4f,
            layerDepth = depth,
        };
        sprite.CopyAppearanceFromItemId(Keys.TotemQualifiedId);
        multiplayer.broadcastSprites(here, sprite);
    }

    private static void Vanish(string target, Multiplayer multiplayer)
    {
        var who = Game1.player;
        var here = who.currentLocation;
        for (int i = 0; i < 12; i++)
        {
            multiplayer.broadcastSprites(here, new TemporaryAnimatedSprite(354, Game1.random.Next(25, 75), 6, 1,
                new Vector2(Game1.random.Next((int)who.Position.X - 256, (int)who.Position.X + 192), Game1.random.Next((int)who.Position.Y - 256, (int)who.Position.Y + 192)),
                flicker: false, Game1.random.NextBool()));
        }
        who.playNearbySoundAll("wand");
        Game1.displayFarmer = false;
        who.temporarilyInvincible = true;
        who.temporaryInvincibilityTimer = -2000;
        who.freezePause = 1000;
        Game1.flashAlpha = 1f;
        DelayedAction.fadeAfterDelay(() => Arrive(target), 1000);
    }

    private static void Arrive(string target)
    {
        // Where to stand is decided on arrival: a totem may have been moved while the farmer was on the way.
        // A farm that has gone in the meantime leaves the farmer where they stand.
        if (Game1.getLocationFromName(target) is Farm farm)
        {
            Point tile = Farms.ArrivalTile(farm, Game1.player);
            Game1.warpFarmer(target, tile.X, tile.Y, flip: false);
        }
        Game1.fadeToBlackAlpha = 0.99f;
        Game1.screenGlow = false;
        Game1.player.temporarilyInvincible = false;
        Game1.player.temporaryInvincibilityTimer = 0;
        Game1.displayFarmer = true;
        // The warp above has been queued; the arrival is dealt with before the next tick's events.
        DelayedAction.functionAfterDelay(() => InProgress = false, 100);
    }
}

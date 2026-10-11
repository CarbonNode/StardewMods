using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace ManyFarms.Mod;

/// <summary>
/// A storm over the valley is over every farm. The game rolls its lightning for its own farm
/// only, so a lightning rod anywhere else would never charge. Each started plot gets the same
/// roll the game makes (Utility.performLightningUpdate in 1.6.15), with its own dice: a rod
/// takes the strike and makes a battery, and without a free rod a tree or a crop may be hit.
/// The flash and the thunder are the storm's and come from the game's own strike; a plot's
/// strike adds only the bolt, for whoever is standing there.
/// </summary>
internal static class Lightning
{
    /// <returns>True when a rod on this farm took a strike.</returns>
    public static bool Strike(Farm farm, int timeOfDay, int slot)
    {
        var random = Utility.CreateRandom(Game1.uniqueIDForThisGame, Game1.stats.DaysPlayed, timeOfDay, slot * 7919);
        double luck = Game1.player.team.AverageDailyLuck() + Game1.player.team.AverageLuckLevel() / 100.0;
        if (random.NextDouble() >= 0.125 + luck) return false;

        var strike = new Farm.LightningStrikeEvent();
        var rods = farm.objects.Pairs.Where(pair => pair.Value.QualifiedItemId == "(BC)9").Select(pair => pair.Key).ToList();
        if (rods.Count > 0)
        {
            for (int i = 0; i < 2; i++)
            {
                Vector2 tile = random.ChooseFrom(rods);
                var rod = farm.objects[tile];
                if (rod.heldObject.Value is not null) continue;
                rod.heldObject.Value = ItemRegistry.Create<SObject>("(O)787");
                rod.minutesUntilReady.Value = Utility.CalculateMinutesUntilMorning(Game1.timeOfDay);
                rod.shakeTimer = 1000;
                strike.createBolt = true;
                strike.boltPosition = tile * 64f + new Vector2(32f, 0f);
                farm.lightningStrikeEvent.Fire(strike);
                return true;
            }
        }
        if (random.NextDouble() < 0.25 - luck)
        {
            try
            {
                if (Utility.TryGetRandom(farm.terrainFeatures, out var key, out var feature))
                {
                    if (feature is FruitTree tree)
                    {
                        tree.struckByLightningCountdown.Value = 4;
                        tree.shake(key, doEvenIfStillShaking: true);
                        strike.createBolt = true;
                        strike.boltPosition = key * 64f + new Vector2(32f, -128f);
                    }
                    else
                    {
                        var crop = (feature as HoeDirt)?.crop;
                        bool alive = crop is not null && !crop.dead.Value;
                        if (feature.performToolAction(null, 50, key))
                        {
                            strike.destroyedTerrainFeature = true;
                            strike.createBolt = true;
                            farm.terrainFeatures.Remove(key);
                            strike.boltPosition = key * 64f + new Vector2(32f, -128f);
                        }
                        if (alive && crop!.dead.Value)
                        {
                            strike.createBolt = true;
                            strike.boltPosition = key * 64f + new Vector2(32f, 0f);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // As in the game: a terrain feature that cannot take a strike is not struck.
            }
        }
        if (strike.createBolt) farm.lightningStrikeEvent.Fire(strike);
        return false;
    }

    /// <summary>The game draws a bolt only on a location named Farm. On a plot, whoever stands there sees theirs.</summary>
    public static void ShowBolt(Farm farm, Farm.LightningStrikeEvent strike)
    {
        if (!strike.createBolt || Game1.currentLocation != farm) return;
        if (strike.destroyedTerrainFeature)
            farm.temporarySprites.Add(new TemporaryAnimatedSprite(362, 75f, 6, 1, strike.boltPosition, flicker: false, flipped: false));
        Utility.drawLightningBolt(strike.boltPosition, farm);
    }
}

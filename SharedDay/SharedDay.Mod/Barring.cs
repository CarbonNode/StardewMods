using System.Globalization;
using System.Text.Json;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace SharedDay.Mod;

/// <summary>
/// Farmers the group has removed from the farm (0.1.29), as the turn tracker orders it
/// (docs/turn-tracker-contract.md, "Responses") or the host's console does. A barred farmer sits
/// today out, is left off every later roster, is disconnected on sight, and once they are offline
/// their farmhand is deleted the game's own way so the cabin is free: the launcher promised "They
/// lose the group and their farmer on its farm". Kept in the host's save (MasterPlayer.modData).
/// </summary>
internal static class Barring
{
    internal const string Key = "CarbonNode.SharedDay/Barred";
    internal const string NewFarmersKey = "CarbonNode.SharedDay/NewFarmers";
    internal const string Reason = "You were removed from this farm's group.";

    internal static HashSet<long> Read()
    {
        if (!Game1.MasterPlayer.modData.TryGetValue(Key, out string json) || string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(json) ?? new())
                .Select(raw => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : (long?)null)
                .Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        }
        catch (JsonException) { return new(); }
    }

    internal static void Write(HashSet<long> barred)
        => Game1.MasterPlayer.modData[Key] = JsonSerializer.Serialize(barred.OrderBy(id => id).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList());

    /// <summary>Whether new farmers may claim a cabin, as last ordered; true when never ordered.</summary>
    internal static bool ReadNewFarmers()
        => !Game1.MasterPlayer.modData.TryGetValue(NewFarmersKey, out string raw) || raw != "false";

    internal static void WriteNewFarmers(bool allowed) => Game1.MasterPlayer.modData[NewFarmersKey] = allowed ? "true" : "false";

    /// <summary>
    /// Deletes an offline farmhand the game's own way, so their cabin is free for somebody else.
    /// The 1.6 game has FarmerTeam.DeleteFarmhand(Farmer) (the host's side of the Co-op menu's
    /// delete; it freed a cabin in the `removed` lab on 1.6.15, 2026-10-08) and Cabin.DeleteFarmhand();
    /// both are looked up by name so that a game without either costs only this one thing.
    /// </summary>
    /// <returns>Null on success, or why not.</returns>
    internal static string? FreeCabin(Farmer farmer, IMonitor monitor)
    {
        if (farmer.IsMainPlayer) return "the host's own farmer has no cabin to free";
        if (farmer.isUnclaimedFarmhand) return "that cabin is already free";
        if (Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == farmer.UniqueMultiplayerID)) return "they are still on the farm";
        try
        {
            var byTeam = AccessTools.Method(typeof(FarmerTeam), "DeleteFarmhand", new[] { typeof(Farmer) });
            if (byTeam is not null)
            {
                byTeam.Invoke(Game1.player.team, new object[] { farmer });
                monitor.Log($"Deleted {farmer.Name}'s farmhand (FarmerTeam.DeleteFarmhand); their cabin is free.", LogLevel.Info);
                return null;
            }
            var cabin = Game1.getFarm().buildings.Select(building => building.GetIndoors()).OfType<Cabin>()
                .FirstOrDefault(c => c.owner?.UniqueMultiplayerID == farmer.UniqueMultiplayerID);
            var byCabin = AccessTools.Method(typeof(Cabin), "DeleteFarmhand", Type.EmptyTypes);
            if (cabin is not null && byCabin is not null)
            {
                byCabin.Invoke(cabin, null);
                monitor.Log($"Deleted {farmer.Name}'s farmhand (Cabin.DeleteFarmhand); their cabin is free.", LogLevel.Info);
                return null;
            }
            return cabin is null ? "no cabin on the farm belongs to them" : "this game has no farmhand deletion Shared Day knows";
        }
        catch (Exception ex)
        {
            return "the game refused: " + (ex.InnerException ?? ex).Message;
        }
    }
}

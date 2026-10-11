using System.Text.Json;
using System.IO.Compression;
using Microsoft.Xna.Framework;
using SharedDay.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;

namespace SharedDay.Mod;

internal sealed class CheckpointEnvelope
{
    public int SchemaVersion { get; set; } = 1;
    public DayJournal Journal { get; set; } = new();
    public CalendarClockState? Clock { get; set; }
    public int TimeOfDay { get; set; }
    public bool IsMidday { get; set; }
}

/// <summary>
/// Stores the journal inside the SAME game save as its world, so completion can never
/// survive a crash independently of the work it describes. No standalone newer journal.
/// </summary>
internal static class WorldCheckpoint
{
    private const string Key = "CarbonNode.SharedDay/Checkpoint";
    private const string ShippingOwner = "CarbonNode.SharedDay/ShippingOwner";

    internal static void EnsureBackup(string modDirectory)
    {
        string source = Constants.CurrentSavePath ?? throw new InvalidOperationException("No save directory is loaded.");
        string directory = Path.Combine(modDirectory, "backups", Constants.SaveFolderName!);
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, $"day-{Game1.Date.TotalDays}.zip");
        if (File.Exists(destination)) return;
        string temporary = destination + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        ZipFile.CreateFromDirectory(source, temporary);
        File.Move(temporary, destination);
        PruneBackups(directory);
    }

    // One copy a day for ever fills the disk of a farm that runs for years; a modded farm's save
    // is tens of megabytes. Two weeks of days is what anyone goes back for.
    private const int BackupsKept = 14;

    private static void PruneBackups(string directory)
    {
        try
        {
            var old = Directory.GetFiles(directory, "day-*.zip")
                .Select(path => (path, day: int.TryParse(Path.GetFileNameWithoutExtension(path)[4..], out int day) ? day : -1))
                .Where(entry => entry.day >= 0).OrderByDescending(entry => entry.day).Skip(BackupsKept);
            foreach (var (path, _) in old) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static CheckpointEnvelope? Read()
    {
        if (!Game1.MasterPlayer.modData.TryGetValue(Key, out string value)) return null;
        var data = JsonSerializer.Deserialize<CheckpointEnvelope>(value)
            ?? throw new InvalidDataException("The checkpoint is empty.");
        if (data.SchemaVersion != 1) throw new InvalidDataException("Unsupported checkpoint schema.");
        if (data.TimeOfDay < 600 || data.TimeOfDay > 2600 || data.TimeOfDay % 100 >= 60)
            throw new InvalidDataException("Invalid checkpoint clock.");
        return data;
    }

    internal static void Embed(DayJournal journal, CalendarClockState clock, bool midday) => Game1.MasterPlayer.modData[Key] = JsonSerializer.Serialize(new CheckpointEnvelope
    {
        Journal = journal,
        Clock = clock,
        TimeOfDay = Game1.timeOfDay,
        IsMidday = midday
    });

    /// <param name="remoteHeld">
    /// "Save my turn" (0.1.29): answers true for a connected remote farmer who has frozen for this
    /// save and sent their state, which is the one case a connected farmhand may be written. Null,
    /// or any remote farmer it does not vouch for, keeps the rule that the farm is saved mid-day
    /// only with no remote farmers.
    /// </param>
    internal static void Save(IModHelper helper, IMonitor monitor, DayJournal journal, CalendarClockState clock, Func<long, bool>? remoteHeld = null)
    {
        if (!Context.IsMainPlayer || Game1.gameMode != 3
            || Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID && !(remoteHeld?.Invoke(f.UniqueMultiplayerID) ?? false)))
            throw new InvalidOperationException("A same-day checkpoint requires a loaded host with no remote farmers, or every remote farmer held still for it.");
        // A backup that cannot be made (a full disk) is no reason to lose the save itself.
        try { EnsureBackup(helper.DirectoryPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { monitor.LogOnce("Shared Day could not make today's backup copy of the farm. " + ex.Message, LogLevel.Warn); }
        BindOfflineWallets();
        JunimoCheckpointBridge.CaptureForCheckpoint(helper);
        // Every other mod prepares for this save as it does for the night's (see SaveEvents).
        SaveEvents.RaiseSaving(monitor);

        // Vanilla serializes the host's shipping bin. Separate-wallet farmhands' bins are
        // XmlIgnore, so preserve them as temporary, tagged objects INSIDE this same save.
        var temporary = new List<Vector2>();
        Action putBack = () => { };
        try
        {
            // What the game cannot write is lifted out for the save, as its own mod does at night.
            putBack = UnsavableObjects.Lift(monitor);
            if (Game1.player.team.useSeparateWallets.Value)
            {
                int index = 0;
                foreach (var farmer in Game1.getAllFarmers().Where(f => f.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID))
                {
                    var tile = new Vector2(-10000 - index++, -10000);
                    if (Game1.getFarm().objects.ContainsKey(tile)) throw new InvalidOperationException("Checkpoint storage tile is occupied.");
                    var chest = new Chest(true);
                    chest.modData[ShippingOwner] = farmer.UniqueMultiplayerID.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    chest.modData["Pathoschild.ChestsAnywhere/IsIgnored"] = "true";
                    foreach (var item in Game1.getFarm().getShippingBin(farmer))
                    {
                        // Avoid attaching one item's NetFields to two collections at once.
                        var copy = item.getOne();
                        copy.Stack = item.Stack;
                        chest.Items.Add(copy);
                    }
                    Game1.getFarm().objects.Add(tile, chest);
                    temporary.Add(tile);
                }
            }
            Embed(journal, clock, true);
            helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue().saveFarmhands();
            // No newDay/ShippingMenu invocation: drive only the serializer on the game thread.
            using var save = SaveGame.getSaveEnumerator();
            while (save.MoveNext()) { }
        }
        finally
        {
            // Keep escrow objects only in the checkpoint bytes, never the running farm.
            foreach (var tile in temporary) Game1.getFarm().objects.Remove(tile);
            putBack();
            // Whether or not the save was written, the other mods put back what they took out.
            SaveEvents.RaiseSaved(monitor);
        }
    }

    internal static void RestoreShipping()
    {
        var farm = Game1.getFarm();
        foreach (var entry in farm.objects.Pairs.ToArray())
        {
            if (entry.Value is not Chest chest || !chest.modData.TryGetValue(ShippingOwner, out string raw)) continue;
            if (!long.TryParse(raw, out long id)) throw new InvalidDataException("Invalid shipping owner in checkpoint.");
            var farmer = Game1.getAllFarmers().SingleOrDefault(f => f.UniqueMultiplayerID == id)
                ?? throw new InvalidDataException("Checkpoint shipping owner is missing from this farm.");
            var bin = farm.getShippingBin(farmer);
            bin.Clear();
            foreach (var item in chest.Items) if (item is not null) bin.Add(item);
            farm.objects.Remove(entry.Key);
        }
    }

    internal static void BindOfflineWallets()
    {
        if (!Context.IsMainPlayer || !Game1.player.team.useSeparateWallets.Value) return;
        var team = Game1.player.team;
        var online = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();
        foreach (var farmer in Game1.getAllFarmers().Where(f => !online.Contains(f.UniqueMultiplayerID)))
        {
            // Vanilla load populates the host's individualMoney dictionary, but only
            // connecting a farmer links their teamRoot. Offline shipping credits the
            // host dictionary while the XML money getter reads the farmer's own root.
            // Link them as native addPlayer/Clone do, so the next save keeps that payout.
            // A newly created offline identity may not yet have a canonical balance.
            if (!team.individualMoney.ContainsKey(farmer.UniqueMultiplayerID))
                team.SetIndividualMoney(farmer, farmer.Money);
            farmer.teamRoot = Game1.player.teamRoot;
        }
    }
}

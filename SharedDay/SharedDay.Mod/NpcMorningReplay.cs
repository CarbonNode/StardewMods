using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Pathfinding;

namespace SharedDay.Mod;

/// <summary>
/// Replays an observed morning's movement, never NPC.dayUpdate/resetForNewDay. Those
/// methods change marriage/daily progress. Routes are deep copied because the engine
/// consumes their stacks while walking. No farmer, friendship, gift, quest, dialogue,
/// invisibility, or marriage-progress field is restored from the morning.
/// </summary>
internal static class NpcMorningReplay
{
    private const string Key = "CarbonNode.SharedDay/NpcMorning";

    /// <summary>Check before enabling Relay on a loaded midday checkpoint.</summary>
    internal static bool HasCurrentMorning => Read() is { } snapshot && snapshot.Day == Game1.Date.TotalDays;

    // Movement/route bookkeeping verified against the installed 1.6.15.24356 assembly.
    // Fail before changing any NPC if a later game version changes these members.
    private static readonly string[] MovementFieldNames =
    {
        "lastAttemptedSchedule", "currentScheduleDelay", "scheduleDelaySeconds",
        "ignoreScheduleToday", "followSchedule", "isWalkingInSquare", "returningToEndPoint",
        "isPlayingSleepingAnimation", "currentlyDoingEndOfRouteAnimation", "_skipRouteEndIntro",
        "_startedEndOfRouteBehavior", "_finishingEndOfRouteBehavior", "loadedEndOfRouteBehavior",
        "_beforeEndOfRouteAnimationFrame", "routeEndIntro", "routeEndAnimation", "routeEndOutro",
        "nextEndOfRouteMessage"
    };

    private static readonly string[] OutroSpriteFields =
    {
        "timer", "interval", "framesPerAnimation", "tempSpriteHeight", "loop",
        "ignoreStopAnimation", "textureUsesFlippedRightForLeft", "oldFrame",
        "currentAnimationIndex", "ignoreSourceRectUpdates"
    };
    private static readonly string[] OutroNpcFields = { "freezeMotion", "isCharging" };
    private static readonly string[] OutroFrameFields = { "frame", "milliseconds", "positionOffset", "xOffset", "armOffset", "flip" };

    /// <summary>
    /// Call at a real morning (SaveLoaded for a fresh day, then DayStarted), before play.
    /// A same-date checkpoint retains its original morning rather than capturing a late
    /// save's reload positions as if they were the morning. Store travels with game save.
    /// </summary>
    /// <returns>Villagers whose 6 AM state could not be captured exactly; they are reset the
    /// game's way on replay (morning spot and schedule) instead of holding the farm.</returns>
    internal static IReadOnlyList<string> EnsureMorningBaseline()
    {
        RequireHost();
        // Stored data from another day that no longer reads (another farm's, an older shape) is
        // simply replaced by this morning's.
        NpcMorningEnvelope? existing;
        try { existing = Read(); }
        catch (InvalidDataException) when (Game1.timeOfDay == 600 && !Game1.eventUp) { existing = null; }
        catch (System.Text.Json.JsonException) when (Game1.timeOfDay == 600 && !Game1.eventUp) { existing = null; }
        if (existing is not null && existing.Day == Game1.Date.TotalDays) return Array.Empty<string>();
        if (Game1.timeOfDay != 600 || Game1.eventUp)
            throw new InvalidOperationException("NPC replay needs an observed 6 AM morning. Enable Shared Day at the start of a real day, outside an event.");

        var fields = ResolveMovementFields();
        var frames = new List<NpcMorningFrame>();
        var degraded = new List<string>();
        foreach (var npc in GetUniqueVillagers())
        {
            // A custom controller or an animation already under way can hold callbacks that
            // cannot be stored. Such a villager (vanilla or modded) is not exactly replayable;
            // replay resets them the game's way instead of holding the whole farm.
            string? inexact = npc.controller is not null || npc.temporaryController is not null
                || npc.queuedSchedulePaths.Count > 0
                || npc.doingEndOfRouteAnimation.Value
                || (bool)fields.Single(f => f.Name == "currentlyDoingEndOfRouteAnimation").GetValue(npc)!
                || fields.Single(f => f.Name == "_startedEndOfRouteBehavior").GetValue(npc) is not null
                || fields.Single(f => f.Name == "_finishingEndOfRouteBehavior").GetValue(npc) is not null
                ? "a movement or animation already under way at 6 AM" : null;
            NpcRouteOutro? outro = null;
            if (inexact is null)
            {
                try { outro = CaptureRouteOutro(npc); }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or MissingMemberException)
                {
                    inexact = "a morning animation Shared Day cannot store";
                }
            }
            var movement = fields.ToDictionary(f => f.Name, f => JsonSerializer.SerializeToElement(f.GetValue(npc), f.FieldType));
            if (inexact is not null)
            {
                degraded.Add(npc.Name);
                ClearActiveMovement(movement, fields);
            }
            // In 1.6.15 getRouteEndBehaviorFunction sets the 'going' flag when it
            // prepares a route; doAnimationAtEndOfScheduleRoute starts the animation
            // separately. Vanilla can retain only this flag overnight (Willy, spring
            // 3 rain), with no controller, frames or active behavior. Preserve that
            // pending data instead of rejecting it or invoking an animation callback.
            frames.Add(new NpcMorningFrame
            {
                Name = npc.Name,
                TypeName = npc.GetType().FullName!,
                Location = npc.currentLocation?.NameOrUniqueName
                    ?? throw new InvalidOperationException($"{npc.Name} has no morning location."),
                X = npc.Position.X, Y = npc.Position.Y,
                Facing = npc.FacingDirection,
                Frame = npc.Sprite.CurrentFrame,
                IsSleeping = npc.isSleeping.Value,
                LayingDown = npc.layingDown,
                GoingToDoEndOfRouteAnimation = npc.goingToDoEndOfRouteAnimation.Value,
                EndOfRouteBehaviorName = npc.endOfRouteBehaviorName.Value,
                EndOfRouteMessage = npc.endOfRouteMessage.Value,
                RouteAnimationMetadata = inexact is null ? npc.routeAnimationMetadata?.ToArray() : null,
                PendingRouteOutro = outro,
                Inexact = inexact,
                ScheduleKey = npc.ScheduleKey,
                HasSchedule = npc.Schedule is not null,
                Routes = npc.Schedule?.Select(p => ScheduleRouteFrame.Capture(p.Key, p.Value)).ToList() ?? new(),
                Movement = movement
            });
        }
        Game1.MasterPlayer.modData[Key] = JsonSerializer.Serialize(new NpcMorningEnvelope
        {
            Day = Game1.Date.TotalDays,
            SaveId = Constants.SaveFolderName!,
            Villagers = frames
        });
        return degraded;
    }

    // In-progress route state that only makes sense with the callbacks we could not store.
    private static readonly string[] ActiveMovementFieldNames =
    {
        "currentlyDoingEndOfRouteAnimation", "_startedEndOfRouteBehavior", "_finishingEndOfRouteBehavior",
        "isWalkingInSquare", "returningToEndPoint", "routeEndIntro", "routeEndAnimation", "routeEndOutro",
        "nextEndOfRouteMessage", "loadedEndOfRouteBehavior"
    };

    private static void ClearActiveMovement(Dictionary<string, JsonElement> movement, FieldInfo[] fields)
    {
        foreach (var field in fields.Where(f => ActiveMovementFieldNames.Contains(f.Name)))
            movement[field.Name] = JsonSerializer.SerializeToElement(
                field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null, field.FieldType);
    }

    /// <summary>Call after setting the host clock to 600, before admitting the next cohort.</summary>
    /// <returns>Villagers reset the game's way rather than replayed exactly.</returns>
    internal static IReadOnlyList<string> RestoreMorning()
    {
        RequireHost();
        if (Game1.timeOfDay != 600 || Game1.eventUp)
            throw new InvalidOperationException("NPC morning replay requires 6 AM outside an event.");
        var baseline = Read() ?? throw new InvalidOperationException("This farm has no observed NPC morning to replay.");
        if (baseline.Day != Game1.Date.TotalDays)
            throw new InvalidOperationException("NPC morning belongs to a different date.");
        var fields = ResolveMovementFields();
        var villagers = GetUniqueVillagers().ToDictionary(n => n.Name, StringComparer.Ordinal);
        var plans = new List<(NPC Npc, NpcMorningFrame Frame, Dictionary<int, SchedulePathDescription> Schedule, object?[] Values, RouteOutroPlan? Outro)>();
        var reset = baseline.Villagers.Where(f => f.Inexact is not null).Select(f => f.Name).ToList();

        // Resolve every location, type, route and field before the first world mutation.
        foreach (var frame in baseline.Villagers)
        {
            // An NPC removed by a quest/event stays removed. Never resurrect morning's
            // character collection or remove an NPC introduced by today's progress.
            if (!villagers.TryGetValue(frame.Name, out var npc)) continue;
            // A villager a mod replaced or moved somewhere that no longer exists keeps their
            // current state; the rest of the village still replays.
            if (npc.GetType().FullName != frame.TypeName || Game1.getLocationFromName(frame.Location) is null)
            {
                reset.Add(frame.Name + " (left as is)");
                continue;
            }
            if (!float.IsFinite(frame.X) || !float.IsFinite(frame.Y))
                throw new InvalidDataException($"Invalid morning destination for {frame.Name}.");
            if (frame.Facing is < 0 or > 3 || frame.Frame < 0)
                throw new InvalidDataException($"Invalid morning pose for {frame.Name}.");
            var values = fields.Select(f => frame.Movement.TryGetValue(f.Name, out var value)
                ? value.Deserialize(f.FieldType)
                : throw new InvalidDataException($"Missing NPC movement field {f.Name}.")).ToArray();
            var schedule = frame.Routes.ToDictionary(r => r.At, r => r.Restore());
            RouteOutroPlan? outro = null;
            if (frame.PendingRouteOutro is not null)
            {
                try { outro = PrepareRouteOutro(npc, frame); }
                catch (Exception ex) when (ex is InvalidOperationException or MissingMemberException)
                {
                    // The stored animation no longer fits this villager (another sprite, other
                    // timing): start them at their morning spot without it.
                    reset.Add(frame.Name);
                }
            }
            plans.Add((npc, frame, schedule, values, outro));
        }

        foreach (var (npc, frame, schedule, values, outro) in plans)
        {
            // Clear animation directly: route-end callbacks can carry game effects and
            // must not run just because another cohort is starting.
            npc.Sprite.CurrentAnimation = null;
            npc.Halt();
            npc.controller = null;
            npc.temporaryController = null;
            npc.queuedSchedulePaths.Clear();
            AccessTools.Field(typeof(NPC), "directionsToNewLocation").SetValue(npc, null);
            npc.doingEndOfRouteAnimation.Value = false;
            npc.goingToDoEndOfRouteAnimation.Value = false;
            npc.isMovingOnPathFindPath.Value = false;
            npc.endOfRouteBehaviorName.Value = null;
            npc.endOfRouteMessage.Value = null;
            npc.IsWalkingTowardPlayer = false;
            npc.faceTowardFarmer = false;
            npc.faceTowardFarmerTimer = 0;
            npc.drawOffset = Vector2.Zero;
            npc.appliedRouteAnimationOffset = Vector2.Zero;
            npc.rotation = npc.yOffset = npc.swimTimer = npc.timerSinceLastMovement = 0;
            npc.layingDown = frame.LayingDown;
            npc.isSleeping.Value = frame.IsSleeping;

            Game1.warpCharacter(npc, frame.Location, new Vector2(frame.X / 64f, frame.Y / 64f));
            npc.lastPosition = npc.Position;
            npc.previousEndPoint = npc.TilePoint;
            npc.lastCrossroad = Rectangle.Empty;
            npc.faceDirection(frame.Facing);
            npc.Sprite.CurrentFrame = frame.Frame;
            npc.Sprite.UpdateSourceRect();
            if (frame.HasSchedule) npc.TryLoadSchedule(frame.ScheduleKey, schedule);
            else npc.ClearSchedule();
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(npc, values[i]);
            npc.routeAnimationMetadata = frame.RouteAnimationMetadata?.ToArray();
            npc.endOfRouteBehaviorName.Value = frame.EndOfRouteBehaviorName;
            npc.endOfRouteMessage.Value = frame.EndOfRouteMessage;
            npc.goingToDoEndOfRouteAnimation.Value = frame.GoingToDoEndOfRouteAnimation;
            outro?.Apply(npc);
        }
        return reset;
    }

    /// <summary>
    /// Vanilla can finish yesterday's route during new-day setup, leaving its final
    /// one or two frames for the next normal update (Jodi, Pam and Maru in 1.6.15).
    /// Only that exact inactive native outro is durable here. Its final callback
    /// resets movement/sprite state; finishEndOfRouteAnimation itself has wider game
    /// effects and is deliberately never called by capture or replay.
    /// </summary>
    private static NpcRouteOutro? CaptureRouteOutro(NPC npc)
    {
        if (npc.Sprite.CurrentAnimation is not { Count: > 0 } animation) return null;
        RequireNativeOutro(npc);
        var callback = NativeOutroCallback();
        for (int i = 0; i < animation.Count; i++)
        {
            object frame = animation[i];
            var start = FrameField("frameStartBehavior").GetValue(frame);
            var end = FrameField("frameEndBehavior").GetValue(frame) as Delegate;
            if (start is not null || (i < animation.Count - 1 ? end is not null
                : end is null || end.GetInvocationList().Length != 1 || end.Method != callback || !ReferenceEquals(end.Target, npc)))
                throw UnsupportedOutro(npc);
        }
        var result = new NpcRouteOutro
        {
            Frames = animation.Select(frame => CapturePrimitives(frame, typeof(FarmerSprite.AnimationFrame), OutroFrameFields)).ToList(),
            Sprite = CapturePrimitives(npc.Sprite, typeof(AnimatedSprite), OutroSpriteFields),
            Npc = CapturePrimitives(npc, typeof(NPC), OutroNpcFields),
            Speed = npc.speed, AddedSpeed = npc.addedSpeed,
            Width = npc.Sprite.SpriteWidth, Height = npc.Sprite.SpriteHeight,
            Texture = (string?)SpriteField("loadedTexture").GetValue(npc.Sprite),
            TextureOverride = (string?)SpriteField("overrideTextureName").GetValue(npc.Sprite),
            DrawX = npc.drawOffset.X, DrawY = npc.drawOffset.Y,
            AppliedX = npc.appliedRouteAnimationOffset.X, AppliedY = npc.appliedRouteAnimationOffset.Y
        };
        // Use the same validation as persisted replay before accepting this morning.
        PrepareRouteOutro(npc, new NpcMorningFrame
        {
            Name = npc.Name, Frame = npc.Sprite.CurrentFrame, PendingRouteOutro = result,
            Movement = ResolveMovementFields().ToDictionary(f => f.Name, f => JsonSerializer.SerializeToElement(f.GetValue(npc), f.FieldType))
        });
        return result;
    }

    private static RouteOutroPlan PrepareRouteOutro(NPC npc, NpcMorningFrame morning)
    {
        var saved = morning.PendingRouteOutro!;
        // Today's NPC may be moving now; validate the stored morning's inactive
        // state, never require that the current cohort left it idle.
        if (npc.GetType() != typeof(NPC) || npc.Sprite.GetType() != typeof(AnimatedSprite)
            || saved.Frames is not { Count: > 0 and <= 256 } || saved.Sprite is null || saved.Npc is null
            || saved.Width is <= 0 or > 256 || saved.Height is <= 0 or > 256
            || !float.IsFinite(saved.DrawX) || !float.IsFinite(saved.DrawY)
            || !float.IsFinite(saved.AppliedX) || !float.IsFinite(saved.AppliedY)
            || !float.IsFinite(saved.AddedSpeed)
            || !SameAsset(saved.Texture, (string?)SpriteField("loadedTexture").GetValue(npc.Sprite))
            || !SameAsset(saved.TextureOverride, (string?)SpriteField("overrideTextureName").GetValue(npc.Sprite))
            || morning.GoingToDoEndOfRouteAnimation
            || morning.Movement["isWalkingInSquare"].GetBoolean()
            || morning.Movement["currentlyDoingEndOfRouteAnimation"].GetBoolean()
            || morning.Movement["_startedEndOfRouteBehavior"].ValueKind != JsonValueKind.Null
            || morning.Movement["_finishingEndOfRouteBehavior"].ValueKind != JsonValueKind.Null)
            throw UnsupportedOutro(npc);

        var spriteFields = ResolvePrimitiveFields(typeof(AnimatedSprite), OutroSpriteFields);
        var npcFields = ResolvePrimitiveFields(typeof(NPC), OutroNpcFields);
        var spriteValues = ReadPrimitives(saved.Sprite, spriteFields);
        var npcValues = ReadPrimitives(saved.Npc, npcFields);
        int index = saved.Sprite["currentAnimationIndex"].GetInt32();
        double timer = saved.Sprite["timer"].GetDouble();
        var route = morning.Movement["routeEndOutro"].Deserialize<int[]>();
        if (route is null || route.Length != saved.Frames.Count || index < 0 || index >= route.Length
            || morning.Frame != route[index] || !double.IsFinite(timer) || timer < 0 || timer > 100
            || saved.Sprite["oldFrame"].GetInt32() < 0
            || saved.Sprite["ignoreStopAnimation"].GetBoolean()
            || saved.Sprite["ignoreSourceRectUpdates"].GetBoolean())
            throw UnsupportedOutro(npc);

        var callback = NativeOutroCallback().CreateDelegate(typeof(AnimatedSprite.endOfAnimationBehavior), npc);
        var constructor = AccessTools.Constructor(typeof(FarmerSprite.AnimationFrame), new[]
        {
            typeof(int), typeof(int), typeof(int), typeof(bool), typeof(bool),
            typeof(AnimatedSprite.endOfAnimationBehavior), typeof(bool), typeof(int)
        }) ?? throw new MissingMethodException("The native route-outro animation constructor changed.");
        var frameFields = ResolvePrimitiveFields(typeof(FarmerSprite.AnimationFrame), OutroFrameFields);
        var frames = new List<FarmerSprite.AnimationFrame>();
        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] < 0 || saved.Frames[i] is null) throw UnsupportedOutro(npc);
            // Match the engine's construction exactly. Arbitrary frame timings,
            // offsets and delegates are not accepted through the journal.
            var frame = (FarmerSprite.AnimationFrame)constructor.Invoke(new object?[]
                { route[i], 100, 0, false, false, i == route.Length - 1 ? callback : null, true, 0 });
            var expected = ReadPrimitives(saved.Frames[i], frameFields);
            if (frameFields.Where((field, n) => !Equals(field.GetValue(frame), expected[n])).Any())
                throw UnsupportedOutro(npc);
            frames.Add(frame);
        }
        // Resolve every field/callback before RestoreMorning mutates any villager.
        _ = SpriteField("endOfAnimationFunction");
        return new RouteOutroPlan(saved, frames, spriteFields, spriteValues, npcFields, npcValues);
    }

    private static void RequireNativeOutro(NPC npc)
    {
        if (npc.GetType() != typeof(NPC) || npc.Sprite.GetType() != typeof(AnimatedSprite)
            || npc.goingToDoEndOfRouteAnimation.Value
            || (bool)AccessTools.Field(typeof(NPC), "isWalkingInSquare").GetValue(npc)!
            || SpriteField("endOfAnimationFunction").GetValue(npc.Sprite) is not null)
            throw UnsupportedOutro(npc);
    }

    // A fresh process names the sheet "Characters/Jodi" where the capturing process
    // had "Characters\Jodi". Compare the asset, never the separator spelling.
    private static bool SameAsset(string? saved, string? current) =>
        saved is null || current is null
            ? saved == current
            : string.Equals(PathUtilities.NormalizeAssetName(saved), PathUtilities.NormalizeAssetName(current), StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException UnsupportedOutro(NPC npc) => new(
        $"Cannot snapshot or replay {npc.Name}'s morning animation. Only the known inactive native route outro is supported.");

    private static MethodInfo NativeOutroCallback() => AccessTools.DeclaredMethod(typeof(NPC), "routeEndAnimationFinished", new[] { typeof(Farmer) })
        ?? throw new MissingMethodException(typeof(NPC).FullName, "routeEndAnimationFinished");
    private static FieldInfo SpriteField(string name) => AccessTools.Field(typeof(AnimatedSprite), name)
        ?? throw new MissingFieldException(typeof(AnimatedSprite).FullName, name);
    private static FieldInfo FrameField(string name) => AccessTools.Field(typeof(FarmerSprite.AnimationFrame), name)
        ?? throw new MissingFieldException(typeof(FarmerSprite.AnimationFrame).FullName, name);

    private static FieldInfo[] ResolvePrimitiveFields(Type type, IEnumerable<string> names) => names.Select(name =>
    {
        var field = AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        if (!field.FieldType.IsPrimitive) throw new InvalidOperationException($"Native animation field {type.Name}.{name} changed type.");
        return field;
    }).ToArray();

    private static Dictionary<string, JsonElement> CapturePrimitives(object instance, Type type, IEnumerable<string> names) =>
        ResolvePrimitiveFields(type, names).ToDictionary(field => field.Name,
            field => JsonSerializer.SerializeToElement(field.GetValue(instance), field.FieldType));

    private static object?[] ReadPrimitives(Dictionary<string, JsonElement> source, FieldInfo[] fields) =>
        fields.Select(field => source.TryGetValue(field.Name, out var value)
            ? value.Deserialize(field.FieldType)
            : throw new InvalidDataException($"Missing native animation field {field.Name}.")).ToArray();

    private sealed record RouteOutroPlan(NpcRouteOutro Saved, List<FarmerSprite.AnimationFrame> Frames,
        FieldInfo[] SpriteFields, object?[] SpriteValues, FieldInfo[] NpcFields, object?[] NpcValues)
    {
        internal void Apply(NPC npc)
        {
            for (int i = 0; i < NpcFields.Length; i++) NpcFields[i].SetValue(npc, NpcValues[i]);
            npc.speed = Saved.Speed;
            npc.addedSpeed = Saved.AddedSpeed;
            npc.drawOffset = new Vector2(Saved.DrawX, Saved.DrawY);
            npc.appliedRouteAnimationOffset = new Vector2(Saved.AppliedX, Saved.AppliedY);
            npc.Sprite.SpriteWidth = Saved.Width;
            npc.Sprite.SpriteHeight = Saved.Height;
            SpriteField("endOfAnimationFunction").SetValue(npc.Sprite, null);
            // The setter only copies the list. Normal native animateOnce updates
            // run the final cleanup callback later, at its preserved timer/index.
            npc.Sprite.CurrentAnimation = Frames;
            for (int i = 0; i < SpriteFields.Length; i++) SpriteFields[i].SetValue(npc.Sprite, SpriteValues[i]);
            npc.Sprite.UpdateSourceRect();
        }
    }

    private static NpcMorningEnvelope? Read()
    {
        if (!Game1.MasterPlayer.modData.TryGetValue(Key, out string data)) return null;
        var snapshot = JsonSerializer.Deserialize<NpcMorningEnvelope>(data)
            ?? throw new InvalidDataException("Empty NPC morning snapshot.");
        if (snapshot.SchemaVersion is not (1 or 2) || snapshot.SaveId != Constants.SaveFolderName || snapshot.Day < 0 || snapshot.Villagers is null)
            throw new InvalidDataException("NPC morning snapshot has an unsupported schema or farm identity.");
        if (snapshot.Day > Game1.Date.TotalDays)
            throw new InvalidDataException("NPC morning snapshot is newer than the loaded farm date. Restore a matching checkpoint.");
        if (snapshot.Villagers.Any(n => n is null || string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrWhiteSpace(n.Location)
                || n.Movement is null || n.Routes is null)
            || snapshot.Villagers.Select(n => n.Name).Distinct(StringComparer.Ordinal).Count() != snapshot.Villagers.Count)
            throw new InvalidDataException("NPC morning snapshot contains invalid or duplicate villagers.");
        if (snapshot.SchemaVersion == 1 && snapshot.Villagers.Any(n => n.PendingRouteOutro is not null))
            throw new InvalidDataException("Native NPC outro data requires morning schema 2.");
        return snapshot;
    }

    private static FieldInfo[] ResolveMovementFields()
    {
        if (AccessTools.Field(typeof(NPC), "directionsToNewLocation") is null)
            throw new MissingFieldException(typeof(NPC).FullName, "directionsToNewLocation");
        return MovementFieldNames.Select(name => AccessTools.Field(typeof(NPC), name)
            ?? throw new MissingFieldException(typeof(NPC).FullName, name)).ToArray();
    }

    private static NPC[] GetUniqueVillagers()
    {
        // The world traversal may reach one live object through more than one location
        // collection. That is one NPC. Vanilla also has two different Mister Qi NPCs
        // (Club and QiNutRoom). Unscheduled, stationary duplicates do not need movement
        // replay: retain both live objects untouched rather than choose one by name.
        var villagers = Utility.getAllCharacters().Where(n => n.IsVillager)
            .Distinct<NPC>(ReferenceEqualityComparer.Instance).ToArray();
        var duplicates = villagers.GroupBy(n => n.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).ToArray();
        // A pack can also put two walking villagers under one name. Which is which cannot be told
        // from a name, so neither is replayed; they stay where the day left them. This used to
        // hold the whole farm.
        var stationaryDuplicateNames = duplicates.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        return villagers.Where(n => !stationaryDuplicateNames.Contains(n.Name)).ToArray();
    }

    private static void RequireHost()
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady)
            throw new InvalidOperationException("Only the loaded host can snapshot or replay NPC movement.");
    }
}

internal sealed class NpcMorningEnvelope
{
    // Schema 2 prevents older builds from silently ignoring preserved callbacks.
    // Existing schema-1 mornings remain readable and are retained on the same date.
    public int SchemaVersion { get; set; } = 2;
    public string SaveId { get; set; } = "";
    public int Day { get; set; }
    public List<NpcMorningFrame> Villagers { get; set; } = new();
}

internal sealed class NpcMorningFrame
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string Location { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public int Facing { get; set; }
    public int Frame { get; set; }
    public bool IsSleeping { get; set; }
    public bool LayingDown { get; set; }
    // Optional schema-1 additions. Older baselines rejected pending animations,
    // so their absent values retain the original false/null replay behavior.
    public bool GoingToDoEndOfRouteAnimation { get; set; }
    public string? EndOfRouteBehaviorName { get; set; }
    public string? EndOfRouteMessage { get; set; }
    public string[]? RouteAnimationMetadata { get; set; }
    public NpcRouteOutro? PendingRouteOutro { get; set; }
    /// <summary>Why this villager's 6 AM state was not stored exactly; replay resets them instead.</summary>
    public string? Inexact { get; set; }
    public string? ScheduleKey { get; set; }
    public bool HasSchedule { get; set; }
    public List<ScheduleRouteFrame> Routes { get; set; } = new();
    public Dictionary<string, JsonElement> Movement { get; set; } = new();
}

/// <summary>Optional schema-2 data; schema-1 baselines had no accepted live animation.</summary>
internal sealed class NpcRouteOutro
{
    public List<Dictionary<string, JsonElement>> Frames { get; set; } = new();
    public Dictionary<string, JsonElement> Sprite { get; set; } = new();
    public Dictionary<string, JsonElement> Npc { get; set; } = new();
    public int Speed { get; set; }
    public float AddedSpeed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Texture { get; set; }
    public string? TextureOverride { get; set; }
    public float DrawX { get; set; }
    public float DrawY { get; set; }
    public float AppliedX { get; set; }
    public float AppliedY { get; set; }
}

internal sealed class ScheduleRouteFrame
{
    public int At { get; set; }
    public int Time { get; set; }
    public int Facing { get; set; }
    public string? Behavior { get; set; }
    public string? Message { get; set; }
    public string? TargetLocation { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public bool HasRoute { get; set; }
    public List<int[]> Route { get; set; } = new();

    internal static ScheduleRouteFrame Capture(int at, SchedulePathDescription path) => new()
    {
        At = at, Time = path.time, Facing = path.facingDirection,
        Behavior = path.endOfRouteBehavior, Message = path.endOfRouteMessage,
        TargetLocation = path.targetLocationName, TargetX = path.targetTile.X, TargetY = path.targetTile.Y,
        HasRoute = path.route is not null,
        Route = path.route?.Select(p => new[] { p.X, p.Y }).ToList() ?? new()
    };

    internal SchedulePathDescription Restore()
    {
        if (Route is null || Route.Any(p => p is null || p.Length != 2))
            throw new InvalidDataException("Invalid NPC schedule route.");
        // Stack enumeration is top first; reverse before constructing a fresh stack.
        return new SchedulePathDescription(HasRoute ? new Stack<Point>(Route.AsEnumerable().Reverse().Select(p => new Point(p[0], p[1]))) : null,
            Facing, Behavior, Message, TargetLocation, new Point(TargetX, TargetY)) { time = Time };
    }
}

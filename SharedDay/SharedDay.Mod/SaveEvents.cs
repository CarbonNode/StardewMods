using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace SharedDay.Mod;

/// <summary>
/// Tells the other mods a save is about to happen, and that it has happened.
///
/// SMAPI raises Saving and Saved around the night's save, and mods rely on that: before a save
/// they take out of the world whatever the game cannot write (their own monsters, shop objects,
/// companions) and store their own progress; afterwards they put it back. Shared Day's same-date
/// save runs the game's serializer directly, in the middle of a day, where SMAPI raises neither.
/// On a farm with a large pack the serializer then met one of those objects and failed ("There
/// was an error generating the XML document"), and anything a mod only stores on Saving would not
/// have been in the save at all. So the same two events are raised here, through SMAPI's own
/// event manager, and every mod brackets this save exactly as it brackets the night's.
///
/// SMAPI keeps that manager to itself, so it is reached by reflection. If a later SMAPI moves it,
/// the save goes ahead without the events, as it always did before, and the log says so.
/// </summary>
internal static class SaveEvents
{
    private static bool resolved;
    private static object? savingEvent, savedEvent;
    private static string? unavailable;

    /// <summary>How many save events have been raised for the other mods, for the tests.</summary>
    internal static int Raised { get; private set; }

    /// <summary>Why the events cannot be raised on this SMAPI, or null when they can.</summary>
    internal static string? Unavailable { get { Resolve(); return unavailable; } }

    internal static void RaiseSaving(IMonitor monitor) => Raise(savingEventOf(), typeof(SavingEventArgs), "Saving", monitor);
    internal static void RaiseSaved(IMonitor monitor) => Raise(savedEventOf(), typeof(SavedEventArgs), "Saved", monitor);

    private static object? savingEventOf() { Resolve(); return savingEvent; }
    private static object? savedEventOf() { Resolve(); return savedEvent; }

    private static void Resolve()
    {
        if (resolved) return;
        resolved = true;
        try
        {
            var core = AccessTools.TypeByName("StardewModdingAPI.Framework.SCore")
                ?? throw new MissingMemberException("SMAPI's core type was not found.");
            object instance = AccessTools.Property(core, "Instance")?.GetValue(null)
                ?? AccessTools.Field(core, "Instance")?.GetValue(null)
                ?? throw new MissingMemberException("SMAPI's running core was not found.");
            object manager = AccessTools.Field(core, "EventManager")?.GetValue(instance)
                ?? AccessTools.Property(core, "EventManager")?.GetValue(instance)
                ?? throw new MissingMemberException("SMAPI's event manager was not found.");
            savingEvent = Member(manager, "Saving");
            savedEvent = Member(manager, "Saved");
            if (Raiser(savingEvent, typeof(SavingEventArgs)) is null || Raiser(savedEvent, typeof(SavedEventArgs)) is null)
                throw new MissingMethodException("SMAPI's save events cannot be raised the way this build expects.");
        }
        catch (Exception ex)
        {
            savingEvent = savedEvent = null;
            unavailable = ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message;
        }
    }

    private static object Member(object manager, string name) =>
        AccessTools.Field(manager.GetType(), name)?.GetValue(manager)
        ?? AccessTools.Property(manager.GetType(), name)?.GetValue(manager)
        ?? throw new MissingMemberException($"SMAPI's {name} event was not found.");

    // SMAPI has spelled this RaiseEmpty() and RaiseEmpty<TArgs>() over the years; either will do,
    // and so will Raise(args) with args it lets us make.
    private static Func<object?>? Raiser(object? managed, Type args)
    {
        if (managed is null) return null;
        foreach (var method in managed.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != "RaiseEmpty" || method.GetParameters().Length != 0) continue;
            var callable = method.IsGenericMethodDefinition ? method.MakeGenericMethod(args) : method;
            return () => callable.Invoke(managed, null);
        }
        var raise = managed.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "Raise" && m.GetParameters() is { Length: 1 } p && p[0].ParameterType.IsAssignableFrom(args));
        if (raise is null) return null;
        return () => raise.Invoke(managed, new[] { Activator.CreateInstance(args, nonPublic: true) });
    }

    private static void Raise(object? managed, Type args, string name, IMonitor monitor)
    {
        if (managed is null)
        {
            monitor.LogOnce($"Shared Day could not tell the other mods about its same-date save ({unavailable}). The save goes ahead without them, as it did before 0.1.19.", LogLevel.Warn);
            return;
        }
        try
        {
            Raiser(managed, args)!();
            Raised++;
        }
        catch (Exception ex)
        {
            // One mod's handler failing must not cost everyone the save; SMAPI itself catches
            // handlers one by one, so reaching here means the call itself went wrong.
            var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            monitor.Log($"Raising {name} for the other mods failed: {cause.Message}", LogLevel.Warn);
        }
    }
}

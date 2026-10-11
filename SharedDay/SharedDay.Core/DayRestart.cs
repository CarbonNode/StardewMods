using System.Globalization;

namespace SharedDay.Core;

/// <summary>What a farmer's own game does about a restart of the day.</summary>
public enum RestartAction
{
    /// <summary>No restart today, or this one has been dealt with.</summary>
    None,
    /// <summary>Go to bed: they were on the farm when it restarted, or they arrive for its morning.</summary>
    Bed,
    /// <summary>Note it and stay put: they arrive in a turn that is already under way.</summary>
    Note
}

/// <summary>
/// "Restart today" (0.1.34). The host writes a mark on its own farmer, which every game on the
/// farm sees: the date, a number for this restart, and the farmers who were on the farm when it
/// happened. Each farmer's game takes them to bed once per mark and remembers the mark on that
/// farmer, so a farmer who comes back later is not sent to bed again.
/// </summary>
public static class DayRestart
{
    /// <summary>A farmer arriving up to this clock (HHMM) is arriving for the restarted morning.</summary>
    public const int MorningUntil = 610;

    public sealed record Mark(int Day, long Number, IReadOnlyList<long> OnFarm);

    public static string Write(int day, long number, IEnumerable<long> onFarm)
        => string.Create(CultureInfo.InvariantCulture, $"{day}:{number}:{string.Join(",", onFarm)}");

    /// <summary>The mark, or null when there is none or it is not one.</summary>
    public static Mark? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int day)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long number)) return null;
        var ids = new List<long>();
        foreach (string id in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!long.TryParse(id, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed)) return null;
            ids.Add(parsed);
        }
        return new Mark(day, number, ids.AsReadOnly());
    }

    /// <summary>
    /// What this farmer's game does with the host's mark. <paramref name="seen"/> is the mark this
    /// farmer last dealt with; <paramref name="timeOfDay"/> the clock now (HHMM).
    /// </summary>
    public static RestartAction For(string? mark, string? seen, int today, long me, int timeOfDay)
    {
        if (Read(mark) is not { } restart || restart.Day != today || string.Equals(mark, seen, StringComparison.Ordinal)) return RestartAction.None;
        return restart.OnFarm.Contains(me) || timeOfDay <= MorningUntil ? RestartAction.Bed : RestartAction.Note;
    }

    /// <summary>
    /// Why the host will not restart the day now, or null when it will. Each reason is a sentence
    /// for the farmer who asked.
    /// </summary>
    public static string? Refusal(bool open, bool nightUnderWay, bool festivalOrCutscene, bool paused)
    {
        if (paused) return "The farm is paused. Use Try again first, then restart the day.";
        if (nightUnderWay || !open) return "The night has already started, so today cannot be restarted.";
        if (festivalOrCutscene) return "Somebody is at a festival or in a cutscene. Restart the day once it is over.";
        return null;
    }
}

/// <summary>
/// "Give a farmer a fresh day" (0.1.35). When a farmer's own 6:00 AM starts, the host adds them to
/// a mark on its own farmer, which every game on the farm sees: <c>day|id=number,id=number</c>.
/// That farmer's game puts them in their own bed once per number and writes <c>day:number</c> on
/// them, so a game that reads the mark again (or a second farmer's fresh day the same morning)
/// does not send them to bed twice. Nobody else on the farm is moved.
/// </summary>
public static class FreshDayMark
{
    /// <summary>The mark with <paramref name="farmer"/> added under <paramref name="number"/>. Entries from another day are dropped.</summary>
    public static string Add(string? mark, int day, long farmer, long number)
    {
        var entries = Read(mark) is { } old && old.Day == day ? new Dictionary<long, long>(old.Entries) : new Dictionary<long, long>();
        entries[farmer] = number;
        return string.Create(CultureInfo.InvariantCulture,
            $"{day}|{string.Join(",", entries.OrderBy(e => e.Key).Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Key}={e.Value}")))}");
    }

    public sealed record Mark(int Day, IReadOnlyDictionary<long, long> Entries);

    /// <summary>The mark, or null when there is none or it is not one.</summary>
    public static Mark? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('|');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int day)) return null;
        var entries = new Dictionary<long, long>();
        foreach (string entry in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = entry.Split('=');
            if (pair.Length != 2
                || !long.TryParse(pair[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long id)
                || !long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out long number)) return null;
            entries[id] = number;
        }
        return new Mark(day, entries);
    }

    /// <summary>What this farmer writes on themselves once their game has dealt with their fresh day.</summary>
    public static string Seen(int day, long number) => string.Create(CultureInfo.InvariantCulture, $"{day}:{number}");

    /// <summary>
    /// The <c>Seen</c> text to write and go to bed for, or null when this farmer has no fresh day
    /// to start today, or has already started it.
    /// </summary>
    public static string? Due(string? mark, string? seen, int today, long me)
    {
        if (Read(mark) is not { } read || read.Day != today || !read.Entries.TryGetValue(me, out long number)) return null;
        string due = Seen(today, number);
        return string.Equals(due, seen, StringComparison.Ordinal) ? null : due;
    }

    /// <summary>
    /// A turn resumed at a parked clock of 6:00 AM (a fresh day, or a farmer who left at 6:00) is a
    /// morning: the adapter starts it as it starts any new turn, with the villagers' morning put
    /// back, not by catching the villagers up to a clock that went backwards.
    /// </summary>
    public static bool IsMorning(int hhmm) => hhmm <= DayClock.Opens;
}

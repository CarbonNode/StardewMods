namespace SharedDay.Core;

/// <summary>Serializable clock progress, persisted in the same checkpoint as the farm.</summary>
public sealed record CalendarClockState(int AbsoluteDay, int HighWaterMinute);

/// <summary>
/// Grants machine time only once for each elapsed minute of a shared calendar day.
/// Rewinding the displayed clock never rewinds this budget. This is a GLOBAL budget:
/// a machine placed during replay receives no repeated minutes until the displayed
/// clock passes the day's previous high-water time. The adapter applies each returned
/// delta to all machines once; it must not call this separately for individual machines.
/// </summary>
public sealed class CalendarClock
{
    public const int MorningMinute = 6 * 60;
    public const int EndOfDayMinute = 26 * 60;

    public int AbsoluteDay { get; private set; }
    public int HighWaterMinute { get; private set; }

    public CalendarClock(int absoluteDay, int highWaterMinute = MorningMinute)
    {
        if (absoluteDay < 0) throw new ArgumentOutOfRangeException(nameof(absoluteDay));
        ValidateMinute(highWaterMinute);
        AbsoluteDay = absoluteDay;
        HighWaterMinute = highWaterMinute;
    }

    public static CalendarClock Restore(CalendarClockState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state.AbsoluteDay, state.HighWaterMinute);
    }

    public CalendarClockState ExportState() => new(AbsoluteDay, HighWaterMinute);

    /// <summary>
    /// Observe one game tick's target minute within its calendar day (06:00 is 360).
    /// Returns only the newly reached interval. Repeated/reversed times return zero.
    /// </summary>
    public int AdvanceToMinute(int targetMinute)
    {
        ValidateMinute(targetMinute);
        if (targetMinute <= HighWaterMinute) return 0;
        int granted = targetMinute - HighWaterMinute;
        HighWaterMinute = targetMinute;
        return granted;
    }

    public int AdvanceToTimeOfDay(int timeOfDay) => AdvanceToMinute(ToMinutes(timeOfDay));

    /// <summary>
    /// Reset only after the adapter has confirmed the next calendar day's durable game
    /// save, alongside its day coordinator acknowledgement. Never reset for a new
    /// relay session, a clock rewind, a reconnect, or an unconfirmed DayStarted event.
    /// </summary>
    public void ConfirmNextDay(int actualSavedDay)
    {
        if ((long)actualSavedDay != (long)AbsoluteDay + 1)
            throw new DayCoordinationException("The machine-time budget resets only for a confirmed save exactly one day later.");
        AbsoluteDay = actualSavedDay;
        HighWaterMinute = MorningMinute;
    }

    /// <summary>Convert Stardew's HHMM clock, including 24:00–26:00, to minutes.</summary>
    public static int ToMinutes(int timeOfDay)
    {
        if (timeOfDay < 600 || timeOfDay > 2600 || timeOfDay % 100 >= 60)
            throw new ArgumentOutOfRangeException(nameof(timeOfDay), "The game clock must be between 06:00 and 26:00 with valid minutes.");
        return timeOfDay / 100 * 60 + timeOfDay % 100;
    }

    private static void ValidateMinute(int minute)
    {
        if (minute < MorningMinute || minute > EndOfDayMinute)
            throw new ArgumentOutOfRangeException(nameof(minute), "A calendar-day minute must be between 06:00 and 26:00.");
    }
}

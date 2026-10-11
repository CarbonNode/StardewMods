namespace SharedDay.Core;

/// <summary>
/// The farm's clock as the game counts it: HHMM, from 600 (6:00 AM) to 2600 (2:00 AM the next
/// morning, which the game writes as hour 26). A game day is those 20 hours.
/// </summary>
public static class DayClock
{
    public const int Opens = 600;
    public const int Closes = 2600;
    public const int MinutesInDay = 20 * 60;

    public static bool IsValid(int hhmm) => hhmm >= Opens && hhmm <= Closes && hhmm % 100 < 60;

    /// <summary>Minutes of the day already gone at this clock: 0 at 6:00 AM, 1,200 at 2:00 AM. Never outside that.</summary>
    public static int MinutesGone(int hhmm)
    {
        int minutes = (hhmm / 100 - 6) * 60 + Math.Min(59, Math.Max(0, hhmm % 100));
        return Math.Clamp(minutes, 0, MinutesInDay);
    }

    /// <summary>"2:10 PM". Past midnight the game counts on: 2400 is "12:00 AM", 2530 is "1:30 AM".</summary>
    public static string Words(int hhmm)
    {
        int hour = Math.Max(0, hhmm / 100) % 24, minute = Math.Min(59, Math.Max(0, hhmm % 100));
        int shown = hour % 12 == 0 ? 12 : hour % 12;
        return $"{shown}:{minute:00} {(hour < 12 ? "AM" : "PM")}";
    }

    /// <summary>"8 h 10 min", "8 h", "40 min".</summary>
    public static string Span(int minutes)
    {
        minutes = Math.Max(0, minutes);
        int h = minutes / 60, m = minutes % 60;
        return h == 0 ? $"{m} min" : m == 0 ? $"{h} h" : $"{h} h {m} min";
    }
}

public enum JoinOutlook
{
    /// <summary>Joining now costs this player nothing, or nothing is known well enough to say.</summary>
    Nothing,
    /// <summary>The player has a turn of their own still open at this clock: they carry on in it.</summary>
    Resume,
    /// <summary>Joining now starts the player's day part way through; waiting would start it at 6:00 AM.</summary>
    Warn
}

/// <summary>What to tell a player before they join. <see cref="Text"/> is empty for <see cref="JoinOutlook.Nothing"/>.</summary>
public sealed record JoinAdvice(JoinOutlook Kind, string Text)
{
    public static readonly JoinAdvice None = new(JoinOutlook.Nothing, "");
    /// <summary>The two answers to a warning. Wait is the first, safe one.</summary>
    public const string WaitAnswer = "Wait";
    public const string JoinAnswer = "Join now";
}

/// <summary>
/// "im confused what happens if someone is half way through day and someone else joins server"
/// (the owner, 2026-10-07). In Relay a turn is one run of the clock from 6:00 AM, shared by everyone
/// in it. A player who joins while a turn has an unfinished farmer playing in it enters that turn
/// at its clock (<see cref="DayCoordinator.Join"/>: JoinCurrentSession). Only when nobody is
/// playing does a joiner start a turn of their own at 6:00 AM; since 0.1.29 a farmer who left a
/// turn unfinished keeps a clock of their own (ResumeOwnClock) and holds nobody else's. So the
/// hours already played in someone else's turn are hours the joiner does not get, and nothing
/// said so.
///
/// This works out, from the farm's turn tracker alone, what a player is about to get, and words
/// it. The same rules and words are the launcher's (docs/midday-join.md).
/// </summary>
public static class MiddayJoin
{
    /// <summary>
    /// A report this young is the farm's clock "about" now. While a turn is being played the host
    /// reports every half hour of game time (21 seconds), so a report older than this comes from
    /// an older host or a held clock, and its time is only the least it can be.
    /// </summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);

    /// <summary>The tracker calls a host offline after this long without a report; nothing is said about a farm that quiet.</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromMinutes(12);

    /// <param name="farm">The farm as its tracker last reported it.</param>
    /// <param name="player">The player's ModRelay name (the launcher's link file), which the tracker matches to a farmer. Null when unknown.</param>
    /// <param name="hostsItself">This PC is the farm's own unattended host: it never joins, and is never warned.</param>
    /// <param name="age">How old the report is now. Null when that cannot be told.</param>
    public static JoinAdvice Before(HubFarm? farm, string? player, bool hostsItself, TimeSpan? age)
    {
        if (hostsItself || farm is null || !farm.Reported || !farm.HostOnline || farm.Paused is not null) return JoinAdvice.None;
        // Together: playing at the same time is the point. An overnight save has no turn to join.
        if (farm.Mode != "Relay" || farm.Phase != "Open") return JoinAdvice.None;
        if (farm.Time is not { } time || !DayClock.IsValid(time)) return JoinAdvice.None;
        if (age is { } old && (old > Stale || old < TimeSpan.Zero)) return JoinAdvice.None;
        int gone = DayClock.MinutesGone(time);
        if (gone <= 0) return JoinAdvice.None;

        // The turn in progress keeps its clock for as long as one farmer in it is unfinished AND
        // in the farm. Since 0.1.29 a farmer who left a turn unfinished keeps a clock of their own
        // and holds nobody else's: with nobody playing, a joiner gets a morning of their own.
        var playing = farm.Farmers.Where(f => f.Status == "Pending" && f.InTurn && f.Online).ToList();
        bool running = playing.Count > 0;
        bool fresh = age is { } young && young <= Fresh;
        string clock = DayClock.Words(time), span = DayClock.Span(gone);
        string ago = age is { } a ? HubFarm.FormatAge(a) : "a while ago";

        var mine = player is null ? new List<HubFarmer>()
            : farm.Farmers.Where(f => f.Player is not null && string.Equals(f.Player.Trim(), player.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        bool sure = true;
        if (mine.Count > 0)
        {
            // Their own turn is still open: they carry on in it. With somebody else playing, the
            // game asks them on the farm whether to join that turn or wait and carry on at their own
            // clock, which the tracker does not know, so nothing here names a time for it.
            if (mine.Any(f => f.Status == "Pending" && f.InTurn))
                return new(JoinOutlook.Resume, !running ? "Your turn is still open. Join and you carry on where you left it."
                    : $"Your turn is still open. {HubFarm.JoinNames(playing.Select(f => f.Name).ToList())} {(playing.Count == 1 ? "is" : "are")} playing now: the farm asks whether you join that turn at its clock or wait and carry on where you left yours.");
            // Played or sitting out by choice: they only watch, and lose nothing. A farmer the farm
            // stopped waiting for (Away) is different: joining puts them straight back into today.
            if (!mine.Any(f => f.StillToPlay)) return JoinAdvice.None;
        }
        else
        {
            // Nobody the tracker knows as this player. A farmer no account has claimed and who is not
            // in the farm right now could be theirs; if any of those has played, sits out or holds
            // this turn, joining may cost nothing, and the warning says "if".
            sure = farm.Farmers.Where(f => f.Player is null && !f.Online).All(f => f.Status == "Pending" && !f.InTurn);
        }
        if (!running) return JoinAdvice.None;

        string now = HubFarm.JoinNames(playing.Select(f => f.Name).ToList());
        string wait = $"Wait until {now} {(playing.Count == 1 ? "has" : "have")} finished and you start at 6:00 AM.";
        string starts = sure ? "Join now and your day starts" : "If you have not played today, joining now starts your day";
        string are = playing.Count == 1 ? "is" : "are";
        return new(JoinOutlook.Warn, fresh
            ? $"{now} {are} playing now and it is about {clock} on the farm. {starts} there: about {span} of today's 20 hours are already gone. {wait}"
            : $"{now} {are} playing now. The farm last said it was {clock}, {ago}, and it is later than that now. {starts} at the farm's time, not at 6:00 AM: at least {span} of today's 20 hours are already gone. {wait}");
    }

    /// <summary>
    /// The one line a player reads in chat after the host has put them into a turn that was already
    /// running. Null when they got a fresh morning, only watch, or the farm plays Together.
    /// </summary>
    /// <param name="wasInTurn">They were already part of this turn (they left it unfinished and are back).</param>
    /// <param name="time">The host's clock at the moment they joined (for ResumeOwnClock, their own parked clock, now the farm's).</param>
    public static string? After(JoinDisposition disposition, bool wasInTurn, PlayMode mode, int time)
    {
        if (mode != PlayMode.Relay || !DayClock.IsValid(time)) return null;
        if (disposition == JoinDisposition.ResumeOwnClock)
            return $"You are back in your turn. It is {DayClock.Words(time)}, where you left it.";
        if (disposition is not (JoinDisposition.JoinCurrentSession or JoinDisposition.ResumeSession)) return null;
        int gone = DayClock.MinutesGone(time);
        if (gone <= 0) return null;
        string clock = DayClock.Words(time);
        return wasInTurn
            ? $"You are back in your turn. It is {clock}, where the turn had got to."
            : $"You joined at {clock}: {DayClock.Span(gone)} of today's 20 hours had already gone. The clock is shared, so your day ends at 2:00 AM with everyone else's.";
    }
}

/// <summary>
/// When the host sends the farm's state to its turn tracker (docs/turn-tracker-contract.md): a
/// change at once (but never two inside five seconds), a heartbeat every five minutes, and, while
/// a Relay turn is being played, the clock every half hour of game time. Without the last, the
/// clock a player reads before joining could be five real minutes old, which is seven hours on
/// the farm.
/// </summary>
public static class TrackerCadence
{
    public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);
    /// <summary>Game minutes the clock moves between two reports while a turn is being played.</summary>
    public const int ClockStep = 30;

    /// <param name="changed">Who has played, who is online or whether the farm is paused differs from the last report.</param>
    /// <param name="sinceLast">Real time since the last report.</param>
    /// <param name="turnBeingPlayed">Relay, the date is open and an unfinished farmer of the current turn is online.</param>
    /// <param name="minutesNow">The game clock now, in minutes of the day.</param>
    /// <param name="minutesAtLast">The game clock in the last report.</param>
    public static bool Due(bool changed, TimeSpan sinceLast, bool turnBeingPlayed, int minutesNow, int minutesAtLast)
    {
        if (sinceLast < MinimumGap) return false;
        if (changed || sinceLast >= Heartbeat) return true;
        return turnBeingPlayed && Math.Abs(minutesNow - minutesAtLast) >= ClockStep;
    }
}

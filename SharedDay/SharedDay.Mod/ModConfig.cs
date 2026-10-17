using SharedDay.Core;
using StardewModdingAPI;

namespace SharedDay.Mod;

public sealed class ModConfig
{
    // Opt in per installation after making a disposable test farm.
    public bool Enabled { get; set; }
    public PlayMode Mode { get; set; } = PlayMode.Relay;
    public bool IncludeHost { get; set; } = true;
    /// <summary>
    /// A farmer who has not connected for this many days in a row stops holding the farm: from
    /// then on they sit each day out by themselves, with no vote needed, and are back in the day
    /// the moment they connect. 0 waits for every farmer for ever.
    /// </summary>
    public int AwayAfterDays { get; set; } = 3;
    public long[] RequiredPlayerIds { get; set; } = Array.Empty<long>();
    public SButton MenuKey { get; set; } = SButton.F8;
    /// <summary>
    /// On a farm that hosts itself: the one key that sits somebody down at the host PC as its own
    /// farmer, and gets them up again. No other press there reaches the game while the seat is empty.
    /// "None": nobody can sit down there at all, and that PC is only ever the server.
    /// </summary>
    public SButton SeatKey { get; set; } = SButton.F9;
    public NotificationConfig Notifications { get; set; } = new();
    public TurnTrackerConfig TurnTracker { get; set; } = new();
    public JoinServerConfig JoinServer { get; set; } = new();
    public AutoHostConfig AutoHost { get; set; } = new();
    public BypassConfig Bypass { get; set; } = new();
    public HudConfig Hud { get; set; } = new();
    /// <summary>
    /// Players. The game's join screen (the list of farmers after connecting) shows one "Make a new
    /// farmer" row instead of one bare "New Farmer" row per empty cabin, your own farmers first,
    /// and the farm's name without a doubled "Farm". Only this PC's screen changes; false leaves
    /// the game's own list.
    /// </summary>
    public bool TidyJoinList { get; set; } = true;
    /// <summary>
    /// Host (0.1.37). A player needs a ModRelay account in the farm's group to join at all: one
    /// with no account (a game the launcher did not start, or a ticket that does not check out)
    /// is disconnected with "this farm is private to its group". Null, the default, means yes on
    /// a farm whose turn tracker has told it the group, and no otherwise. Whatever this says,
    /// a farmer that belongs to another account is never anyone else's to play.
    /// </summary>
    public bool? RequireGroupAccount { get; set; }
    /// <summary>
    /// Host (0.1.40). Build a cabin for every group member who still has to make a farmer, so a
    /// full farm never holds the day for somebody who cannot join it. The game's own safety
    /// checks decide where: nothing anyone owns is cleared to make room. False leaves the farm
    /// exactly as the host built it.
    /// </summary>
    public bool BuildCabinsForGroup { get; set; } = true;
    /// <summary>
    /// Host only (0.1.29). While exactly one remote farmer is playing, their turn is saved by itself
    /// every this many minutes of farm time (a "Save my turn" nobody has to press: the player is
    /// held still for a moment, the farm is written, and they carry on). 0, the default, never.
    /// </summary>
    public int SaveTurnEveryMinutes { get; set; }
}

/// <summary>
/// The farm's bypass password, on the host only, as a salted hash (see BypassCode). Empty: the
/// farm has no bypass. The password itself is in no file.
/// </summary>
public sealed class BypassConfig
{
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
}

public sealed class FinishRequest
{
    public int Day { get; set; }
    public int Session { get; set; }
}

/// <summary>A farmer's vote to let an absent farmer sit today out. The host counts it under the sender SMAPI names.</summary>
public sealed class SkipVoteRequest
{
    public int Day { get; set; }
    public long Target { get; set; }
}

/// <summary>
/// A farmer asking the host to put something right from "Fix a problem": "reopen" (their own
/// finished day), "unskip" (bring Target back into today), "refresh" (send the farm's state again)
/// or "retry" (lift a pause and check again; the one fix a paused farm takes). From 0.1.29 also
/// the answers to the return choice, "joinnow" and "wait", and "savenow" (Save my turn).
/// </summary>
public sealed class FixRequest
{
    public int Day { get; set; }
    public string Kind { get; set; } = "";
    public long Target { get; set; }
    /// <summary>"bypass" only: the password as typed. The host checks it again itself.</summary>
    public string? Secret { get; set; }
}

/// <summary>Where the vote on one absent farmer stands, as the host tells every player.</summary>
public sealed class SkipVoteEntry
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int Votes { get; set; }
    public int Needed { get; set; }
    public long[] Voters { get; set; } = Array.Empty<long>();
}

/// <summary>Farmer.health is not a net field; a farmhand reports it so mid-day checkpoints keep it.</summary>
public sealed class HealthMessage
{
    public int Health { get; set; }
}

public sealed class HelloMessage
{
    public int Protocol { get; set; }
    public bool Enabled { get; set; }
    /// <summary>0.1.37: who this player is, from the launcher's link file; the host checks the ticket (see JoinTicket). Null from a game the launcher did not start.</summary>
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? Ticket { get; set; }
}

/// <summary>Host to a farmer just made (0.1.37): they are on today's roster now, and may play, leave or sit today out.</summary>
public sealed class WelcomeMessage
{
    public string Date { get; set; } = "";
}

public sealed class StatusMessage
{
    public int Protocol { get; set; } = ModEntry.ProtocolVersion;
    public int Day { get; set; }
    public int Session { get; set; }
    public long Revision { get; set; }
    public long[] Finished { get; set; } = Array.Empty<long>();
    public string[] WaitingFor { get; set; } = Array.Empty<string>();
    public bool Released { get; set; }
    public bool CheckpointPending { get; set; }
    public bool HasFatalError { get; set; }
    public string? Error { get; set; }
    // Added in 0.1.6 for player-facing text; older peers ignore them.
    public PlayMode Mode { get; set; }
    public string[] FinishedNames { get; set; } = Array.Empty<string>();
    public string[] SkippedNames { get; set; } = Array.Empty<string>();
    // Added in 0.1.11 for the in-game turn board; older peers ignore it, and a board drawn for
    // an older host falls back to the names above.
    public RosterEntry[] Roster { get; set; } = Array.Empty<RosterEntry>();
    // Added in 0.1.17: every absent farmer who can be voted on, with the votes so far. Null from
    // an older host, which takes no votes, so its players are not offered one.
    public SkipVoteEntry[]? SkipVotes { get; set; }
    // Added in 0.1.18: 1 when the host answers "Fix a problem" requests. 0 from an older host,
    // whose players are offered only the fixes that need no host. 2 from 0.1.19: it also takes
    // "Try again" while paused. 3 from 0.1.29: it also takes "joinnow", "wait" and "savenow".
    public int Fixes { get; set; }
    // Added in 0.1.29: the farmers who have come back to a parked turn while somebody else plays
    // and are asked (or have chosen to wait), with what the question needs. Null from an older host.
    public ReturnChoiceEntry[]? ReturnChoices { get; set; }
    // Added in 0.1.29: the farmers whose current admission put the clock back to where they left
    // their turn, so their own game puts them back where they stood. Null from an older host.
    public long[]? ResumedAtOwnClock { get; set; }
    // Added in 0.1.29: the clock (HHMM) the turn in progress started at (6:00 AM for a fresh turn,
    // a parked clock for a resumed one), so a reader can tell where a turn began whatever the clock
    // reads now. 0 from an older host.
    public int TurnStartedAt { get; set; }
    // Added in 0.1.23: the farm's bypass, as salt and hash, so a player whose host has stopped
    // answering can still be let out. Null when the farm has none.
    public string? BypassSalt { get; set; }
    public string? BypassHash { get; set; }
    // Added in 0.1.20: farmers sitting out only because they have been away. Null from an older host.
    public string[]? AwayNames { get; set; }
    // Added in 0.1.33: the farm's sticky notes, oldest first. Null from an older host (no board).
    public StickyNoteEntry[]? Notes { get; set; }
}

/// <summary>One sticky note as the host tells every player about it (0.1.33).</summary>
public sealed class StickyNoteEntry
{
    public string Id { get; set; } = "";
    public long AuthorId { get; set; }
    public string Author { get; set; } = "";
    public int Day { get; set; }
    public string Date { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>A farmer pinning a note to the farm's board, or taking their own down (0.1.33). The host checks and keeps it.</summary>
public sealed class StickyNoteRequest
{
    public int Day { get; set; }
    /// <summary>"add" or "remove".</summary>
    public string Action { get; set; } = "";
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>The host's answer to a note that could not be pinned (0.1.33): the reason, for the player's screen.</summary>
public sealed class StickyNoteRefused
{
    public string Reason { get; set; } = "";
}

/// <summary>
/// One line from the host for one player's chat: what just happened to them, in plain words
/// (they joined part way through a turn; the farm's wallets changed). Added in 0.1.28. An older
/// player's game ignores a message type it does not know, and an older host never sends one.
/// </summary>
public sealed class NoteMessage
{
    public string Text { get; set; } = "";
}

/// <summary>
/// A farmer back on the farm with a parked turn while somebody else plays (0.1.29): the host
/// tells them who is playing and at what time, and where their own turn stands, so their popup
/// can ask "join now or wait". <see cref="Waiting"/>: they chose to wait.
/// </summary>
public sealed class ReturnChoiceEntry
{
    public long Id { get; set; }
    public string[] Playing { get; set; } = Array.Empty<string>();
    public int TheirTime { get; set; }
    public int MyTime { get; set; }
    public bool Waiting { get; set; }
}

/// <summary>
/// "Save my turn" (0.1.29). "hold": the host asks every connected player to freeze for a save;
/// "release": they may move again. The token tells a late answer from a current one.
/// </summary>
public sealed class HoldMessage
{
    public int Day { get; set; }
    public int Token { get; set; }
}

/// <summary>"held": a player's answer to a hold, once frozen and with their health sent.</summary>
public sealed class HeldMessage
{
    public int Day { get; set; }
    public int Token { get; set; }
}

/// <summary>
/// "nights" (0.1.29): the nights that ran while this farmer was away, oldest first, for their
/// own game to show (the shipping totals and level-ups) before their turn starts. Display only.
/// </summary>
public sealed class NightsMessage
{
    public int Day { get; set; }
    public List<NightRecord> Records { get; set; } = new();
}

/// <summary>"nightsshown": a player has been shown their owed nights through this date.</summary>
public sealed class NightsShownMessage
{
    public int ThroughDay { get; set; }
}

/// <summary>One farmer on today's roster, as the host sends it to every player.</summary>
public sealed class RosterEntry
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public FarmerStatus Status { get; set; }
    public bool Online { get; set; }
    public bool InTurn { get; set; }
    /// <summary>0.1.37: a group member with no farmer on the farm yet (Name reads "Pat (no farmer yet)"). Nobody can play it; it is waited for or skipped.</summary>
    public bool Placeholder { get; set; }
}

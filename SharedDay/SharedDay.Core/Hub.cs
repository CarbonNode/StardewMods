using System.Text.Json;

namespace SharedDay.Core;

/// <summary>
/// What the title-screen hub knows about the player's farm before any network call: which
/// farm it is, where its turn tracker lives and how to join it. It comes from two places.
/// The ModRelay launcher writes a link file beside the Mods folder for the group the player
/// is in (<see cref="ParseFile"/>); a host or a player without the launcher sets the same
/// things in Shared Day's config.json. Config wins field by field, because a person typed it.
/// </summary>
public sealed record HubLink(
    string? Label,
    string? GroupName,
    string? Player,
    string? TrackerUrl,
    string? PageUrl,
    string? JoinAddress,
    string? InviteCode,
    string? HostName,
    string? HostState)
{
    public const string FileName = "modrelay-shared-day.json";
    public const string Schema = "modrelay.shared-day-link/1";
    public static readonly HubLink Empty = new(null, null, null, null, null, null, null, null, null);

    /// <summary>
    /// A way into the farm that is worth offering. A host PC that says it is asleep, starting or
    /// off is believed over an address it shared earlier: nothing is listening there.
    /// </summary>
    public bool CanJoin => (JoinAddress is not null || InviteCode is not null) && HostState is null or "awake";
    public bool HasFarm => TrackerUrl is not null;
    /// <summary>Anything at all to show on the title screen.</summary>
    public bool Any => CanJoin || HasFarm || GroupName is not null;
    /// <summary>What to call the farm on the button: its own label, else the group's name.</summary>
    public string? Name => Label ?? GroupName;

    /// <summary>The launcher's link file. Null when it is not one (wrong schema, not JSON).</summary>
    public static HubLink? ParseFile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "schema") != Schema) return null;
            var group = Child(root, "group");
            var farm = Child(root, "farm");
            var host = Child(root, "host");
            return new HubLink(
                Text(farm, "label"),
                Text(group, "name"),
                Text(root, "player"),
                Web(Text(farm, "trackerUrl")),
                Web(Text(farm, "pageUrl")),
                Text(host, "joinAddress"),
                Text(host, "joinCode"),
                Text(host, "name"),
                Text(host, "state"));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Config over the link file, field by field. The tracker's page is derived when only its URL is known.</summary>
    public static HubLink Merge(HubLink? file, string? configLabel, string? configAddress, string? configInviteCode, string? configTrackerUrl)
    {
        var f = file ?? Empty;
        string? address = Clean(configAddress), code = Clean(configInviteCode), tracker = Web(Clean(configTrackerUrl));
        // A join target typed into config replaces the launcher's as a pair: never one side's address with the other's code.
        bool typedTarget = address is not null || code is not null;
        string? trackerUrl = tracker ?? f.TrackerUrl;
        return f with
        {
            Label = Clean(configLabel) ?? f.Label,
            JoinAddress = typedTarget ? address : f.JoinAddress,
            InviteCode = typedTarget ? code : f.InviteCode,
            // A target a person typed is theirs to try, whatever a launcher's host says.
            HostState = typedTarget ? null : f.HostState,
            TrackerUrl = trackerUrl,
            PageUrl = tracker is not null ? PageFor(tracker) : f.PageUrl ?? (trackerUrl is null ? null : PageFor(trackerUrl)),
        };
    }

    /// <summary>The farm's page for a tracker address: the same path plus /page, without any query or fragment.</summary>
    public static string? PageFor(string trackerUrl) => Under(trackerUrl, "page");

    /// <summary>Where the hub wakes the farm's host PC: the tracker's address plus /wake (POST, with the wake password).</summary>
    public static string? WakeFor(string trackerUrl) => Under(trackerUrl, "wake");

    private static string? Under(string trackerUrl, string leaf)
    {
        if (!Uri.TryCreate(trackerUrl, UriKind.Absolute, out var uri)) return null;
        return new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" + leaf, Query = "", Fragment = "" }.Uri.ToString();
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Only http(s) addresses are ever fetched or opened.</summary>
    private static string? Web(string? s)
        => s is not null && Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? s : null;

    private static JsonElement? Child(JsonElement? parent, string name)
        => parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    internal static string? Text(JsonElement? parent, string name)
        => parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Clean(v.GetString()) : null;
}

/// <summary>
/// The group's host PC as the tracker tells the game about it (the state's `host`, from the
/// server of 2026-10-08): its name, whether it is up, whether a wake is already on its way, and
/// whether the game may wake it with the wake password. Never a way in.
/// </summary>
public sealed record HubHost(string Name, string State, bool WakePending, bool WakeFromFarm)
{
    /// <summary>The host loop's own word on what it is doing while it starts ("Starting the game and loading the farm"). Null when it says nothing.</summary>
    public string? Note { get; init; }
}

public sealed record HubFarmer(string Name, string Status, bool Online, string? Player)
{
    /// <summary>Part of the turn in progress (the report's inCurrentTurn). False from a report without it.</summary>
    public bool InTurn { get; init; }
    /// <summary>
    /// Sitting out only because the farm stopped waiting for them (the report's away). The moment
    /// they connect they are Pending again and play today. False from a report without it.
    /// </summary>
    public bool Away { get; init; }
    /// <summary>Their turn is still to play if they join: Pending, or Away (which a join turns back into Pending).</summary>
    public bool StillToPlay => Status == "Pending" || (Status == "Skipped" && Away);
}

/// <summary>
/// The farm as its ModRelay turn tracker last reported it (GET on the tracker's URL), reduced
/// to what the title-screen hub shows. Parsed leniently: a field a newer server adds or an
/// older one lacks never breaks the hub.
/// </summary>
public sealed record HubFarm(
    string? FarmName,
    string? DateLabel,
    int? Year,
    string? Mode,
    string? Phase,
    string? Paused,
    bool HostOnline,
    bool Reported,
    TimeSpan? Age,
    IReadOnlyList<HubFarmer> Farmers)
{
    /// <summary>The farm's clock in the report, HHMM (600 to 2600). Null when the report has none.</summary>
    public int? Time { get; init; }
    /// <summary>The group's host PC, from a server that tells the game about it. Null from an older server, a link-only farm or a group with no host.</summary>
    public HubHost? Host { get; init; }

    public static HubFarm? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tracker_id", out _)) return null;
            bool online = root.TryGetProperty("host_online", out var ho) && ho.ValueKind == JsonValueKind.True;
            TimeSpan? age = null;
            if (Stamp(root, "updated_at") is { } updated && Stamp(root, "server_time") is { } server && server >= updated) age = server - updated;
            var host = ParseHost(root);
            if (!root.TryGetProperty("snapshot", out var snap) || snap.ValueKind != JsonValueKind.Object)
                return new HubFarm(HubLink.Text(root, "label"), null, null, null, null, null, online, false, age, Array.Empty<HubFarmer>()) { Host = host };

            // Who plays which farmer, from the accounts that joined the tracker.
            var players = new Dictionary<string, string>();
            if (root.TryGetProperty("members", out var members) && members.ValueKind == JsonValueKind.Array)
                foreach (var m in members.EnumerateArray())
                    if (HubLink.Text(m, "farmer_id") is { } id && HubLink.Text(m, "display_name") is { } name) players[id] = name;

            var farmers = new List<HubFarmer>();
            if (snap.TryGetProperty("farmers", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var f in list.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.Object) continue;
                    // A farmer with no name yet still has a turn; the launcher calls them the same.
                    string name = HubLink.Text(f, "name") ?? "A farmer";
                    string? id = HubLink.Text(f, "id");
                    farmers.Add(new HubFarmer(name, HubLink.Text(f, "status") ?? "Pending",
                        f.TryGetProperty("online", out var on) && on.ValueKind == JsonValueKind.True,
                        id is not null && players.TryGetValue(id, out var player) ? player : null)
                    {
                        InTurn = f.TryGetProperty("inCurrentTurn", out var turn) && turn.ValueKind == JsonValueKind.True,
                        Away = f.TryGetProperty("away", out var away) && away.ValueKind == JsonValueKind.True
                    });
                }
            JsonElement? farm = snap.TryGetProperty("farm", out var fm) ? fm : null, date = snap.TryGetProperty("date", out var dt) ? dt : null;
            int? year = date is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out int yy) ? yy : null;
            return new HubFarm(HubLink.Text(farm, "name") ?? HubLink.Text(root, "label"), HubLink.Text(date, "label"), year,
                HubLink.Text(snap, "mode"), HubLink.Text(snap, "phase"), HubLink.Text(snap, "paused"), online, true, age, farmers)
            { Time = snap.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.Number && time.TryGetInt32(out int hhmm) ? hhmm : null, Host = host };
        }
        // Whatever a server sends, the hub shows a farm or says it has none; it never throws.
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException) { return null; }
    }

    /// <summary>The state's `host`, when the server sends one with a name and a state it can read.</summary>
    private static HubHost? ParseHost(JsonElement root)
    {
        if (!root.TryGetProperty("host", out var h) || h.ValueKind != JsonValueKind.Object) return null;
        string? name = HubLink.Text(h, "name"), state = HubLink.Text(h, "state");
        if (name is null || state is null) return null;
        return new HubHost(name, state,
            h.TryGetProperty("wake_pending", out var pending) && pending.ValueKind == JsonValueKind.True,
            h.TryGetProperty("wake_from_farm", out var offered) && offered.ValueKind == JsonValueKind.True)
        { Note = HubLink.Text(h, "note") };
    }

    /// <summary>
    /// The hub's answer to a wake it sent (POST on <see cref="HubLink.WakeFor"/>), from the status
    /// and body, in the player's words. Lenient like <see cref="Parse"/>: an unknown body is a
    /// plain failure, never a crash.
    /// </summary>
    public static WakeAnswer ParseWakeAnswer(int status, string body)
    {
        string? code = null, state = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                code = HubLink.Text(doc.RootElement, "code");
                state = HubLink.Text(doc.RootElement, "state");
            }
        }
        catch (JsonException) { }
        if (status is >= 200 and < 300)
            return state switch
            {
                "awake" => new WakeAnswer(WakeResult.AlreadyUp, "The farm is already up."),
                "starting" => new WakeAnswer(WakeResult.AlreadyUp, "The farm is already waking up."),
                _ => new WakeAnswer(WakeResult.Woken, WakeOutlook.WokenWords),
            };
        return (status, code) switch
        {
            (403, "wrong_password") => new WakeAnswer(WakeResult.WrongPassword, "That is not the wake password."),
            (403, _) => new WakeAnswer(WakeResult.PasswordRequired, "Type the farm's wake password."),
            (429, _) => new WakeAnswer(WakeResult.Throttled, "Too many wrong passwords. Try again in a few minutes."),
            (409, _) => new WakeAnswer(WakeResult.NoPassword, WakeOutlook.NoPasswordWords),
            (404, _) or (410, _) => new WakeAnswer(WakeResult.Gone, "ModRelay has no host PC for this farm any more."),
            _ => new WakeAnswer(WakeResult.Failed, "ModRelay did not answer. Try again in a moment."),
        };
    }

    private static DateTimeOffset? Stamp(JsonElement root, string name)
        => HubLink.Text(root, name) is { } s && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    public IEnumerable<HubFarmer> Pending => Farmers.Where(f => f.Status == "Pending");

    /// <summary>Still to play first, then played, then sitting out; the host's order within each.</summary>
    public IReadOnlyList<HubFarmer> Sorted => Farmers
        .Select((f, i) => (f, i))
        .OrderBy(x => x.f.Status switch { "Pending" => 0, "Completed" => 1, "Skipped" => 2, _ => 3 })
        .ThenBy(x => x.i)
        .Select(x => x.f)
        .ToList();

    /// <summary>The one line that answers "what is the farm waiting for?", in the launcher's words.</summary>
    public string Headline
    {
        get
        {
            if (!Reported) return "No report from the farm yet";
            if (Paused is not null) return "Paused";
            if (Phase == "Advancing") return "Saving overnight, the next day is coming";
            if (Farmers.Count == 0) return "No farmers yet";
            var pending = Pending.Select(f => f.Name).ToList();
            if (pending.Count == 0) return "Everyone has played, the day ends when the farm saves";
            return (Mode == "Together" ? "Playing together, waiting on " : "Waiting on ") + JoinNames(pending);
        }
    }

    /// <summary>The button's short second line: "Spring 13, waiting on Bob".</summary>
    public string Summary
    {
        get
        {
            if (!Reported) return "No report yet";
            string what = Paused is not null ? "paused" : Phase == "Advancing" ? "saving the night"
                : !Pending.Any() ? "everyone has played" : "waiting on " + JoinNames(Pending.Select(f => f.Name).ToList(), 2);
            return DateLabel is null ? char.ToUpperInvariant(what[0]) + what[1..] : DateLabel + ", " + what;
        }
    }

    /// <summary>"Farm is up" / when it last reported, from the server's own clock.</summary>
    public string HostLine => HostOnline ? "The farm is up" : Age is { } age ? "The farm last reported " + FormatAge(age) : "The farm has not reported yet";

    public static string StatusWords(string status) => status switch
    {
        "Pending" => "Still to play",
        "Completed" => "Played",
        "Skipped" => "Sitting out",
        _ => status,
    };

    /// <summary>"Bob", "Bob and Cleo", "Bob, Cleo and 2 more": the launcher's own rule (joinNames in the protocol).</summary>
    public static string JoinNames(IReadOnlyList<string> names, int max = 3)
    {
        if (names.Count == 0) return "nobody";
        if (names.Count == 1) return names[0];
        if (names.Count > max) return string.Join(", ", names.Take(max - 1)) + $" and {names.Count - (max - 1)} more";
        return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }

    public static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromHours(48)) return $"{(int)age.TotalHours} h ago";
        return $"{(int)age.TotalDays} days ago";
    }
}

public enum WakeResult { Woken, AlreadyUp, WrongPassword, PasswordRequired, Throttled, NoPassword, Gone, Failed }

/// <summary>What ModRelay said to a wake, in the player's words. Only WrongPassword keeps the password box open.</summary>
public sealed record WakeAnswer(WakeResult Kind, string Message)
{
    public bool AskAgain => Kind is WakeResult.WrongPassword or WakeResult.PasswordRequired;
}

/// <summary>
/// The hub's third button, "Wake the farm": what it reads, whether it is lit, and why not when
/// it is not. Pure, from what the hub knows: the launcher's link file, the tracker's answer
/// (its `host`, when the server sends one), whether this PC hosts the farm itself, and whether a
/// wake was just sent from here. The owner, 2026-10-08: "can i start the server in game? When i go
/// to use the UI? And ask me for password?".
/// </summary>
public sealed record WakeOutlook(string Label, bool Enabled, string? Why)
{
    public const string WakeLabel = "Wake the farm";
    public const string WakingLabel = "Waking up...";
    public const string UpLabel = "The farm is up";
    public const string WokenWords = "Waking the farm. Join lights up by itself when it is ready, in a few minutes.";
    public const string NoPasswordWords = "Waking the farm from here needs a wake password. The group's owner sets one in ModRelay (Turns, Host).";
    public const string AskingWords = "Asking ModRelay whether the farm can be woken from here...";
    /// <summary>A wake sent from here is believed for this long before the button offers another.</summary>
    public static readonly TimeSpan JustAskedFor = TimeSpan.FromSeconds(90);

    public static WakeOutlook For(HubLink link, HubFarm? farm, bool hostsItself, bool justAsked)
    {
        if (!link.HasFarm) return new(WakeLabel, false, null);
        if (hostsItself) return new(WakeLabel, false, "This PC hosts the farm, so it does not wake it from here: use Host a farm, or ModRelay (Turns, Host).");
        var host = farm?.Host;
        // The tracker's word is the server's own and fresh; the link file is the launcher's copy of it.
        string? state = host?.State ?? link.HostState;
        if (state == "awake") return new(UpLabel, false, null);
        if (state == "starting" || host?.WakePending == true || justAsked) return new(WakingLabel, false, null);
        if (host is null) return new(WakeLabel, false, state is null && link.HostName is null ? "This farm has no host PC in ModRelay to wake." : AskingWords);
        if (!host.WakeFromFarm) return new(WakeLabel, false, NoPasswordWords);
        return new(WakeLabel, true, null);
    }
}

/// <summary>
/// What the hub says while the farm wakes, instead of a bare "Waking up" (the owner, 2026-10-09: "seems
/// to be stuck waking up ... progress is fine"): the host loop's own word on what it is doing, how
/// long it has been at it, and what to expect. The launcher's farm card says the same from the same
/// notes.
/// </summary>
public static class WakeProgress
{
    public const string Usually = "it usually takes 1 to 3 minutes";
    public const string Lights = "Join lights up by itself when it is ready.";
    /// <summary>Before the host has said anything: the wake has been asked for and the loop has not picked it up.</summary>
    public const string Asked = "Waking the host PC";

    /// <summary>The line above the buttons: "Starting the game and loading the farm. 1:42 so far; it usually takes 1 to 3 minutes. Join lights up by itself when it is ready."</summary>
    public static string Line(string? note, TimeSpan? elapsed)
    {
        string what = string.IsNullOrWhiteSpace(note) ? Asked : note!.Trim().TrimEnd('.');
        string time = elapsed is { } t ? $" {Clock(t)} so far; {Usually}." : $" {char.ToUpperInvariant(Usually[0])}{Usually[1..]}.";
        return what + "." + time + " " + Lights;
    }

    /// <summary>The title button's second line: "The farm is waking up, 1:42 so far".</summary>
    public static string Short(TimeSpan? elapsed) => elapsed is { } t ? $"The farm is waking up, {Clock(t)} so far" : "The farm is waking up";

    /// <summary>"0:07", "1:42", "12:05".</summary>
    public static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}


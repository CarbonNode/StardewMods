using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SharedDay.Core;
using StardewModdingAPI;
using StardewValley;

namespace SharedDay.Mod;

/// <summary>
/// Reports the farm's turn state to a ModRelay turn tracker (docs/turn-tracker-contract.md):
/// on every change of who has played, who is online or whether the farm is paused (at most
/// once per 5 seconds), as a heartbeat every 5 minutes, and, while a Relay turn is being played,
/// every half hour of game time, so the clock a player reads before joining is never hours old
/// (<see cref="TrackerCadence"/>). Only the host reports. Posts run in order on a background
/// task; the game never waits for the network.
/// </summary>
internal sealed class TurnTracker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IMonitor monitor;
    private readonly Func<TurnTrackerConfig> config;
    private readonly string modVersion;
    private readonly object gate = new();
    private Task chain = Task.CompletedTask;
    private string? lastState;
    private DateTime lastSent = DateTime.MinValue;
    private int lastClock;
    private long lastSequence;
    private bool failing;
    // What the tracker answered (0.1.29: who is barred, whether new farmers may join), from the
    // background task to the game thread, which drains it with TakeOrders.
    private readonly ConcurrentQueue<TrackerOrders> orders = new();

    public TurnTracker(IMonitor monitor, Func<TurnTrackerConfig> config, string modVersion)
    {
        this.monitor = monitor;
        this.config = config;
        this.modVersion = modVersion;
    }

    private TurnTrackerConfig Config => (TurnTrackerConfig?)config() ?? new TurnTrackerConfig();

    private bool Enabled => Uri.TryCreate(Config.Url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public void Reset() { lastState = null; lastSent = DateTime.MinValue; while (orders.TryDequeue(out _)) { } }

    /// <summary>The orders the tracker has answered with since the last call, oldest first. Call on the game thread.</summary>
    public List<TrackerOrders> TakeOrders()
    {
        var taken = new List<TrackerOrders>();
        while (orders.TryDequeue(out var next)) taken.Add(next);
        return taken;
    }

    /// <summary>Whether a tracker is configured and its farmers' faces are wanted with the reports.</summary>
    public bool WantsPortraits => Enabled && Config.Portraits;

    /// <summary>
    /// Call often (every second is fine); it decides itself whether to send. <paramref name="portrait"/>
    /// gives a farmer's face as a PNG address, or null; a face that changes is a change to report.
    /// </summary>
    public void Update(DaySnapshot day, PlayMode mode, int furthestTime, string? paused, Func<long, string?>? portrait = null, Func<long, (string? Id, string? Name)>? owner = null)
    {
        if (!Enabled) return;
        bool faces = portrait is not null && Config.Portraits;
        var fitted = PortraitWire.Fit(day.Farmers.Select(f => faces ? portrait!(f.Id) : null).ToArray());
        var owners = day.Farmers.Select(f => owner is null || f.IsPlaceholder ? (null, null) : owner(f.Id)).ToArray();
        var farmers = day.Farmers.Select((f, i) => new
        {
            id = f.Id.ToString(),
            // A placeholder is sent under the member's own name: the launcher words "has no farmer yet" itself.
            name = f.MemberName ?? f.Name,
            status = f.Status.ToString(),
            online = f.IsOnline,
            inCurrentTurn = f.IsInCurrentSession,
            // Sitting out only because they have not been seen for days: back in the day the moment they connect.
            away = f.Away,
            // 0.1.37: a group member with no farmer on the farm yet, standing on the roster until they
            // make one or are skipped. The key is only there for a placeholder; `account` is their ModRelay account id.
            placeholder = f.IsPlaceholder ? true : (bool?)null,
            account_id = f.MemberId,
            // 0.1.37: the ModRelay account this farmer belongs to (see Ownership); absent for one nobody owns yet.
            owner_id = owners[i].Item1,
            owner_name = owners[i].Item2,
            portrait = fitted[i]
        }).ToArray();
        // Everything a player cares about except the clock, which moves every few seconds.
        string state = JsonSerializer.Serialize(new { day.AbsoluteDay, mode, day.Phase, day.SessionNumber, paused, farmers });
        var now = DateTime.UtcNow;
        bool changed = state != lastState;
        int clock = DayClock.MinutesGone(Game1.timeOfDay);
        bool turnBeingPlayed = mode == PlayMode.Relay && day.Phase == DayPhase.Open
            && day.Farmers.Any(f => f.IsOnline && f.IsInCurrentSession && f.Status == FarmerStatus.Pending);
        if (!TrackerCadence.Due(changed, now - lastSent, turnBeingPlayed, clock, lastClock)) return;
        lastState = state;
        lastSent = now;
        lastClock = clock;
        long sequence = Math.Max(lastSequence + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        lastSequence = sequence;
        string body = JsonSerializer.Serialize(new
        {
            schema = "shared-day.turns/1",
            sequence,
            sentUtc = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            modVersion,
            farm = new { id = Game1.uniqueIDForThisGame.ToString(), name = Game1.MasterPlayer.farmName.Value },
            date = new
            {
                season = Game1.currentSeason,
                day = Game1.dayOfMonth,
                year = Game1.year,
                absoluteDay = day.AbsoluteDay,
                label = $"{Utility.getSeasonNameFromNumber(Game1.seasonIndex)} {Game1.dayOfMonth}"
            },
            mode = mode.ToString(),
            phase = day.Phase.ToString(),
            turn = day.SessionNumber,
            time = Game1.timeOfDay,
            furthestTime,
            paused,
            farmers
        });
        string url = Config.Url, token = Config.Token;
        lock (gate)
            chain = chain.ContinueWith(_ => Send(url, token, body), TaskScheduler.Default).Unwrap();
    }

    private async Task Send(string url, string token, string body)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            // 409 means the tracker already has a newer report: fine.
            if (!response.IsSuccessStatusCode && (int)response.StatusCode != 409)
                throw new HttpRequestException((int)response.StatusCode == 401
                    ? "the tracker rejected the token (check TurnTracker.Token)"
                    : $"the tracker answered {(int)response.StatusCode}");
            // 204 is "no orders", as every tracker answered until 0.1.29. A 200 may carry some.
            if ((int)response.StatusCode == 200)
            {
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (TrackerOrders.Parse(text) is { } reply) orders.Enqueue(reply);
            }
            if (failing) monitor.Log("Turn tracker reports are being delivered again.", LogLevel.Info);
            failing = false;
        }
        catch (Exception ex)
        {
            // Never log the URL or token: they are credentials for the tracker.
            if (!failing) monitor.Log("Could not report to the turn tracker: " + (ex is TaskCanceledException ? "it did not answer in time" : ex.Message), LogLevel.Warn);
            failing = true;
        }
    }
}

public sealed class TurnTrackerConfig
{
    /// <summary>The tracker URL ModRelay gives you (https://modrelay.app/turns/...). Empty turns reporting off.</summary>
    public string Url { get; set; } = "";

    /// <summary>The secret token ModRelay gives you for that tracker.</summary>
    public string Token { get; set; } = "";

    /// <summary>Send each farmer's face (the game's small portrait) with the reports, so the launcher shows it.</summary>
    public bool Portraits { get; set; } = true;
}

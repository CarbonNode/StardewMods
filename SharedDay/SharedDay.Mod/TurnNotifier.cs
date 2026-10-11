using System.Net.Http;
using System.Text;
using System.Text.Json;
using SharedDay.Core;
using StardewModdingAPI;

namespace SharedDay.Mod;

/// <summary>
/// Posts turn changes to a chat webhook (Discord or Slack), so players in a Relay group
/// learn it is their turn without keeping the game open. Only the host posts. Messages go
/// out in order on a background task; the game never waits for the network.
/// </summary>
internal sealed class TurnNotifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IMonitor monitor;
    private readonly Func<NotificationConfig> config;
    private readonly object gate = new();
    private Task chain = Task.CompletedTask;
    private bool failing;
    private string? lastPausedMessage;

    public TurnNotifier(IMonitor monitor, Func<NotificationConfig> config)
    {
        this.monitor = monitor;
        this.config = config;
    }

    // A hand-edited config.json can set the section to null.
    private NotificationConfig Config => (NotificationConfig?)config() ?? new NotificationConfig();

    private bool Enabled => Uri.TryCreate(Config.WebhookUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>A farmer's name, or the mention configured for them (such as Discord's &lt;@id&gt;).</summary>
    public string Mention(string name) =>
        (Config.Mentions ?? new()).FirstOrDefault(m => string.Equals(m.Key, name, StringComparison.OrdinalIgnoreCase)).Value is { Length: > 0 } mention
            ? mention : name;

    public void Finished(string farm, string date, string name, PlayMode mode, IReadOnlyList<string> stillToPlay)
    {
        string text = $"{name} finished {date} on {farm} Farm.";
        if (stillToPlay.Count == 0) text += " Everyone has played. The next day starts once the farm saves.";
        else if (mode == PlayMode.Together) text += $" Waiting for {List(stillToPlay.Select(Mention))}. In Together mode everyone has to be online to end the day.";
        else text += $" Still to play: {List(stillToPlay.Select(Mention))}.";
        Post(text);
    }

    public void Skipped(string farm, string date, string name) => Post($"{name} will sit out {date} on {farm} Farm.");

    public void Joined(string farm, string date, string name) => Post($"{Mention(name)} joined {farm} Farm and is playing {date}.");

    public void NewDay(string farm, string date, IReadOnlyList<string> players)
    {
        lastPausedMessage = null;
        Post(players.Count == 0
            ? $"{date} has started on {farm} Farm."
            : $"{date} has started on {farm} Farm. Everyone can play: {List(players.Select(Mention))}.");
    }

    public void Paused(string farm, string message)
    {
        // A held farm repeats its error on every status refresh; post each new one once.
        if (message == lastPausedMessage) return;
        lastPausedMessage = message;
        Post($"Shared Day has paused {farm} Farm to protect the save. {message}");
    }

    public void Reset() => lastPausedMessage = null;

    private static string List(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }

    private void Post(string text)
    {
        if (!Enabled) return;
        string url = Config.WebhookUrl;
        // Discord reads "content" and Slack reads "text"; each ignores the other field.
        string body = JsonSerializer.Serialize(new { content = text, text, username = "Shared Day" });
        lock (gate)
            chain = chain.ContinueWith(_ => Send(url, body), TaskScheduler.Default).Unwrap();
    }

    private async Task Send(string url, string body)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(url, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"the webhook answered {(int)response.StatusCode}");
            if (failing) monitor.Log("Turn notifications are being delivered again.", LogLevel.Info);
            failing = false;
        }
        catch (Exception ex)
        {
            // Never log the URL: a webhook URL is a credential for the channel.
            if (!failing) monitor.Log("Could not post a turn notification: " + (ex is TaskCanceledException ? "the webhook did not answer in time" : ex.Message), LogLevel.Warn);
            failing = true;
        }
    }
}

public sealed class NotificationConfig
{
    /// <summary>A Discord or Slack incoming-webhook URL. Empty turns notifications off.</summary>
    public string WebhookUrl { get; set; } = "";

    /// <summary>Optional farmer name → mention text, such as "&lt;@123456789012345678&gt;" for a Discord user.</summary>
    public Dictionary<string, string> Mentions { get; set; } = new();
}

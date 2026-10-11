using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharedDay.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace SharedDay.Mod;

/// <summary>
/// The title screen's way into everything Shared Day and ModRelay know about the player's
/// farm: which group and farm it is, what day it is there and who the farm is waiting on
/// (from the farm's ModRelay turn tracker), whether the farm is up, and one press to join it
/// or to host a farm. The title button (<see cref="TitleJoinButton"/>) is its door.
///
/// It learns the farm from the launcher's link file and from config.json (<see cref="HubLink"/>),
/// re-read every two seconds so a host waking up while the game sits on the title screen
/// shows without a restart. The tracker is asked in the background; the game never waits
/// for the network, and nothing here runs once a farm is loaded.
/// </summary>
public sealed class TitleHub
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan LinkEvery = TimeSpan.FromSeconds(2);
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly Func<ModConfig> config;
    private readonly object gate = new();
    private HubLink link = HubLink.Empty;
    private HubLink? lastFile;
    private volatile HubFarm? farm;
    private volatile string? farmError;
    private volatile string? askedUrl;
    /// <summary>When the tracker's last answer arrived, on this PC's clock: its report has aged since.</summary>
    private DateTime farmAt;
    /// <summary>Counts farms asked about, so an answer for an earlier farm is never shown for the next one.</summary>
    private int generation;
    private DateTime nextLink = DateTime.MinValue, nextAsk = DateTime.MinValue;
    private int asking;
    private bool loggedFile;

    /// <summary>When a wake was last sent from here and taken; the hub says "Waking up..." for a while after.</summary>
    private DateTime wokeAt = DateTime.MinValue;
    /// <summary>When this hub first saw the farm waking (a wake pending, or the host starting), for the clock it shows; MinValue while it is not.</summary>
    private DateTime wakingSince = DateTime.MinValue;
    private HubMenu? open;

    /// <summary>The title button; it owns the game's join flow, which the hub reuses.</summary>
    public TitleJoinButton Button { get; set; } = null!;
    public HubLink Link => link;
    public HubFarm? Farm => farm;
    /// <summary>Where a wake for this farm is posted (the tracker's address plus /wake), or null without a tracker.</summary>
    public string? WakeUrl => link.TrackerUrl is { } url ? HubLink.WakeFor(url) : null;
    /// <summary>A wake sent from here was taken less than a minute and a half ago.</summary>
    public bool JustWoke => DateTime.UtcNow - wokeAt < WakeOutlook.JustAskedFor;
    /// <summary>The hub's third button: whether the farm can be woken from here, and what to say if not.</summary>
    public WakeOutlook Wakeable() => WakeOutlook.For(link, farm, HostsItself, JustWoke);
    /// <summary>The farm is waking (a wake pending, the host starting, or a wake just sent from here), as the hub and the title button read it.</summary>
    public bool Waking => Wakeable().Label == WakeOutlook.WakingLabel;
    /// <summary>How long the farm has been waking, from the first moment this hub saw it; null while it is not.</summary>
    public TimeSpan? WakingFor => Waking && wakingSince != DateTime.MinValue ? DateTime.UtcNow - wakingSince : null;
    /// <summary>The line above the buttons while the farm wakes: the host's word, the clock and what to expect.</summary>
    public string WakingLine() => WakeProgress.Line(farm?.Host?.Note, WakingFor);
    /// <summary>Why the farm's state is not showing, in a player's words. Null while it is.</summary>
    public string? FarmError => farmError;
    /// <summary>This PC is the farm's host: it loads the farm itself, so joining was never its way in.</summary>
    public bool HostsItself => config().AutoHost?.Enabled == true;
    public bool MenuOpen => TitleMenu.subMenu is HubMenu;
    /// <summary>Where the link file was found, for the log and the tests.</summary>
    public string? LinkFilePath { get; private set; }

    /// <summary>
    /// What joining the farm right now would mean for this player: nothing to say, "you carry on
    /// in your own turn", or a warning that their day would start part way through (<see cref="MiddayJoin"/>).
    /// </summary>
    public JoinAdvice Advice()
    {
        HubFarm? known;
        DateTime at;
        lock (gate) { known = farm; at = farmAt; }
        // The report was this old when the tracker answered, and has aged here since.
        TimeSpan? age = known?.Age is { } then ? then + (DateTime.UtcNow - at) : null;
        return MiddayJoin.Before(known, link.Player, HostsItself, age);
    }

    public TitleHub(IModHelper helper, IMonitor monitor, Func<ModConfig> config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
    }

    public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        // The title's own Back button closes the hub without telling it: a password box it left
        // selected would go on eating the keyboard, so it is let go of here.
        if (open is { } was && TitleMenu.subMenu != was)
        {
            open = null;
            was.Dismissed();
        }
        if (Context.IsWorldReady || Game1.activeClickableMenu is not TitleMenu) return;
        var now = DateTime.UtcNow;
        // The clock starts the moment the farm is first seen waking, wherever the wake came from, and stops when it is not.
        if (Waking) { if (wakingSince == DateTime.MinValue) wakingSince = wokeAt != DateTime.MinValue && now - wokeAt < WakeOutlook.JustAskedFor ? wokeAt : now; }
        else wakingSince = DateTime.MinValue;
        if (now >= nextLink)
        {
            nextLink = now + LinkEvery;
            link = Resolve();
            if (link.TrackerUrl != askedUrl)
            {
                // A different farm (or none): what was known about the old one is not shown for it.
                lock (gate)
                {
                    generation++;
                    askedUrl = link.TrackerUrl;
                    farm = null;
                    farmError = null;
                }
                nextAsk = now;
            }
        }
        if (askedUrl is { } url && now >= nextAsk && Interlocked.CompareExchange(ref asking, 1, 0) == 0)
        {
            nextAsk = now + TimeSpan.FromSeconds(MenuOpen ? 10 : 20);
            _ = Ask(url, generation);
        }
    }

    /// <summary>Config over the launcher's link file. The file sits beside the Mods folder, so it is looked for up the tree from this mod.</summary>
    private HubLink Resolve()
    {
        try
        {
            HubLink? file = null;
            string? foundAt = null;
            var dir = new DirectoryInfo(helper.DirectoryPath);
            for (int up = 0; up < 6 && dir is not null && file is null; up++)
            {
                string path = Path.Combine(dir.FullName, HubLink.FileName);
                if (File.Exists(path) && new FileInfo(path).Length <= 64 * 1024)
                {
                    file = HubLink.ParseFile(File.ReadAllText(path));
                    if (file is not null) foundAt = path;
                }
                // On macOS and Linux the launcher links the game's Mods folder to the loadout's: the
                // file is beside the real folder, so a link is followed to where it points.
                dir = dir.LinkTarget is not null && dir.ResolveLinkTarget(returnFinalTarget: true) is DirectoryInfo real ? real.Parent : dir.Parent;
            }
            // Only a look that finished replaces what was known: no file is an answer, a failed read is not.
            lastFile = file;
            LinkFilePath = foundAt;
            if (file is not null && !loggedFile)
            {
                loggedFile = true;
                monitor.Log($"The ModRelay launcher linked this game to {file.GroupName ?? "no group yet"}.", LogLevel.Info);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            // Mid-write by the launcher, a virus scanner, or a link that points nowhere: the last
            // good read stands, and the next look is two seconds away.
        }
        var c = config();
        return HubLink.Merge(lastFile, c.JoinServer?.Label, c.JoinServer?.Address, c.JoinServer?.InviteCode, c.TurnTracker?.Url);
    }

    private async Task Ask(string url, int asked)
    {
        HubFarm? parsed = null;
        string? error = null;
        bool gone = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            parsed = response.IsSuccessStatusCode ? HubFarm.Parse(body) : null;
            gone = (int)response.StatusCode is 404 or 410;
            if (parsed is null)
                error = gone
                    ? "ModRelay has no tracker for this farm any more. The host can make a new one in ModRelay."
                    : "ModRelay did not answer about the farm. This keeps trying.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Never log the URL: a tracker's address is how its followers find it.
            error = "Could not reach ModRelay. Check the internet connection; this keeps trying.";
        }
        catch (Exception ex)
        {
            // Anything else must not leave the hub "asking" for ever with nothing said.
            error = "Could not read ModRelay's answer about the farm. This keeps trying.";
            monitor.Log("The title-screen hub could not read the farm's state: " + ex.GetType().Name, LogLevel.Trace);
        }
        finally
        {
            lock (gate)
            {
                // Only the farm that was asked about is updated: the launcher may have linked another meanwhile.
                if (asked == generation)
                {
                    if (parsed is not null) { farm = parsed; farmAt = DateTime.UtcNow; }
                    // A tracker that is gone has no state worth showing; a missed answer keeps the last one.
                    else if (gone) farm = null;
                    farmError = parsed is not null ? null : error;
                }
            }
            Interlocked.Exchange(ref asking, 0);
        }
    }

    /// <summary>Open the hub over the title screen, as one of the title's own sub-menus.</summary>
    public bool Open(TitleMenu title)
    {
        if (TitleMenu.subMenu is not null) return false;
        Game1.playSound("bigSelect");
        nextAsk = DateTime.MinValue;
        open = new HubMenu(this, helper, monitor);
        title.ForceSubmenu(open);
        return true;
    }

    /// <summary>Ask the tracker again at the next tick instead of waiting out the interval.</summary>
    public void AskSoon() => nextAsk = DateTime.MinValue;

    /// <summary>
    /// Wake the farm's host PC through ModRelay with the wake password (POST on the tracker's
    /// /wake). The game speaks for no account, so the password is the whole key; ModRelay's
    /// answer comes back in the player's words. Never throws, and never logs the password or the
    /// address.
    /// </summary>
    public async Task<WakeAnswer> Wake(string password)
    {
        if (WakeUrl is not { } url) return new WakeAnswer(WakeResult.Gone, "This farm has no tracker to wake it through.");
        WakeAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { password }), Encoding.UTF8, "application/json"),
            };
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            answer = HubFarm.ParseWakeAnswer((int)response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            answer = new WakeAnswer(WakeResult.Failed, "Could not reach ModRelay. Check the internet connection and try again.");
        }
        catch (Exception ex)
        {
            monitor.Log("The wake could not be sent: " + ex.GetType().Name, LogLevel.Trace);
            answer = new WakeAnswer(WakeResult.Failed, "Could not send the wake. Try again in a moment.");
        }
        if (answer.Kind == WakeResult.Woken) wokeAt = DateTime.UtcNow;
        monitor.Log("Wake the farm: " + answer.Kind + ".", LogLevel.Info);
        // The tracker is asked again at once, so the hub reads "starting" as soon as the host does.
        if (answer.Kind is WakeResult.Woken or WakeResult.AlreadyUp) AskSoon();
        return answer;
    }
}

/// <summary>
/// The hub itself: the farm's name, its date and who it is waiting on, every farmer's turn,
/// whether the farm is up, and three actions (join the farm, host a farm, wake the farm). A
/// sub-menu of the title screen, so the title's own Back button and Escape close it.
///
/// Wake the farm asks for the host's wake password in a box of its own and sends it to ModRelay
/// (the owner, 2026-10-08: "can i start the server in game? When i go to use the UI? And ask me for
/// password?"). The farm's web page is not opened any more: the hub already shows what it shows,
/// and a button that shells out to a browser from a game is a surprise ("turn page on popup
/// opens browser???").
/// </summary>
public sealed class HubMenu : IClickableMenu
{
    private const int JoinId = 7381101, HostId = 7381102, WakeId = 7381103, WaitId = 7381104, JoinNowId = 7381105, CancelWakeId = 7381106, WakeNowId = 7381107;
    private static readonly Rectangle Frame = new(384, 396, 15, 15);
    private static readonly Rectangle ButtonFrame = new(432, 439, 9, 9);
    private static readonly Color Green = new(40, 110, 40);
    private readonly TitleHub hub;
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    // Public so the game's controller snapping finds them.
    public ClickableComponent joinButton = new(Rectangle.Empty, "SharedDay.Hub.Join") { myID = JoinId, rightNeighborID = HostId };
    public ClickableComponent hostButton = new(Rectangle.Empty, "SharedDay.Hub.Host") { myID = HostId, leftNeighborID = JoinId, rightNeighborID = WakeId };
    public ClickableComponent wakeButton = new(Rectangle.Empty, "SharedDay.Hub.Wake") { myID = WakeId, leftNeighborID = HostId };
    // The two answers to "join part way through the day?". Wait is first: it is the one that costs nothing.
    public ClickableComponent waitButton = new(Rectangle.Empty, "SharedDay.Hub.Wait") { myID = WaitId, rightNeighborID = JoinNowId };
    public ClickableComponent joinNowButton = new(Rectangle.Empty, "SharedDay.Hub.JoinNow") { myID = JoinNowId, leftNeighborID = WaitId };
    // The password box's two buttons. Cancel is first: it is the one that costs nothing.
    public ClickableComponent cancelWakeButton = new(Rectangle.Empty, "SharedDay.Hub.CancelWake") { myID = CancelWakeId, rightNeighborID = WakeNowId };
    public ClickableComponent wakeNowButton = new(Rectangle.Empty, "SharedDay.Hub.WakeNow") { myID = WakeNowId, leftNeighborID = CancelWakeId };
    /// <summary>The warning being asked about, while the player has not answered it. Null otherwise.</summary>
    private string? asking;
    /// <summary>The wake password is being asked for.</summary>
    private bool askingWake;
    private TextBox? passwordBox;
    /// <summary>1 while a wake is on its way to ModRelay and its answer is not back.</summary>
    private int wakeBusy;
    /// <summary>ModRelay's answer to the last wake, until the game thread has shown it.</summary>
    private volatile WakeAnswer? wakeAnswer;
    private ClickableComponent? hovered;
    private bool laidOutForFarm;
    private string? notice;
    private DateTime noticeUntil;

    /// <summary>What the menu last drew as its lines of text, top to bottom, for the tests.</summary>
    public IReadOnlyList<string> Lines { get; private set; } = Array.Empty<string>();
    public Rectangle JoinBounds => joinButton.bounds;
    public Rectangle HostBounds => hostButton.bounds;
    public Rectangle WakeBounds => wakeButton.bounds;
    public bool CanJoin => hub.Link.CanJoin;
    /// <summary>Wake the farm is lit: the host is asleep or off, and ModRelay offers a wake from the game.</summary>
    public bool CanWake => hub.Wakeable().Enabled;
    /// <summary>The mid-day warning the menu is waiting for an answer to, or null. For the tests.</summary>
    public string? Asking => asking;
    /// <summary>The wake password box is open. For the tests.</summary>
    public bool AskingWake => askingWake;
    /// <summary>ModRelay's answer to the last wake sent from this menu, once it is back. For the tests.</summary>
    public WakeAnswer? LastWakeAnswer { get; private set; }
    public Rectangle WaitBounds => waitButton.bounds;
    public Rectangle JoinNowBounds => joinNowButton.bounds;
    public Rectangle CancelWakeBounds => cancelWakeButton.bounds;
    public Rectangle WakeNowBounds => wakeNowButton.bounds;
    /// <summary>Where the password box sits, while it is open. For the tests.</summary>
    public Rectangle PasswordBounds => passwordBox is { } box ? new Rectangle(box.X, box.Y, box.Width, box.Height) : Rectangle.Empty;

    public HubMenu(TitleHub hub, IModHelper helper, IMonitor monitor)
    {
        this.hub = hub;
        this.helper = helper;
        this.monitor = monitor;
        Layout();
        if (Game1.options.SnappyMenus)
        {
            populateClickableComponentList();
            snapToDefaultClickableComponent();
        }
    }

    private void Layout()
    {
        var view = Game1.uiViewport;
        width = Math.Min(960, view.Width - 64);
        // As tall as what it has to say: the farmer list needs room, a line or two does not, and
        // the password box has a question, two lines, the box and its answer (no empty middle).
        laidOutForFarm = hub.Link.HasFarm;
        height = Math.Min(askingWake ? 440 : laidOutForFarm ? 660 : 400, view.Height - 96);
        xPositionOnScreen = (view.Width - width) / 2;
        yPositionOnScreen = Math.Max(16, (view.Height - height) / 2 - 16);
        // Three buttons of one size along the bottom edge.
        int pad = 40, gap = 16, h = 72;
        int w = (width - pad * 2 - gap * 2) / 3;
        int y = yPositionOnScreen + height - pad - h;
        joinButton.bounds = new Rectangle(xPositionOnScreen + pad, y, w, h);
        hostButton.bounds = new Rectangle(xPositionOnScreen + pad + w + gap, y, w, h);
        wakeButton.bounds = new Rectangle(xPositionOnScreen + pad + (w + gap) * 2, y, w, h);
        // The warning's two answers share the same row, a half each; so do the password box's.
        int half = (width - pad * 2 - gap) / 2;
        waitButton.bounds = new Rectangle(xPositionOnScreen + pad, y, half, h);
        joinNowButton.bounds = new Rectangle(xPositionOnScreen + pad + half + gap, y, half, h);
        cancelWakeButton.bounds = waitButton.bounds;
        wakeNowButton.bounds = joinNowButton.bounds;
        if (passwordBox is { } box)
        {
            // As wide as the text; it sits under its explanation, which draw() places once the words have wrapped.
            box.X = xPositionOnScreen + pad;
            box.Width = width - pad * 2;
        }
        // A controller's cursor follows the button it was on.
        if (Game1.options.SnappyMenus && currentlySnappedComponent is not null) snapCursorToCurrentSnappedComponent();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();

    public override void snapToDefaultClickableComponent()
    {
        currentlySnappedComponent = getComponentWithID(askingWake ? CancelWakeId : asking is not null ? WaitId : CanJoin ? JoinId : CanWake ? WakeId : HostId);
        snapCursorToCurrentSnappedComponent();
    }

    /// <summary>
    /// While a question is open (the mid-day warning, the wake password), the title's own Back,
    /// Escape and B do not close the hub: backing out of the question is the answer, and the hub
    /// stays. <see cref="Close"/> is the hub's own way out and ignores this.
    /// </summary>
    public override bool readyToClose() => asking is null && !askingWake;

    public override void performHoverAction(int x, int y)
    {
        var now = (askingWake ? new[] { cancelWakeButton, wakeNowButton } : asking is not null ? new[] { waitButton, joinNowButton } : new[] { joinButton, hostButton, wakeButton })
            .FirstOrDefault(c => c.containsPoint(x, y) && Enabled(c));
        if (now is not null && now != hovered) Game1.playSound("Cowboy_Footstep");
        hovered = now;
    }

    private bool Enabled(ClickableComponent c) => c == joinButton ? CanJoin : c == wakeButton ? CanWake : c == wakeNowButton ? wakeBusy == 0 : true;

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (askingWake)
        {
            if (cancelWakeButton.containsPoint(x, y)) CancelWake();
            else if (wakeNowButton.containsPoint(x, y)) WakeNow();
            else if (passwordBox is { } box && new Rectangle(box.X, box.Y, box.Width, box.Height).Contains(x, y)) box.SelectMe();
            return;
        }
        if (asking is not null)
        {
            if (waitButton.containsPoint(x, y)) Wait();
            else if (joinNowButton.containsPoint(x, y)) JoinNow();
            return;
        }
        if (joinButton.containsPoint(x, y)) Join();
        else if (hostButton.containsPoint(x, y)) Host();
        else if (wakeButton.containsPoint(x, y)) Wake();
    }

    public override void update(GameTime time)
    {
        base.update(time);
        Pump();
    }

    /// <summary>ModRelay's answer to a wake arrives on another thread; the menu shows it on the game's.</summary>
    private void Pump()
    {
        if (wakeAnswer is not { } answer) return;
        wakeAnswer = null;
        LastWakeAnswer = answer;
        if (answer.AskAgain)
        {
            // The box stays open for another go, empty.
            if (passwordBox is { } box) { box.Text = ""; box.SelectMe(); }
            Say(answer.Message);
            return;
        }
        CloseWakeBox();
        if (answer.Kind is WakeResult.Woken or WakeResult.AlreadyUp)
        {
            Game1.playSound("bigSelect");
            notice = answer.Message;
            noticeUntil = DateTime.UtcNow.AddSeconds(8);
        }
        else Say(answer.Message);
        if (Game1.options.SnappyMenus) snapToDefaultClickableComponent();
    }

    /// <summary>
    /// Join the farm through the game's own join flow. False (with the reason shown) when there is
    /// no way in yet, and false with a question on screen when joining now would start this
    /// player's day part way through: "i think it should warn about a midday join (you are
    /// sacrificing time x amount of time so far)" (the owner, 2026-10-07). The question's "Join now"
    /// comes back here with <paramref name="warned"/> set.
    /// </summary>
    public bool Join(bool warned = false)
    {
        if (Game1.activeClickableMenu is not TitleMenu title) return false;
        if (!CanJoin) return Say(WhyNoJoin());
        if (!warned && hub.Advice() is { Kind: JoinOutlook.Warn } advice)
        {
            Game1.playSound("smallSelect");
            asking = advice.Text;
            monitor.Log("Before joining, Shared Day said: " + advice.Text, LogLevel.Info);
            if (Game1.options.SnappyMenus) snapToDefaultClickableComponent();
            return false;
        }
        asking = null;
        // The hub is already the title's sub-menu: the next menu takes its place, with no second slide-in.
        if (hub.Button.Join(title, replaceSubmenu: true)) return true;
        return Say(hub.Button.Notice ?? "Couldn't start joining.");
    }

    /// <summary>"Wait": the warning goes away and the player is still on the title screen, in the hub, having joined nothing.</summary>
    public void Wait()
    {
        if (asking is null) return;
        asking = null;
        Game1.playSound("bigDeSelect");
        monitor.Log("The player chose to wait.", LogLevel.Info);
        if (Game1.options.SnappyMenus) snapToDefaultClickableComponent();
    }

    /// <summary>"Join now": the player has read what it costs and joins anyway.</summary>
    public bool JoinNow()
    {
        if (asking is null) return false;
        monitor.Log("The player chose to join now.", LogLevel.Info);
        return Join(warned: true);
    }

    /// <summary>The game's own Co-op menu on its Host tab: the player picks the farm, as always.</summary>
    public bool Host()
    {
        if (Game1.activeClickableMenu is not TitleMenu title) return false;
        Game1.playSound("bigSelect");
        // The hub is already the title's sub-menu: the Co-op menu takes its place, with no second slide-in.
        TitleMenu.subMenu = new CoopMenu(tooManyFarms: false, splitScreen: false, CoopMenu.Tab.HOST_TAB);
        return true;
    }

    /// <summary>
    /// Wake the farm: opens the password box. False, with the reason shown, when the farm cannot
    /// be woken from here right now (it is up or already waking, this PC hosts it, or ModRelay has
    /// no wake password for it).
    /// </summary>
    public bool Wake()
    {
        if (Game1.activeClickableMenu is not TitleMenu) return false;
        var outlook = hub.Wakeable();
        if (!outlook.Enabled) return Say(outlook.Why ?? (outlook.Label == WakeOutlook.UpLabel ? "The farm is already up." : outlook.Label == WakeOutlook.WakingLabel ? "The farm is already waking up." : WhyNoJoin()));
        Game1.playSound("smallSelect");
        if (passwordBox is null)
        {
            passwordBox = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), null, Game1.smallFont, Game1.textColor)
            {
                PasswordBox = true,
                textLimit = 64,
                limitWidth = false,
            };
            passwordBox.OnEnterPressed += _ => WakeNow();
        }
        passwordBox.Text = "";
        askingWake = true;
        Layout();
        passwordBox.SelectMe();
        monitor.Log("Wake the farm: asking for the wake password.", LogLevel.Info);
        if (Game1.options.SnappyMenus) snapToDefaultClickableComponent();
        return true;
    }

    /// <summary>What is in the password box. For the tests, which have no keyboard.</summary>
    public void TypePassword(string password)
    {
        if (passwordBox is { } box) box.Text = password;
    }

    /// <summary>"Cancel": the box goes away and the player is still in the hub, having sent nothing.</summary>
    public void CancelWake()
    {
        if (!askingWake) return;
        CloseWakeBox();
        Game1.playSound("bigDeSelect");
        monitor.Log("Wake the farm: cancelled.", LogLevel.Info);
        if (Game1.options.SnappyMenus) snapToDefaultClickableComponent();
    }

    /// <summary>"Wake": sends what was typed to ModRelay. False when nothing was typed or a wake is already on its way.</summary>
    public bool WakeNow()
    {
        if (!askingWake || passwordBox is not { } box) return false;
        if (wakeBusy != 0) return false;
        string password = box.Text ?? "";
        if (password.Length == 0) return Say("Type the farm's wake password.");
        if (Interlocked.CompareExchange(ref wakeBusy, 1, 0) != 0) return false;
        Game1.playSound("smallSelect");
        notice = "Asking ModRelay to wake the farm...";
        noticeUntil = DateTime.UtcNow.AddSeconds(15);
        _ = SendWake(password);
        return true;
    }

    private async Task SendWake(string password)
    {
        try { wakeAnswer = await hub.Wake(password).ConfigureAwait(false); }
        finally { Interlocked.Exchange(ref wakeBusy, 0); }
    }

    private void CloseWakeBox()
    {
        bool was = askingWake;
        askingWake = false;
        if (passwordBox is { } box)
        {
            box.Text = "";
            // Lets go of the keyboard (Game1.keyboardDispatcher) when this box held it.
            box.Selected = false;
        }
        // Back to the hub's own height.
        if (was) Layout();
    }

    /// <summary>The title closed the hub itself (its Back button): the keyboard must not stay with the password box.</summary>
    public void Dismissed() => CloseWakeBox();

    private bool Say(string message)
    {
        Game1.playSound("cancel");
        notice = message;
        noticeUntil = DateTime.UtcNow.AddSeconds(6);
        return false;
    }

    /// <summary>Why there is no way into the farm right now, and what changes that.</summary>
    public string WhyNoJoin()
    {
        if (hub.HostsItself)
            // The owner, 2026-10-06, on a host install: "why cant i rejoin?". The old answer told him to set
            // JoinServer, which is the one thing a host must NOT do - it would join its own farm.
            return hub.Link.HostState == "awake"
                ? "This PC hosts the farm, so it does not join. The farm is already up."
                : "This PC hosts the farm, so it does not join - use Host a farm, or wake it from ModRelay (Turns).";
        var wake = hub.Wakeable();
        // The tracker's word on the host is fresher than the launcher's link file; the wake button reads it too.
        string? state = hub.Farm?.Host?.State ?? hub.Link.HostState;
        return state switch
        {
            "asleep" or "offline" when wake.Enabled => (state == "asleep" ? "The farm is asleep. " : "The host PC has not checked in lately. ") + "Wake the farm below, and Join lights up by itself when it is ready.",
            "asleep" or "offline" when wake.Why == WakeOutlook.NoPasswordWords => (state == "asleep" ? "The farm is asleep. " : "The host PC has not checked in lately. ") + WakeOutlook.NoPasswordWords,
            "asleep" or "offline" when wake.Label == WakeOutlook.WakingLabel => hub.WakingLine(),
            "asleep" => "The farm is asleep. Wake it from ModRelay (Turns, Host) and this lights up by itself.",
            "starting" => hub.WakingLine(),
            "offline" => "The host PC is off or not connected. Ask whoever looks after it.",
            "awake" => "The farm is up, but its host has not shared an address yet.",
            _ => hub.Link.GroupName is not null
                ? "No address for this farm yet. Whoever hosts shares it in ModRelay (Turns, Host)."
                : "Join a group in ModRelay (Turns), or set JoinServer in Shared Day's config.json.",
        };
    }

    public override void receiveKeyPress(Keys key)
    {
        if (askingWake)
        {
            // The password box has the keyboard; only Escape means anything here, and it is Cancel.
            if (key == Keys.Escape) CancelWake();
            return;
        }
        if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
        {
            // Backing out of the question is the safe answer, not a way out of the hub.
            if (asking is not null) Wait();
            else Close();
            return;
        }
        if (Game1.options.SnappyMenus) applyMovementKey(key);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (button != Buttons.B) return;
        if (askingWake) CancelWake();
        else if (asking is not null) Wait();
        else Close();
    }

    /// <summary>Back to the title's buttons, the way its own Back button goes.</summary>
    public void Close()
    {
        if (TitleMenu.subMenu != this || Game1.activeClickableMenu is not TitleMenu title) return;
        CloseWakeBox();
        asking = null;
        var back = helper.Reflection.GetMethod(title, "backButtonPressed", required: false);
        // The title's own way back plays its own sound.
        if (back is not null) back.Invoke();
        else
        {
            Game1.playSound("bigDeSelect");
            TitleMenu.subMenu = null;
        }
    }

    public override void draw(SpriteBatch b)
    {
        var link = hub.Link;
        var farm = hub.Farm;
        // The launcher can link a farm (or take one away) while the hub is open.
        if (link.HasFarm != laidOutForFarm) Layout();
        var lines = new List<string>();
        int x = xPositionOnScreen, y = yPositionOnScreen, pad = 40, inner = width - pad * 2;
        drawTextureBox(b, Game1.mouseCursors, Frame, x, y, width, height, Color.White, 4f, drawShadow: true);
        Pump();
        if (askingWake && passwordBox is { } box)
        {
            // The whole box is the password question: what it is for, the box, and two answers.
            int qy = y + 32;
            qy += Text(b, lines, Fit("Wake the farm?", Game1.dialogueFont, 1f, inner), Game1.dialogueFont, x + pad, qy, Game1.textColor, 1f);
            qy += 10;
            b.Draw(Game1.staminaRect, new Rectangle(x + pad, qy, inner, 3), Game1.textColor * 0.25f);
            qy += 16;
            string? host = hub.Farm?.Host?.Name ?? link.HostName;
            qy += Wrapped(b, lines, (host is null ? "This starts the farm on its host PC. " : $"This starts the farm on {host}. ")
                + "Type the farm's wake password; the group's owner set it in ModRelay (Turns, Host).", x + pad, qy, inner, Game1.textColor);
            // The box goes under the words, however many lines they took, and never over the buttons.
            box.Y = Math.Min(qy + 12, cancelWakeButton.bounds.Y - box.Height - 72);
            box.Draw(b);
            lines.Add("[password box]");
            // What ModRelay said about the last try, right under the box.
            string? hintNow = notice is not null && DateTime.UtcNow < noticeUntil ? notice : null;
            if (hintNow is not null) HintLines(b, lines, hintNow, x + pad, box.Y + box.Height + 12, inner);
            Button(b, lines, cancelWakeButton, "Cancel", true);
            Button(b, lines, wakeNowButton, wakeBusy != 0 ? "Sending..." : "Wake", wakeBusy == 0);
            Lines = lines;
            return;
        }
        if (asking is { } question)
        {
            // The whole box is the question: nothing else to read, and two answers.
            int qy = y + 32;
            qy += Text(b, lines, Fit("Join part way through the day?", Game1.dialogueFont, 1f, inner), Game1.dialogueFont, x + pad, qy, Game1.textColor, 1f);
            qy += 10;
            b.Draw(Game1.staminaRect, new Rectangle(x + pad, qy, inner, 3), Game1.textColor * 0.25f);
            qy += 16;
            Wrapped(b, lines, question, x + pad, qy, inner, Game1.textColor);
            Button(b, lines, waitButton, JoinAdvice.WaitAnswer, true);
            Button(b, lines, joinNowButton, JoinAdvice.JoinAnswer, true);
            Lines = lines;
            return;
        }

        // Who and where.
        string name = link.Name ?? farm?.FarmName ?? "Shared Day";
        int cy = y + 32;
        cy += Text(b, lines, Fit(name, Game1.dialogueFont, 1f, inner), Game1.dialogueFont, x + pad, cy, Game1.textColor, 1f);
        var about = new List<string>();
        if (link.GroupName is not null && link.GroupName != name) about.Add("Group " + link.GroupName);
        if (farm?.FarmName is not null && farm.FarmName != name) about.Add(FarmWords.Titled(farm.FarmName));
        if (link.HostName is not null) about.Add("Hosted on " + link.HostName);
        if (link.Player is not null) about.Add("You are " + link.Player);
        if (about.Count > 0) cy += Text(b, lines, Fit(string.Join("  |  ", about), Game1.smallFont, 1f, inner), Game1.smallFont, x + pad, cy, Game1.textColor * 0.7f, 1f);
        cy += 10;
        b.Draw(Game1.staminaRect, new Rectangle(x + pad, cy, inner, 3), Game1.textColor * 0.25f);
        cy += 16;

        // Above the buttons: two lines for the hint (why Join is unlit, or a notice).
        int floor = joinButton.bounds.Y - 64;
        if (!link.HasFarm)
        {
            cy += Wrapped(b, lines, link.GroupName is not null
                ? "This group has no farm on its turn tracker yet. Whoever hosts makes one in ModRelay (Turns, Set up)."
                : "No farm is linked to this game yet. Join a group for this pack in ModRelay (Turns), or host a farm from here.",
                x + pad, cy, inner, Game1.textColor * 0.8f);
        }
        else if (farm is null)
        {
            cy += Wrapped(b, lines, hub.FarmError ?? "Asking ModRelay about the farm...", x + pad, cy, inner, Game1.textColor * 0.8f);
        }
        else
        {
            if (farm.DateLabel is not null)
            {
                string date = farm.Year is { } year ? $"{farm.DateLabel}, Year {year}" : farm.DateLabel;
                string mode = farm.Mode switch { "Relay" => "Taking turns", "Together" => "Playing together", { } other => other, _ => "" };
                Text(b, lines, date, Game1.dialogueFont, x + pad, cy, Game1.textColor, 0.85f);
                var size = Game1.smallFont.MeasureString(mode);
                if (mode.Length > 0) Text(b, lines, mode, Game1.smallFont, x + pad + inner - (int)size.X, cy + 10, Game1.textColor * 0.7f, 1f);
                cy += (int)(Game1.dialogueFont.MeasureString(date).Y * 0.85f) + 2;
            }
            cy += Wrapped(b, lines, farm.Headline, x + pad, cy, inner, farm.Paused is not null ? new Color(150, 40, 30) : Game1.textColor);
            // The reason can be a paragraph; the hub shows its start and the turn page has the rest.
            if (farm.Paused is not null) cy += Wrapped(b, lines, farm.Paused.Length > 180 ? farm.Paused[..177].TrimEnd() + "..." : farm.Paused, x + pad, cy, inner, Game1.textColor * 0.8f);
            cy += 8;

            // One row per farmer: who, whose turn it still is, and who is in the farm right now.
            int rowHeight = (int)Game1.smallFont.MeasureString("Ay").Y + 10;
            var farmers = farm.Sorted;
            int room = Math.Max(0, (floor - rowHeight - cy) / rowHeight);
            int shown = farmers.Count <= room ? farmers.Count : Math.Max(0, room - 1);
            for (int i = 0; i < shown; i++)
            {
                var f = farmers[i];
                if (i % 2 == 0) b.Draw(Game1.staminaRect, new Rectangle(x + pad - 8, cy - 3, inner + 16, rowHeight - 2), Game1.textColor * 0.06f);
                string who = f.Player is not null && f.Player != f.Name ? $"{f.Name} ({f.Player})" : f.Name;
                string status = HubFarm.StatusWords(f.Status) + (f.Online ? ", in the farm now" : "");
                var statusSize = Game1.smallFont.MeasureString(status);
                Text(b, lines, Fit(who, Game1.smallFont, 1f, inner - statusSize.X - 24), Game1.smallFont, x + pad, cy,
                    f.Status == "Pending" ? Game1.textColor : Game1.textColor * 0.65f, 1f);
                Text(b, lines, status, Game1.smallFont, x + pad + inner - (int)statusSize.X, cy,
                    f.Online ? Green : f.Status == "Pending" ? Game1.textColor : Game1.textColor * 0.65f, 1f);
                cy += rowHeight;
            }
            // In a short window there may be no room for rows at all: nothing is drawn over the lines below.
            if (shown < farmers.Count && cy + rowHeight <= floor - rowHeight + 6)
                cy += Text(b, lines, shown == 0 ? $"{farmers.Count} farmers; a taller window lists them" : $"and {farmers.Count - shown} more", Game1.smallFont, x + pad, cy, Game1.textColor * 0.65f, 1f);
            if (cy <= floor - rowHeight + 6)
                Text(b, lines, hub.FarmError is not null ? "Could not refresh just now. " + farm.HostLine : farm.HostLine, Game1.smallFont, x + pad, floor - rowHeight + 6,
                    farm.HostOnline && hub.FarmError is null ? Green : Game1.textColor * 0.7f, 1f);
        }

        // The line above the buttons: a fresh notice, else why Join is not lit.
        // A player whose own turn is still open is told they carry on in it; that is not a warning.
        string? hint = notice is not null && DateTime.UtcNow < noticeUntil ? notice
            : !CanJoin ? WhyNoJoin() : hub.Advice() is { Kind: JoinOutlook.Resume } resume ? resume.Text : null;
        if (hint is not null) HintLines(b, lines, hint, x + pad, floor + 4, inner);

        Button(b, lines, joinButton, link.Name is { } n && Game1.dialogueFont.MeasureString("Join " + n).X * 0.8f < joinButton.bounds.Width - 32 ? "Join " + n : "Join the farm", CanJoin);
        Button(b, lines, hostButton, "Host a farm", true);
        var wake = hub.Wakeable();
        Button(b, lines, wakeButton, wake.Label, wake.Enabled);
        Lines = lines;
    }

    private void Button(SpriteBatch b, List<string> lines, ClickableComponent c, string label, bool enabled)
    {
        bool hot = enabled && hovered == c;
        var tint = !enabled ? Color.White * 0.45f : hot ? Color.White : new Color(228, 218, 204);
        drawTextureBox(b, Game1.mouseCursors, ButtonFrame, c.bounds.X, c.bounds.Y, c.bounds.Width, c.bounds.Height, tint, 4f, drawShadow: enabled);
        float scale = 0.8f;
        string text = Fit(label, Game1.dialogueFont, scale, c.bounds.Width - 24);
        var size = Game1.dialogueFont.MeasureString(text) * scale;
        Utility.drawTextWithShadow(b, text, Game1.dialogueFont, new Vector2(c.bounds.X + (c.bounds.Width - size.X) / 2, c.bounds.Y + (c.bounds.Height - size.Y) / 2 + 2),
            Game1.textColor * (enabled ? 1f : 0.45f), scale);
        lines.Add((enabled ? "[" : "(") + text + (enabled ? "]" : ")"));
    }

    private static int Text(SpriteBatch b, List<string> lines, string text, SpriteFont font, int x, int y, Color color, float scale)
    {
        Utility.drawTextWithShadow(b, text, font, new Vector2(x, y), color, scale);
        lines.Add(text);
        return (int)(font.MeasureString(text).Y * scale) + 2;
    }

    /// <summary>The hint in full on up to two lines; only a third line's worth is cut.</summary>
    private static void HintLines(SpriteBatch b, List<string> lines, string text, int x, int y, int width)
    {
        var wrapped = Game1.parseText(text, Game1.smallFont, width).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        if (wrapped.Count > 2) wrapped = new List<string> { wrapped[0], Fit(string.Join(" ", wrapped.Skip(1)), Game1.smallFont, 1f, width) };
        Utility.drawTextWithShadow(b, string.Join("\n", wrapped), Game1.smallFont, new Vector2(x, y), Game1.textColor * 0.8f);
        lines.Add(text);
    }

    private static int Wrapped(SpriteBatch b, List<string> lines, string text, int x, int y, int width, Color color)
    {
        string wrapped = Game1.parseText(text, Game1.smallFont, width);
        Utility.drawTextWithShadow(b, wrapped, Game1.smallFont, new Vector2(x, y), color);
        lines.Add(text);
        return (int)Game1.smallFont.MeasureString(wrapped).Y + 4;
    }

    private static string Fit(string text, SpriteFont font, float scale, float max)
    {
        if (font.MeasureString(text).X * scale <= max) return text;
        while (text.Length > 1 && font.MeasureString(text + "...").X * scale > max) text = text[..^1];
        return text.TrimEnd() + "...";
    }
}

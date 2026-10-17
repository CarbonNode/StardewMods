using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharedDay.Core;
using StardewModdingAPI;
using StardewValley;

namespace SharedDay.Mod;

/// <summary>
/// The farmers' faces for the turn tracker: each farmer's face as the farm sees them (MiniFace -
/// the game's own small portrait, with the accessory and hat it leaves out), drawn off screen on
/// the host and kept as a small PNG, so the ModRelay launcher can show who is who without anyone
/// uploading a picture.
/// One farmer is drawn per call, a new face first and then whichever is oldest, so a changed
/// hairstyle or hat reaches the tracker within about a minute per farmer on the farm.
/// </summary>
internal sealed class FarmerPortraits
{
    /// <summary>
    /// The picture is the face's own square (SharedDay.Core.FaceLayout.Span: a 16-pixel head with
    /// the two pixels a hat overhangs it by on each side) at 2x, so 40 pixels, which a launcher
    /// shows in its own slot. Before 0.1.44 it was a 32-pixel square of the game's small portrait,
    /// which had no room for a hat and drew no accessory at all.
    /// </summary>
    private const int Size = FaceLayout.Span * 2;
    private const float Scale = 2f;
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(60);

    private readonly IMonitor monitor;
    private readonly Dictionary<long, Face> faces = new();
    private RenderTarget2D? target;
    private SpriteBatch? batch;
    private bool failed;

    private sealed record Face(string? Address, DateTime At);

    public FarmerPortraits(IMonitor monitor) => this.monitor = monitor;

    /// <summary>The farmer's face as the tracker accepts it, or null when none has been drawn (yet).</summary>
    public string? For(long id) => faces.TryGetValue(id, out var face) ? face.Address : null;

    public void Reset() => faces.Clear();

    /// <summary>Draws at most one of these farmers. Call from the game's update, about once a second.</summary>
    public void CaptureOne(IEnumerable<long> ids)
    {
        if (failed || Game1.graphics?.GraphicsDevice is not { IsDisposed: false } device) return;
        var now = DateTime.UtcNow;
        Farmer? due = null;
        var oldest = DateTime.MaxValue;
        foreach (long id in ids)
        {
            var farmer = Game1.getAllFarmers().FirstOrDefault(p => p.UniqueMultiplayerID == id);
            if (farmer is null) continue;
            if (!faces.TryGetValue(id, out var face))
            {
                due = farmer;
                break;
            }
            if (now - face.At >= Refresh && face.At < oldest)
            {
                oldest = face.At;
                due = farmer;
            }
        }
        if (due is null) return;
        try
        {
            faces[due.UniqueMultiplayerID] = new Face(PortraitWire.ToAddress(Render(device, due)), now);
        }
        catch (Exception ex)
        {
            // A mod that replaces the farmer's renderer, or a host with no graphics, must not take
            // the reports down with it: the farm is reported without faces from here on.
            failed = true;
            batch?.Dispose();
            batch = null;
            monitor.Log("Could not draw a farmer's face for the turn tracker; reports go out without faces. " + ex.GetType().Name, LogLevel.Warn);
        }
    }

    private byte[] Render(GraphicsDevice device, Farmer farmer)
    {
        target ??= new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color, DepthFormat.None);
        batch ??= new SpriteBatch(device);
        var previous = device.GetRenderTargets();
        try
        {
            device.SetRenderTarget(target);
            device.Clear(Color.Transparent);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            MiniFace.Draw(batch, farmer, Vector2.Zero, Scale);
            batch.End();
        }
        finally
        {
            device.SetRenderTargets(previous);
        }
        using var stream = new MemoryStream();
        target.SaveAsPng(stream, Size, Size);
        return stream.ToArray();
    }
}

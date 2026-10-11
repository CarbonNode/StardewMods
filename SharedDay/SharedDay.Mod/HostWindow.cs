using System.Diagnostics;
using System.Runtime.InteropServices;
using StardewModdingAPI;

namespace SharedDay.Mod;

/// <summary>
/// A farm that hosts itself is a server, and a server has no business owning a window on a PC
/// that is used for other things: on 2026-10-07 the live farm's window sat in front on its host
/// PC and took whatever was typed there. Stardew has no server without a game, but the game does
/// not need to be seen. With AutoHost.HideWindow the game's windows and SMAPI's console are taken
/// off the desktop once the farm is loaded. Nothing is closed: the game runs as before.
/// </summary>
internal static class HostWindow
{
    private const int Hide = 0;
    private delegate bool EnumProc(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc each, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int how);
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();

    public static void HideAll(IMonitor monitor)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            uint me = (uint)Process.GetCurrentProcess().Id;
            int hidden = 0;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out uint owner);
                if (owner == me && IsWindowVisible(window) && ShowWindow(window, Hide)) hidden++;
                return true;
            }, IntPtr.Zero);
            IntPtr console = GetConsoleWindow();
            if (console != IntPtr.Zero && IsWindowVisible(console) && ShowWindow(console, Hide)) hidden++;
            monitor.Log($"This farm hosts itself with its window hidden (AutoHost.HideWindow): {hidden} window(s) taken off the desktop. The game keeps running.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            monitor.Log($"Could not hide the farm's window; it stays on the desktop. {ex.Message}", LogLevel.Warn);
        }
    }
}

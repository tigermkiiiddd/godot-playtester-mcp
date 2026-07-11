#nullable disable
using Godot;
namespace GodotPlaytester;
using System;
using System.Runtime.InteropServices;

public partial class GameMcpServer
{
    // ═══════════════════════════════════════════════════════════════════════
    //  OS MOUSE INPUT ("mode": "os")
    // ═══════════════════════════════════════════════════════════════════════
    //
    // The rest of the mouse toolset drives InputEventMouse* through
    // Input.ParseInputEvent — fast and headless-safe, but it injects directly at the
    // engine layer, so it can never observe (or reproduce) bugs that live in the OS→
    // window→engine chain: an unfocused window silently swallowing clicks, a control
    // occluded by another window, a wrong mouse_filter eating the event, or a click
    // landing in the wrong place because of desktop DPI scaling.
    //
    // "os" mode instead drives the REAL Windows cursor via user32 SetCursorPos +
    // SendInput, so a click only "lands" if the whole real chain actually routes it
    // there. Windows-only (SendInput has no cross-platform equivalent); on other OSes
    // movement falls back to DisplayServer.WarpMouse and clicks are refused outright.

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    // ── Coordinate conversion ────────────────────────────────────────────

    /// <summary>
    /// Convert a window-local point — the same viewport-space coordinates every other
    /// mouse tool (and get_ui_layout rects) already use — into absolute OS screen
    /// coordinates. Accounts for the window's screen position AND any stretch-mode
    /// scale mismatch between the logical viewport size and the physical window size
    /// (e.g. a 1920x1080 window rendering a 1280x720 base viewport).
    /// </summary>
    private (int screenX, int screenY) WindowLocalToScreen(float x, float y)
    {
        var windowId = (int)DisplayServer.MainWindowId;
        var windowPos = DisplayServer.WindowGetPosition(windowId);
        var windowSize = DisplayServer.WindowGetSize(windowId);

        var viewportSize = GetViewport().GetVisibleRect().Size;
        float scaleX = viewportSize.X > 0 ? windowSize.X / viewportSize.X : 1f;
        float scaleY = viewportSize.Y > 0 ? windowSize.Y / viewportSize.Y : 1f;

        int screenX = windowPos.X + (int)Math.Round(x * scaleX);
        int screenY = windowPos.Y + (int)Math.Round(y * scaleY);
        return (screenX, screenY);
    }

    /// <summary>
    /// Best-effort focus guard: an OS click into a truly unfocused/background window is
    /// a real, honest test outcome (many UIs swallow or misroute input while unfocused),
    /// so we don't fail the call over it — we just make one good-faith attempt to bring
    /// the game window forward first, so "os mode" exercises the intended target by
    /// default rather than failing the test for an unrelated reason.
    /// </summary>
    private bool EnsureWindowFocused()
    {
        var win = GetWindow();
        if (win != null && win.HasFocus()) return true;
        DisplayServer.WindowMoveToForeground((int)DisplayServer.MainWindowId);
        return win != null && win.HasFocus();
    }

    private static void SendMouseButtonInput(uint flag)
    {
        var input = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = flag } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static (uint down, uint up) ButtonFlags(string button) => button.ToLowerInvariant() switch
    {
        "right" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
        "middle" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
        _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
    };

    // ── OS input helpers (shared by click_mouse/move_mouse/double_click/click_element) ──

    /// <summary>Move the real OS cursor to a window-local point via SetCursorPos.</summary>
    private string OsMoveMouse(float x, float y)
    {
        if (!OperatingSystem.IsWindows())
        {
            // No SendInput off Windows. Fall back to an engine-level warp so callers still
            // get cursor movement — but note this does NOT exercise the real OS input chain.
            _simMousePos = new Vector2(x, y);
            DisplayServer.WarpMouse(new Vector2I((int)Math.Round(x), (int)Math.Round(y)));
            return $"{{\"ok\":true,\"x\":{x},\"y\":{y},\"warning\":\"os mouse move on non-Windows falls back to window-local WarpMouse (not real OS input)\"}}";
        }

        bool focused = EnsureWindowFocused();
        var (sx, sy) = WindowLocalToScreen(x, y);
        SetCursorPos(sx, sy);
        _simMousePos = new Vector2(x, y); // keep the virtual on-screen crosshair in sync
        return $"{{\"ok\":true,\"x\":{x},\"y\":{y},\"screen_x\":{sx},\"screen_y\":{sy},\"focused_window\":{(focused ? "true" : "false")}}}";
    }

    /// <summary>
    /// Move the real OS cursor to a window-local point, then drive a real mouse button via
    /// SendInput. 'action' is "press" (down only), "release" (up only), or anything else
    /// (default "click") for a full press+release. Windows-only — returns a structured
    /// error on other platforms since there is no SendInput equivalent to fall back to.
    /// </summary>
    private string OsClickMouse(string button, string action, float x, float y)
    {
        if (!OperatingSystem.IsWindows())
            return "{\"error\":\"os mouse mode requires Windows\"}";

        bool focused = EnsureWindowFocused();
        var (sx, sy) = WindowLocalToScreen(x, y);
        SetCursorPos(sx, sy);
        _simMousePos = new Vector2(x, y);

        var (downFlag, upFlag) = ButtonFlags(button);
        var btn = button.ToLowerInvariant();

        switch (action)
        {
            case "press":
                SendMouseButtonInput(downFlag);
                if (btn == "left") _simMouseLeftDown = true; else if (btn == "right") _simMouseRightDown = true;
                break;
            case "release":
                SendMouseButtonInput(upFlag);
                if (btn == "left") _simMouseLeftDown = false; else if (btn == "right") _simMouseRightDown = false;
                break;
            default: // "click" — full press + release
                SendMouseButtonInput(downFlag);
                SendMouseButtonInput(upFlag);
                break;
        }

        return $"{{\"ok\":true,\"button\":\"{button}\",\"x\":{x},\"y\":{y},\"screen_x\":{sx},\"screen_y\":{sy},\"focused_window\":{(focused ? "true" : "false")}}}";
    }
}

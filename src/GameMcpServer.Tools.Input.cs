#nullable disable
using Godot;
namespace GodotPlaytester;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;


public partial class GameMcpServer
{
    // ── press_key ────────────────────────────────────────────────────────

    private string PressKey(string key, string action)
    {
        try
        {
            var keycode = MapKey(key);
            if (action == "press")
            {
                // Complete tap: press + release so Input.IsKeyPressed doesn't stay stuck
                Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = keycode });
                Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = keycode });
            }
            else
            {
                Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = keycode });
            }
            return $"{{\"ok\":true,\"key\":\"{key}\",\"action\":\"{action}\"}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private static Key MapKey(string name)
    {
        var u = name.ToUpperInvariant();
        if (u.Length == 1 && u[0] >= 'A' && u[0] <= 'Z') return (Key)(Key.A + (u[0] - 'A'));
        if (u.Length == 1 && u[0] >= '0' && u[0] <= '9') return (Key)(Key.Key0 + (u[0] - '0'));
        return u switch
        {
            "SPACE" => Key.Space, "ESCAPE" or "ESC" => Key.Escape,
            "ENTER" or "RETURN" => Key.Enter, "SHIFT" => Key.Shift,
            "CTRL" or "CONTROL" => Key.Ctrl, "ALT" => Key.Alt,
            "TAB" => Key.Tab, "BACKSPACE" => Key.Backspace,
            "DELETE" => Key.Delete, "UP" => Key.Up, "DOWN" => Key.Down,
            "LEFT" => Key.Left, "RIGHT" => Key.Right,
            _ when u.StartsWith("F") && int.TryParse(u[1..], out var f) && f >= 1 && f <= 12 => Key.F1 + f - 1,
            _ => Key.Unknown
        };
    }

    private static Key MapCharToKey(char ch)
    {
        if (ch >= 'a' && ch <= 'z') return (Key)((int)Key.A + (ch - 'a'));
        if (ch >= 'A' && ch <= 'Z') return (Key)((int)Key.A + (ch - 'A'));
        if (ch >= '0' && ch <= '9') return (Key)((int)Key.Key0 + (ch - '0'));
        return ch switch
        {
            ' ' => Key.Space, '\n' => Key.Enter, '\t' => Key.Tab,
            '.' => Key.Period, ',' => Key.Comma, '-' => Key.Minus,
            '=' => Key.Equal, '/' => Key.Slash,
            _ => Key.Unknown
        };
    }

    // ── click_mouse ──────────────────────────────────────────────────────

    private string ClickMouse(string button, string action, float x, float y, string mode = "virtual")
    {
        if (!TryParseMouseButton(button, out var btnIdx))
            return $"{{\"error\":\"Unsupported mouse button: {button}. Use left, right, or middle.\"}}";
        if (action != "press" && action != "release" && action != "click")
            return $"{{\"error\":\"Unsupported mouse action: {action}. Use press, release, or click.\"}}";
        if (mode != "virtual" && mode != "os")
            return $"{{\"error\":\"Unsupported mouse mode: {mode}. Use virtual or os.\"}}";

        if (mode == "os") return OsClickMouse(button, action, x, y);
        try
        {
            var pos = new Vector2(x, y);
            switch (action)
            {
                case "press":
                    PushVirtualMouseButton(btnIdx, true, pos);
                    break;
                case "release":
                    PushVirtualMouseButton(btnIdx, false, pos);
                    break;
                case "click":
                    PushVirtualMouseButton(btnIdx, true, pos);
                    PushVirtualMouseButton(btnIdx, false, pos);
                    break;
            }
            return $"{{\"ok\":true,\"button\":\"{button}\",\"action\":\"{action}\",\"x\":{x},\"y\":{y},\"mode\":\"virtual\",\"delivery\":\"viewport\"}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    // ── Shared virtual pointer delivery ──────────────────────────────────

    private static bool TryParseMouseButton(string button, out MouseButton result)
    {
        result = button?.ToLowerInvariant() switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.None
        };
        return result != MouseButton.None;
    }

    private MouseButtonMask CurrentSimulatedButtonMask()
    {
        var mask = (MouseButtonMask)0;
        if (_simMouseLeftDown) mask |= MouseButtonMask.Left;
        if (_simMouseRightDown) mask |= MouseButtonMask.Right;
        if (_simMouseMiddleDown) mask |= MouseButtonMask.Middle;
        return mask;
    }

    private void PushVirtualMouseMotion(Vector2 pos, Viewport viewport = null)
    {
        _simMousePos = pos;
        var ev = new InputEventMouseMotion
        {
            Position = pos,
            GlobalPosition = pos,
            ButtonMask = CurrentSimulatedButtonMask()
        };
        var vp = viewport ?? GetViewport();
        if (vp != null) vp.PushInput(ev);
        else Input.ParseInputEvent(ev);
    }

    private void PushVirtualMouseButton(MouseButton idx, bool pressed, Vector2 pos,
        bool doubleClick = false, Viewport viewport = null)
    {
        var vp = viewport ?? GetViewport();
        if (pressed)
        {
            var nudge = pos + new Vector2(2, 2);
            PushVirtualMouseMotion(nudge, vp);
            PushVirtualMouseMotion(pos, vp);
        }
        else
        {
            _simMousePos = pos;
        }

        SetSimButton(idx, pressed);
        var ev = new InputEventMouseButton
        {
            Pressed = pressed,
            ButtonIndex = idx,
            Position = pos,
            GlobalPosition = pos,
            ButtonMask = CurrentSimulatedButtonMask(),
            DoubleClick = doubleClick
        };
        if (vp != null) vp.PushInput(ev);
        else Input.ParseInputEvent(ev);
    }

    private void SetSimButton(MouseButton idx, bool down)
    {
        if (idx == MouseButton.Left) _simMouseLeftDown = down;
        else if (idx == MouseButton.Right) _simMouseRightDown = down;
        else if (idx == MouseButton.Middle) _simMouseMiddleDown = down;
    }

    // ── move_mouse ───────────────────────────────────────────────────────

    private string MoveMouseWithOption(Dictionary<string, JsonElement> args)
    {
        float x = args["x"].GetSingle();
        float y = args["y"].GetSingle();
        string mode = args.ContainsKey("mode") ? args["mode"].GetString() : "virtual";

        if (mode == "os") return OsMoveMouse(x, y); // duration smooth-move is a virtual-mode-only feature (v1)

        // If duration provided, use macro for smooth move
        if (args.ContainsKey("duration") && args["duration"].GetSingle() > 0)
        {
            float duration = args["duration"].GetSingle();
            var id = $"macro_{++_macroCounter:D3}";
            var steps = new List<MacroStep>
            {
                new MacroStep
                {
                    Type = MacroStepType.Drag,
                    X = _simMousePos.X,
                    Y = _simMousePos.Y,
                    TargetX = x,
                    TargetY = y,
                    Duration = duration,
                    Button = "none" // no button pressed — just move
                }
            };
            _macros[id] = new MacroRun
            {
                Id = id, Name = "move_mouse", Steps = steps, Status = "running",
                StartTime = Time.GetTicksMsec() / 1000.0
            };
            return $"{{\"ok\":true,\"x\":{x},\"y\":{y},\"duration\":{duration},\"macro_id\":\"{id}\"}}";
        }

        // Instant teleport
        return MoveMouse(x, y);
    }

    private string MoveMouse(float x, float y)
    {
        try { PushVirtualMouseMotion(new Vector2(x, y)); return $"{{\"ok\":true,\"x\":{x},\"y\":{y},\"mode\":\"virtual\",\"delivery\":\"viewport\"}}"; }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    // ── scroll_mouse ─────────────────────────────────────────────────────

    private string ScrollMouse(int amount)
    {
        try
        {
            var idx = amount > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            for (int i = 0; i < Math.Abs(amount); i++)
                PushVirtualMouseButton(idx, true, _simMousePos);
            return $"{{\"ok\":true,\"amount\":{amount}}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }
}

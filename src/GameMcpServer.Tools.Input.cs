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
        if (mode == "os") return OsClickMouse(button, action, x, y);
        try
        {
            _simMousePos = new Vector2(x, y);
            var btnIdx = button.ToLowerInvariant() switch { "right" => MouseButton.Right, "middle" => MouseButton.Middle, _ => MouseButton.Left };
            bool pressed = action == "press";
            Input.ParseInputEvent(new InputEventMouseButton { Pressed = pressed, Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y), ButtonIndex = btnIdx });
            // Track simulated button state
            if (btnIdx == MouseButton.Left) _simMouseLeftDown = pressed;
            else if (btnIdx == MouseButton.Right) _simMouseRightDown = pressed;
            return $"{{\"ok\":true,\"button\":\"{button}\",\"x\":{x},\"y\":{y}}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
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
        try { _simMousePos = new Vector2(x, y); Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y) }); return $"{{\"ok\":true,\"x\":{x},\"y\":{y}}}"; }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    // ── scroll_mouse ─────────────────────────────────────────────────────

    private string ScrollMouse(int amount)
    {
        try
        {
            var idx = amount > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            for (int i = 0; i < Math.Abs(amount); i++) Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = idx, Pressed = true });
            return $"{{\"ok\":true,\"amount\":{amount}}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }
}

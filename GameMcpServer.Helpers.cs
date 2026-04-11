using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Shared utilities for MCP tool implementations. Eliminates duplicated patterns.
/// </summary>
public static class McpHelpers
{
    /// <summary>
    /// Parse a tool argument that accepts either comma-separated string or JSON string array.
    /// Returns null if key missing or empty.
    /// </summary>
    public static string[] ParseStringArrayArg(Dictionary<string, JsonElement> args, string key)
    {
        if (!args.ContainsKey(key)) return null;
        var el = args[key];
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            return string.IsNullOrEmpty(s) ? null : s.Split(',');
        }
        if (el.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in el.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                    list.Add(item.GetString());
            return list.Count > 0 ? list.ToArray() : null;
        }
        return null;
    }

    /// <summary>
    /// Parse mouse button string to MouseButton enum. "none" returns Left (for MoveOnly use, check before calling).
    /// </summary>
    public static MouseButton ParseMouseButton(string button)
    {
        return button?.ToLowerInvariant() switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };
    }

    /// <summary>
    /// Map key name string to Godot Key enum.
    /// </summary>
    public static Key MapKey(string name)
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
}

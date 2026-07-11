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
    // ═══════════════════════════════════════════════════════════════════════
    //  TOOL REGISTRATION
    // ═══════════════════════════════════════════════════════════════════════

    private void RegisterBuiltinTools()
    {
        // ── Scene & Info ─────────────────────────────────────────────────

        Reg("get_scene_tree", "Get the complete scene tree structure, recursively. Optional max_depth (default 6) bounds recursion.",
            p("{\"max_depth\":{\"type\":\"integer\"}}", "object"), a => GetSceneTreeJson(a));

        Reg("get_node_properties", "Get properties of a node. Requires 'path'.",
            p("{\"path\":{\"type\":\"string\"}}", "object"), a => GetNodeProperties(a["path"].GetString()));

        Reg("set_node_property", "Set a property on a node. Requires 'path', 'property', 'value' (string, bool, number, or JSON array for vectors).",
            p("{\"path\":{\"type\":\"string\"},\"property\":{\"type\":\"string\"},\"value\":{\"type\":\"string\"}}", "object"),
            a => SetNodeProperty(a["path"].GetString(), a["property"].GetString(), a["value"]));

        Reg("call_node_method", "Call a method on a node. Requires 'path', 'method'. Optional 'args' as JSON array.",
            p("{\"path\":{\"type\":\"string\"},\"method\":{\"type\":\"string\"},\"args\":{\"type\":\"string\"}}", "object", new[] { "path", "method" }),
            a => CallNodeMethod(a["path"].GetString(), a["method"].GetString(),
                a.ContainsKey("args") ? a["args"].GetString() : "[]"));

        Reg("simulate_input", "Simulate a mapped input action (ui_up, ui_accept, etc). For raw keys use press_key.",
            p("{\"action\":{\"type\":\"string\"},\"key\":{\"type\":\"string\"}}", "object"),
            a => SimulateInputAction(a["action"].GetString(), a["key"].GetString()));

        Reg("get_game_info", "Get basic game info: FPS, window size, engine version.",
            p("{}", "object"), _ => GetGameInfo());

        // ── Game State & UI ──────────────────────────────────────────────

        Reg("get_game_state",
            "Get structured game world state by Godot Groups. Returns objects with world/screen positions, distances. Supports filtering by group, area, radius, and pagination.",
            p("{\"groups\":{\"type\":\"string\"},\"radius\":{\"type\":\"number\"},\"near_x\":{\"type\":\"number\"},\"near_y\":{\"type\":\"number\"},\"near_radius\":{\"type\":\"number\"},\"area_x\":{\"type\":\"number\"},\"area_y\":{\"type\":\"number\"},\"area_w\":{\"type\":\"number\"},\"area_h\":{\"type\":\"number\"},\"limit\":{\"type\":\"integer\"},\"offset\":{\"type\":\"integer\"}}", "object"),
            a => GetGameState(a));

        Reg("get_ui_layout",
            "Get UI Control nodes as nested tree (like DOM). Grid patterns (homogeneous mcp_data children) auto-compressed to columnar format {columns,rows} — one header, value arrays per row. rect=[x,y,w,h] in screen pixels. center=[cx,cy] is the click/drag target point. tagged_only (default true) = skip decorative nodes. detail=compact (default, core fields only) or full (all mcp_data fields). path = only inspect subtree. max_depth limits recursion.",
            p("{\"visible_only\":{\"type\":\"boolean\"},\"types\":{\"type\":\"string\"},\"tagged_only\":{\"type\":\"boolean\"},\"path\":{\"type\":\"string\"},\"max_depth\":{\"type\":\"integer\"},\"detail\":{\"type\":\"string\",\"enum\":[\"compact\",\"full\"]}}", "object"),
            a => GetUILayout(a));

        // ── UI Control ───────────────────────────────────────────────────

        Reg("click_element", "Click a UI element by name or path. Uses element's rect center. Disabled or invisible buttons are rejected (a real player could not click them) unless force=true.",
            p("{\"name\":{\"type\":\"string\"},\"path\":{\"type\":\"string\"},\"button\":{\"type\":\"string\"},\"offset_x\":{\"type\":\"number\"},\"offset_y\":{\"type\":\"number\"},\"force\":{\"type\":\"boolean\"}}", "object"),
            a => ClickElement(a));

        Reg("type_text", "Input text into a UI element (LineEdit/TextEdit). Mode: set=direct, type=simulated keystrokes.",
            p("{\"target\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"},\"mode\":{\"type\":\"string\"}}", "object", new[] { "target", "text" }),
            a => TypeText(a));

        Reg("get_focused_element", "Get the currently focused UI Control node.",
            p("{}", "object"),
            _ => GetFocusedElement());

        Reg("select_option", "Select an item in OptionButton or ItemList by index.",
            p("{\"name\":{\"type\":\"string\"},\"path\":{\"type\":\"string\"},\"index\":{\"type\":\"integer\"}}", "object", new[] { "index" }),
            a => SelectOption(a));

        Reg("hover", "Move mouse to position and emit hover notification.",
            p("{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"}}", "object"),
            a => Hover(a["x"].GetSingle(), a["y"].GetSingle()));

        Reg("double_click", "Double-click at screen coordinates.",
            p("{\"button\":{\"type\":\"string\"},\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"}}", "object"),
            a => DoubleClick(
                a.ContainsKey("button") ? a["button"].GetString() : "left",
                a.ContainsKey("x") ? a["x"].GetSingle() : 0f,
                a.ContainsKey("y") ? a["y"].GetSingle() : 0f));

        Reg("drag", "Drag from one point to another. Press at start, move, release at end.",
            p("{\"from_x\":{\"type\":\"number\"},\"from_y\":{\"type\":\"number\"},\"to_x\":{\"type\":\"number\"},\"to_y\":{\"type\":\"number\"},\"duration\":{\"type\":\"number\"},\"button\":{\"type\":\"string\"}}", "object"),
            a => DragMouse(a));

        // ── Input ────────────────────────────────────────────────────────

        Reg("press_key", "Press or release a raw keyboard key (W, A, Space, Escape, Enter, F1, etc). Action defaults to 'press'.",
            p("{\"key\":{\"type\":\"string\"},\"action\":{\"type\":\"string\"}}", "object"),
            a => PressKey(a["key"].GetString(), a.ContainsKey("action") ? a["action"].GetString() : "press"));

        Reg("click_mouse", "Click or release a mouse button at screen coordinates.",
            p("{\"button\":{\"type\":\"string\"},\"action\":{\"type\":\"string\"},\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"}}", "object", new[] { "button", "action" }),
            a => ClickMouse(a["button"].GetString(), a["action"].GetString(),
                a.ContainsKey("x") ? a["x"].GetSingle() : 0f,
                a.ContainsKey("y") ? a["y"].GetSingle() : 0f));

        Reg("move_mouse", "Move the mouse cursor. Without duration: instant teleport. With duration: smooth animated move.",
            p("{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"duration\":{\"type\":\"number\"}}", "object"),
            a => MoveMouseWithOption(a));

        Reg("scroll_mouse", "Scroll the mouse wheel. Positive=up, negative=down.",
            p("{\"amount\":{\"type\":\"integer\"}}", "object"),
            a => ScrollMouse(a["amount"].GetInt32()));

        // ── Screenshot ───────────────────────────────────────────────────

        Reg("screenshot",
            "Capture current frame as an image (returned as an MCP image content block). Prefer get_game_state/get_ui_layout for structured data — use screenshot only when you need to see the visuals. max_width (default 960) downsizes oversized captures.",
            p("{\"format\":{\"type\":\"string\"},\"quality\":{\"type\":\"integer\"},\"max_width\":{\"type\":\"integer\"}}", "object"),
            a => TakeScreenshot(
                a.ContainsKey("format") ? a["format"].GetString() : "jpeg",
                a.ContainsKey("quality") ? a["quality"].GetInt32() : 80,
                a.ContainsKey("max_width") ? a["max_width"].GetInt32() : 960));

        // ── Metrics ──────────────────────────────────────────────────────

        Reg("register_metric", "Register a numeric metric for monitoring.",
            p("{\"name\":{\"type\":\"string\"},\"source_type\":{\"type\":\"string\"},\"node_path\":{\"type\":\"string\"},\"property_name\":{\"type\":\"string\"},\"sample_rate\":{\"type\":\"number\"}}", "object", new[] { "name" }),
            a => RegisterMetricViaMcp(a));

        Reg("get_metrics", "Get registered metric values. Formats: latest, timeline, csv.",
            p("{\"names\":{\"type\":\"string\"},\"format\":{\"type\":\"string\"}}", "object"),
            a => GetMetrics(a));

        // ── Test Runner ──────────────────────────────────────────────────

        Reg("start_test",
            "Start a background test: run a scene, collect metrics and optionally capture frames. Returns test_id.",
            p("{\"scene_path\":{\"type\":\"string\"},\"duration\":{\"type\":\"number\"},\"capture_frames\":{\"type\":\"number\"}}", "object", new[] { "scene_path" }),
            a => StartTest(a));

        Reg("get_test_results",
            "Get results from a background test: metrics timeline, screenshots, logs.",
            p("{\"test_id\":{\"type\":\"string\"},\"include\":{\"type\":\"string\"}}", "object", new[] { "test_id" }),
            a => GetTestResults(a["test_id"].GetString(),
                a.ContainsKey("include") ? a["include"].GetString() : "all"));

        // ── Log Capture ──────────────────────────────────────────────────

        Reg("get_logs",
            "Get AI log entries (important game events). Filter by level, category, time.",
            p("{\"min_level\":{\"type\":\"string\"},\"category\":{\"type\":\"string\"},\"since\":{\"type\":\"number\"},\"limit\":{\"type\":\"integer\"},\"order\":{\"type\":\"string\"}}", "object"),
            a => GetLogs(a, LogType.AI));

        Reg("get_debug_logs",
            "Get debug log entries (game's GD.Print replacement). Filter by level, time.",
            p("{\"min_level\":{\"type\":\"string\"},\"since\":{\"type\":\"number\"},\"limit\":{\"type\":\"integer\"}}", "object"),
            a => GetLogs(a, LogType.Debug));

        Reg("log",
            "Write a log entry from the AI agent into the AI log buffer.",
            p("{\"message\":{\"type\":\"string\"},\"level\":{\"type\":\"string\"},\"category\":{\"type\":\"string\"}}", "object", new[] { "message" }),
            a => AgentLog(a));

        Reg("get_file_logs",
            "Read recent entries from a file log category. Returns last N lines.",
            p("{\"category\":{\"type\":\"string\"},\"limit\":{\"type\":\"integer\"}}", "object", new[] { "category" }),
            a => GetFileLogs(a));

        Reg("get_file_log_summary",
            "Get summary statistics for file logs: entry counts, time range per category.",
            p("{}", "object"),
            _ => GetFileLogSummary());

        Reg("clear_logs",
            "Clear all AI and Debug log ring buffers.",
            p("{}", "object"),
            _ => { ClearLogs(); return "{\"ok\":true}"; });

        // ── Diagnostics ──────────────────────────────────────────────────

        Reg("time_scale",
            "Get or set Engine.TimeScale (range [0.1, 20]) — fast-forward playtests for roguelike balance runs. Omit 'scale' to just read the current value.",
            p("{\"scale\":{\"type\":\"number\"}}", "object"),
            a => TimeScale(a));
    }

    // Helper for concise tool registration
    private void Reg(string name, string desc, JsonObject schema, Func<Dictionary<string, JsonElement>, object> handler)
        => RegisterTool(name, desc, schema, handler);

    private static JsonObject p(string propsJson, string type, string[] required = null)
    {
        var schema = new JsonObject
        {
            ["type"] = type,
            ["properties"] = JsonNode.Parse(propsJson).AsObject(),
            ["required"] = new JsonArray()
        };
        if (required != null)
            foreach (var r in required) schema["required"].AsArray().Add(r);
        return schema;
    }
}

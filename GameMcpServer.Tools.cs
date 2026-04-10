using Godot;
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

        Reg("get_scene_tree", "Get the complete scene tree structure.",
            p("{}", "object"), _ => GetSceneTreeJson());

        Reg("get_node_properties", "Get properties of a node. Requires 'path'.",
            p("{\"path\":{\"type\":\"string\"}}", "object"), a => GetNodeProperties(a["path"].GetString()));

        Reg("set_node_property", "Set a property on a node. Requires 'path', 'property', 'value'.",
            p("{\"path\":{\"type\":\"string\"},\"property\":{\"type\":\"string\"},\"value\":{\"type\":\"string\"}}", "object"),
            a => SetNodeProperty(a["path"].GetString(), a["property"].GetString(), a["value"].GetString()));

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
            "Get all UI Control nodes with layout info (type, name, rect, text, value). Filter by type and visibility.",
            p("{\"visible_only\":{\"type\":\"boolean\"},\"types\":{\"type\":\"string\"}}", "object"),
            a => GetUILayout(a));

        // ── Input ────────────────────────────────────────────────────────

        Reg("press_key", "Press or release a raw keyboard key (W, A, Space, Escape, Enter, F1, etc).",
            p("{\"key\":{\"type\":\"string\"},\"action\":{\"type\":\"string\"}}", "object"),
            a => PressKey(a["key"].GetString(), a["action"].GetString()));

        Reg("click_mouse", "Click or release a mouse button at screen coordinates.",
            p("{\"button\":{\"type\":\"string\"},\"action\":{\"type\":\"string\"},\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"}}", "object", new[] { "button", "action" }),
            a => ClickMouse(a["button"].GetString(), a["action"].GetString(),
                a.ContainsKey("x") ? a["x"].GetSingle() : 0f,
                a.ContainsKey("y") ? a["y"].GetSingle() : 0f));

        Reg("move_mouse", "Move the mouse cursor to screen coordinates.",
            p("{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"}}", "object"),
            a => MoveMouse(a["x"].GetSingle(), a["y"].GetSingle()));

        Reg("scroll_mouse", "Scroll the mouse wheel. Positive=up, negative=down.",
            p("{\"amount\":{\"type\":\"integer\"}}", "object"),
            a => ScrollMouse(a["amount"].GetInt32()));

        // ── Screenshot ───────────────────────────────────────────────────

        Reg("screenshot",
            "Capture current frame as base64 image. Prefer get_game_state/get_ui_layout for structured data — use screenshot only when you need to see the visuals.",
            p("{\"format\":{\"type\":\"string\"},\"quality\":{\"type\":\"integer\"}}", "object"),
            a => TakeScreenshot(
                a.ContainsKey("format") ? a["format"].GetString() : "jpeg",
                a.ContainsKey("quality") ? a["quality"].GetInt32() : 80));

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

    // ═══════════════════════════════════════════════════════════════════════
    //  TOOL IMPLEMENTATIONS
    // ═══════════════════════════════════════════════════════════════════════

    // ── get_game_state ───────────────────────────────────────────────────

    private string GetGameState(Dictionary<string, JsonElement> args)
    {
        var tree = GetTree();
        if (tree == null) return "{\"error\":\"No scene tree\"}";

        string[] groups = null;
        if (args.ContainsKey("groups") && !string.IsNullOrEmpty(args["groups"].GetString()))
            groups = args["groups"].GetString().Split(',');

        float? radius = args.ContainsKey("radius") ? args["radius"].GetSingle() : null;
        float? nearX = args.ContainsKey("near_x") ? args["near_x"].GetSingle() : null;
        float? nearY = args.ContainsKey("near_y") ? args["near_y"].GetSingle() : null;
        float? nearR = args.ContainsKey("near_radius") ? args["near_radius"].GetSingle() : null;
        float? areaX = args.ContainsKey("area_x") ? args["area_x"].GetSingle() : null;
        float? areaY = args.ContainsKey("area_y") ? args["area_y"].GetSingle() : null;
        float? areaW = args.ContainsKey("area_w") ? args["area_w"].GetSingle() : null;
        float? areaH = args.ContainsKey("area_h") ? args["area_h"].GetSingle() : null;
        int limit = args.ContainsKey("limit") ? args["limit"].GetInt32() : 20;
        int offset = args.ContainsKey("offset") ? args["offset"].GetInt32() : 0;

        var player = FindPlayer();
        var camera = GetViewport().GetCamera2D();
        var result = new JsonObject();

        Node2D p2d = player as Node2D;
        if (p2d != null) result["player"] = BuildNodeInfo(p2d, camera, p2d);

        var tracked = groups ?? new[] { "enemies", "npcs", "items", "interactables", "projectiles", "triggers" };
        foreach (var group in tracked)
        {
            var g = group.Trim();
            var nodes = tree.GetNodesInGroup(g);
            var filtered = new List<Node2D>();

            foreach (Node n in nodes)
            {
                if (n == player || n is not Node2D n2d) continue;
                var pos = n2d.GlobalPosition;
                if (radius.HasValue && p2d != null && pos.DistanceTo(p2d.GlobalPosition) > radius.Value) continue;
                if (nearX.HasValue && nearY.HasValue && nearR.HasValue && pos.DistanceTo(new Vector2(nearX.Value, nearY.Value)) > nearR.Value) continue;
                if (areaX.HasValue && areaY.HasValue && areaW.HasValue && areaH.HasValue)
                {
                    if (pos.X < areaX.Value || pos.X > areaX.Value + areaW.Value) continue;
                    if (pos.Y < areaY.Value || pos.Y > areaY.Value + areaH.Value) continue;
                }
                filtered.Add(n2d);
            }

            var paged = filtered.Skip(offset).Take(limit).ToList();
            var items = new JsonArray();
            foreach (var n in paged) items.Add(BuildNodeInfo(n, camera, p2d));
            result[g] = new JsonObject { ["total"] = filtered.Count, ["returned"] = paged.Count, ["items"] = items };
        }
        return JsonSerializer.Serialize(result);
    }

    private static JsonNode BuildNodeInfo(Node2D node, Camera2D camera, Node2D player)
    {
        var o = new JsonObject
        {
            ["name"] = node.Name.ToString(),
            ["type"] = node.GetType().Name,
            ["path"] = node.GetPath().ToString(),
            ["world_pos"] = new JsonArray { Math.Round(node.GlobalPosition.X, 1), Math.Round(node.GlobalPosition.Y, 1) }
        };
        if (camera != null)
        {
            var sp = camera.GetScreenTransform() * node.GlobalPosition;
            o["screen_pos"] = new JsonArray { Math.Round(sp.X, 0), Math.Round(sp.Y, 0) };
        }
        if (player != null && node != player)
            o["dist"] = Math.Round(node.GlobalPosition.DistanceTo(player.GlobalPosition), 1);
        return o;
    }

    private Node FindPlayer()
    {
        if (!string.IsNullOrEmpty(PlayerPath)) return GetNodeOrNull(PlayerPath);
        return GetTree()?.GetFirstNodeInGroup("player");
    }

    // ── get_ui_layout ────────────────────────────────────────────────────

    private string GetUILayout(Dictionary<string, JsonElement> args)
    {
        bool visOnly = !args.ContainsKey("visible_only") || args["visible_only"].GetBoolean();
        string[] types = null;
        if (args.ContainsKey("types") && !string.IsNullOrEmpty(args["types"].GetString()))
            types = args["types"].GetString().Split(',');

        var elements = new JsonArray();
        CollectUI(GetTree()?.Root, elements, visOnly, types);
        return JsonSerializer.Serialize(new JsonObject { ["elements"] = elements });
    }

    private void CollectUI(Node node, JsonArray elements, bool visOnly, string[] types)
    {
        if (node == null || node == this) return;
        foreach (var child in node.GetChildren())
        {
            if (child is Control c)
            {
                if (visOnly && !c.Visible) continue;
                if (types != null && !types.Any(t => child.GetType().Name.Contains(t.Trim(), StringComparison.OrdinalIgnoreCase))) continue;

                var e = new JsonObject { ["type"] = child.GetType().Name, ["name"] = c.Name.ToString(), ["visible"] = c.Visible };
                if (child is BaseButton bb) e["disabled"] = bb.Disabled;
                var r = c.GetGlobalRect();
                e["rect"] = new JsonArray { Math.Round(r.Position.X, 0), Math.Round(r.Position.Y, 0), Math.Round(r.Size.X, 0), Math.Round(r.Size.Y, 0) };

                switch (child)
                {
                    case CheckBox cb: e["pressed"] = cb.ButtonPressed; e["text"] = cb.Text; break;
                    case OptionButton ob: e["selected"] = ob.Selected; e["item_count"] = ob.ItemCount; e["text"] = ob.Text; break;
                    case Button b: e["text"] = b.Text; break;
                    case Label l: e["text"] = l.Text; break;
                    case ProgressBar pb: e["value"] = Math.Round(pb.Value, 1); e["max_value"] = Math.Round(pb.MaxValue, 1); break;
                    case LineEdit le: e["text"] = le.Text; break;
                    case Slider s: e["value"] = Math.Round(s.Value, 2); e["min_value"] = Math.Round(s.MinValue, 2); e["max_value"] = Math.Round(s.MaxValue, 2); break;
                    case ItemList il: e["item_count"] = il.ItemCount; break;
                }
                elements.Add(e);
            }
            CollectUI(child, elements, visOnly, types);
        }
    }

    // ── screenshot ───────────────────────────────────────────────────────

    private string TakeScreenshot(string format, int quality)
    {
        try
        {
            var img = GetViewport().GetTexture().GetImage();
            byte[] buf = format == "png" ? img.SavePngToBuffer() : img.SaveJpgToBuffer();
            return JsonSerializer.Serialize(new JsonObject { ["format"] = format, ["width"] = img.GetWidth(), ["height"] = img.GetHeight(), ["size_bytes"] = buf.Length, ["data"] = Convert.ToBase64String(buf) });
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    // ── press_key ────────────────────────────────────────────────────────

    private string PressKey(string key, string action)
    {
        try { Input.ParseInputEvent(new InputEventKey { Pressed = action == "press", Keycode = MapKey(key) }); return $"{{\"ok\":true,\"key\":\"{key}\",\"action\":\"{action}\"}}"; }
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

    // ── click_mouse ──────────────────────────────────────────────────────

    private string ClickMouse(string button, string action, float x, float y)
    {
        try
        {
            Input.ParseInputEvent(new InputEventMouseButton { Pressed = action == "press", Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y), ButtonIndex = button.ToLowerInvariant() switch { "right" => MouseButton.Right, "middle" => MouseButton.Middle, _ => MouseButton.Left } });
            return $"{{\"ok\":true,\"button\":\"{button}\",\"x\":{x},\"y\":{y}}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    // ── move_mouse ───────────────────────────────────────────────────────

    private string MoveMouse(float x, float y)
    {
        try { Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y) }); return $"{{\"ok\":true,\"x\":{x},\"y\":{y}}}"; }
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

    // ── metrics ──────────────────────────────────────────────────────────

    private string RegisterMetricViaMcp(Dictionary<string, JsonElement> args)
    {
        var name = args["name"].GetString();
        var src = args.ContainsKey("source_type") ? args["source_type"].GetString() : "custom";
        var rate = args.ContainsKey("sample_rate") ? args["sample_rate"].GetSingle() : 1.0;

        if (src == "node_property")
            RegisterMetric(name, args["node_path"].GetString(), args["property_name"].GetString(), rate);
        else if (!_metrics.ContainsKey(name))
        {
            _metrics[name] = new MetricEntry { Getter = () => "not_registered_in_code", SampleRate = rate };
            _metricHistory[name] = new List<MetricSample>();
        }
        return $"{{\"ok\":true,\"name\":\"{name}\"}}";
    }

    private string GetMetrics(Dictionary<string, JsonElement> args)
    {
        string[] names = null;
        if (args.ContainsKey("names") && !string.IsNullOrEmpty(args["names"].GetString()))
            names = args["names"].GetString().Split(',');

        var fmt = args.ContainsKey("format") ? args["format"].GetString() : "latest";
        var query = names ?? _metrics.Keys.ToArray();
        var result = new JsonObject();

        foreach (var name in query)
        {
            var k = name.Trim();
            if (!_metrics.TryGetValue(k, out _) || !_metricHistory.TryGetValue(k, out var hist)) continue;
            switch (fmt)
            {
                case "timeline":
                    var tl = new JsonArray();
                    foreach (var s in hist) tl.Add(new JsonArray { Math.Round(s.Time, 2), FmtVal(s.Value) });
                    result[k] = tl; break;
                case "csv":
                    var lines = new List<string> { "metric,time,value" };
                    foreach (var s in hist) lines.Add($"{k},{Math.Round(s.Time, 2)},{s.Value}");
                    result[k] = string.Join("\n", lines); break;
                default:
                    var last = hist.Count > 0 ? hist[^1].Value : _metrics[k].Getter();
                    result[k] = FmtVal(last); break;
            }
        }
        return JsonSerializer.Serialize(result);
    }

    private static JsonNode FmtVal(object v)
    {
        if (v == null) return "null";
        if (v is double d) return Math.Round(d, 3);
        if (v is float f) return Math.Round(f, 3);
        if (v is int i) return i;
        if (v is long l) return l;
        if (v is bool b) return b;
        return v.ToString();
    }

    // ── HUD ──────────────────────────────────────────────────────────────

    private void CreateHealthHud()
    {
        _healthLabel = new Label { Name = "McpHealthLabel", Position = new Vector2(10, 10), Size = new Vector2(400, 60) };
        _healthLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.6f, 0.8f));
        _healthLabel.AddThemeFontSizeOverride("font_size", 14);
        var canvas = new CanvasLayer { Name = "McpHudLayer", Layer = 100 };
        canvas.AddChild(_healthLabel);
        _mainQueue.Enqueue(() => GetTree().Root.CallDeferred("add_child", canvas));
    }

    private void UpdateHealthHud()
    {
        if (_healthLabel == null) return;
        var runningMacros = _macros.Values.Count(m => m.Status == "running");
        var listening = _running ? "ON" : "OFF";
        _healthLabel.Text = $"[MCP] http://localhost:{Port} | {listening}\n" +
                            $"Tools: {_tools.Count} | Metrics: {_metrics.Count} | Macros: {runningMacros} | Reqs: {_requestCount}";
    }

    // ── metrics sampling ─────────────────────────────────────────────────

    private void SampleMetrics(double delta)
    {
        foreach (var kv in _metrics)
        {
            kv.Value.Elapsed += delta;
            if (kv.Value.Elapsed < kv.Value.SampleRate) continue;
            kv.Value.Elapsed = 0;
            try
            {
                var val = kv.Value.Getter();
                if (!_metricHistory.ContainsKey(kv.Key)) _metricHistory[kv.Key] = new List<MetricSample>();
                var h = _metricHistory[kv.Key];
                h.Add(new MetricSample(Time.GetTicksMsec() / 1000.0, val));
                var maxSec = MAX_HISTORY_SECONDS;
                foreach (var t in _tests.Values) if (t.Status == "running") maxSec = Math.Max(maxSec, MAX_TEST_HISTORY_SECONDS);
                var cut = Time.GetTicksMsec() / 1000.0 - maxSec;
                while (h.Count > 0 && h[0].Time < cut) h.RemoveAt(0);
            }
            catch { }
        }
    }

    // ── test runner ──────────────────────────────────────────────────────

    private string StartTest(Dictionary<string, JsonElement> args)
    {
        var scene = args["scene_path"].GetString();
        var dur = args.ContainsKey("duration") ? args["duration"].GetSingle() : 30f;
        var cap = args.ContainsKey("capture_frames") ? args["capture_frames"].GetSingle() : 0f;

        var id = $"test_{++_testCounter:D3}";
        _tests[id] = new TestRun
        {
            Id = id, ScenePath = scene, MaxDuration = dur,
            CaptureFramesInterval = cap, Status = "running",
            StartTime = Time.GetTicksMsec() / 1000.0,
            MetricsSnapshot = new(), Screenshots = new(), Logs = new()
        };
        GD.Print($"[GameMcp] Test started: {id} scene={scene} duration={dur}s");
        return $"{{\"test_id\":\"{id}\",\"scene\":\"{scene}\",\"duration\":{dur}}}";
    }

    private void UpdateTests(double delta)
    {
        foreach (var kv in _tests.ToList())
        {
            var t = kv.Value;
            if (t.Status != "running") continue;
            var elapsed = Time.GetTicksMsec() / 1000.0 - t.StartTime;

            if (t.CaptureFramesInterval > 0)
            {
                t.FrameCaptureTimer += delta;
                if (t.FrameCaptureTimer >= t.CaptureFramesInterval)
                {
                    t.FrameCaptureTimer = 0;
                    try { var img = GetViewport().GetTexture().GetImage(); t.Screenshots.Add(Convert.ToBase64String(img.SaveJpgToBuffer())); }
                    catch { }
                }
            }
            if (elapsed >= t.MaxDuration)
            {
                t.Status = "completed"; t.Duration = elapsed;
                foreach (var mk in _metricHistory) t.MetricsSnapshot[mk.Key] = new List<MetricSample>(mk.Value);
                int pass = 0, fail = 0; var failures = new List<string>();
                foreach (var log in t.Logs) { if (log.Contains("ASSERT PASS")) pass++; if (log.Contains("ASSERT FAIL")) { fail++; failures.Add(log); } }
                t.AssertPassed = pass; t.AssertFailed = fail; t.AssertFailures = failures;
                GD.Print($"[GameMcp] Test done: {t.Id} duration={elapsed:F1}s asserts={pass}p/{fail}f");
            }
        }
    }

    private string GetTestResults(string testId, string include)
    {
        if (!_tests.TryGetValue(testId, out var t)) return $"{{\"error\":\"Test not found: {testId}\"}}";
        var r = new JsonObject { ["test_id"] = t.Id, ["status"] = t.Status, ["scene"] = t.ScenePath, ["duration_sec"] = Math.Round(t.Duration, 1) };
        if (include == "all" || include == "metrics")
        {
            var metrics = new JsonObject(); var final = new JsonObject();
            foreach (var kv in t.MetricsSnapshot)
            {
                var tl = new JsonArray();
                foreach (var s in kv.Value) tl.Add(new JsonArray { Math.Round(s.Time, 2), FmtVal(s.Value) });
                metrics[kv.Key] = tl;
                if (kv.Value.Count > 0) final[kv.Key] = FmtVal(kv.Value[^1].Value);
            }
            r["metrics_timeline"] = metrics; r["final_values"] = final;
        }
        if (include == "all" || include == "screenshots") { r["screenshot_count"] = t.Screenshots.Count; r["screenshots"] = JsonSerializer.SerializeToNode(t.Screenshots); }
        if (include == "all" || include == "logs")
        {
            r["logs"] = JsonSerializer.SerializeToNode(t.Logs);
            r["assert_results"] = new JsonObject { ["passed"] = t.AssertPassed, ["failed"] = t.AssertFailed, ["failures"] = JsonSerializer.SerializeToNode(t.AssertFailures) };
        }
        return JsonSerializer.Serialize(r);
    }

    // ── scene tree / node ops ────────────────────────────────────────────

    private string GetSceneTreeJson()
    {
        var tree = GetTree();
        return tree == null ? "{\"error\":\"No scene tree\"}" : SerializeNode(tree.CurrentScene, "");
    }

    private string SerializeNode(Node n, string indent)
    {
        if (n == null) return "null";
        var sb = new StringBuilder();
        var c = n.GetChildCount();
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}  \"path\": \"{n.GetPath().ToString()}\",");
        sb.AppendLine($"{indent}  \"type\": \"{n.GetType().Name}\",");
        sb.AppendLine($"{indent}  \"name\": \"{n.Name.ToString()}\",");
        sb.AppendLine($"{indent}  \"children\": {c}");
        if (c > 0) { sb.AppendLine($"{indent}  \"child_types\": ["); for (int i = 0; i < c; i++) { var ch = n.GetChild(i); sb.Append($"{indent}    \"{ch.GetType().Name}\"{(i < c - 1 ? "," : "")}\n"); } sb.AppendLine($"{indent}  ]"); }
        sb.Append($"{indent}}}");
        return sb.ToString();
    }

    private string GetNodeProperties(string path)
    {
        var n = GetNodeOrNull(path);
        if (n == null) return $"{{\"error\":\"Node not found: {path}\"}}";
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"path\":\"{path}\",\"type\":\"{n.GetType().Name}\",\"name\":\"{n.Name.ToString()}\"");
        if (n is Node2D n2d) { sb.Append($",\"position\":\"{n2d.GlobalPosition}\""); sb.Append($",\"visible\":{n2d.Visible}"); }
        sb.Append("}");
        return sb.ToString();
    }

    private string SetNodeProperty(string path, string prop, string valJson)
    {
        var n = GetNodeOrNull(path);
        if (n == null) return $"{{\"error\":\"Node not found: {path}\"}}";
        try
        {
            var parsed = JsonNode.Parse(valJson);
            var val = parsed.AsValue().GetValue<object>();
            if (prop == "position" && val is JsonArray pa && pa.Count >= 2 && n is Node2D n2d) n2d.GlobalPosition = new Vector2(pa[0].GetValue<float>(), pa[1].GetValue<float>());
            else if (prop == "visible" && n is CanvasItem ci) ci.Visible = val is string s ? s.ToLower() == "true" : Convert.ToBoolean(val);
            else n.Set(new StringName(prop), Variant.From(val switch { float f => f, int i => (long)i, string s => s, bool b => b, _ => (object)val.ToString() }));
            return $"{{\"ok\":true,\"path\":\"{path}\",\"property\":\"{prop}\"}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string CallNodeMethod(string path, string method, string argsJson)
    {
        var n = GetNodeOrNull(path);
        if (n == null) return $"{{\"error\":\"Node not found: {path}\"}}";
        try
        {
            var arr = JsonNode.Parse(argsJson).AsArray();
            var ga = new Godot.Collections.Array();
            foreach (var a in arr) { var v = a.AsValue(); if (v.TryGetValue(out string s)) ga.Add(s); else if (v.TryGetValue(out float f)) ga.Add((double)f); else if (v.TryGetValue(out bool b)) ga.Add(b); else ga.Add(a.ToString()); }
            var result = n.Call(new StringName(method), ga);
            return $"{{\"ok\":true,\"result\":\"{result}\"}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string SimulateInputAction(string action, string key)
    {
        try { Input.ParseInputEvent(new InputEventAction { Action = new StringName(key), Pressed = action == "press" }); return $"{{\"ok\":true,\"action\":\"{action}\",\"key\":\"{key}\"}}"; }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string GetGameInfo()
    {
        var tree = GetTree();
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["engine_version"] = Engine.GetVersionInfo()["string"].Obj?.ToString() ?? Engine.GetVersionInfo()["string"].ToString(),
            ["fps"] = Engine.GetFramesPerSecond(),
            ["window_size"] = $"{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}",
            ["current_scene"] = tree?.CurrentScene?.SceneFilePath ?? "unknown",
            ["server_name"] = ServerName,
            ["tools_count"] = _tools.Count,
            ["metrics_count"] = _metrics.Count,
        });
    }

    // ── log tools ───────────────────────────────────────────────────────

    private string GetLogs(Dictionary<string, JsonElement> args, LogType type)
    {
        var (ring, head, count, cap) = type == LogType.Debug
            ? (_debugLogRing, _debugLogHead, _debugLogCount, DEBUG_LOG_CAPACITY)
            : (_aiLogRing, _aiLogHead, _aiLogCount, AI_LOG_CAPACITY);

        var minLevel = args.ContainsKey("min_level")
            ? Enum.Parse<LogLevel>(args["min_level"].GetString(), true)
            : (type == LogType.Debug ? LogLevel.Debug : LogLevel.Info);
        string catFilter = args.ContainsKey("category") ? args["category"].GetString()?.ToLowerInvariant() : null;
        double? since = args.ContainsKey("since") ? args["since"].GetDouble() : null;
        int limit = args.ContainsKey("limit") ? args["limit"].GetInt32() : 100;
        string order = args.ContainsKey("order") ? args["order"].GetString() : "desc";

        var matching = new List<LogEntry>();
        int start = (head - count + cap) % cap;
        for (int i = 0; i < count; i++)
        {
            int idx = (start + i) % cap;
            var e = ring[idx];
            if (e.Level < minLevel) continue;
            if (catFilter != null && !e.Category.ToLowerInvariant().Contains(catFilter)) continue;
            if (since.HasValue && e.Timestamp < since.Value) continue;
            matching.Add(e);
        }

        var result = order == "asc"
            ? matching.Skip(Math.Max(0, matching.Count - limit)).Take(limit)
            : Enumerable.Reverse(matching).Take(limit);

        var arr = new JsonArray();
        foreach (var e in result)
            arr.Add(new JsonObject { ["level"] = e.Level.ToString().ToUpperInvariant(), ["time"] = Math.Round(e.Timestamp, 3), ["category"] = e.Category, ["message"] = e.Message });

        return JsonSerializer.Serialize(new JsonObject { ["count"] = arr.Count, ["total_in_buffer"] = count, ["entries"] = arr });
    }

    private string AgentLog(Dictionary<string, JsonElement> args)
    {
        var msg = args["message"].GetString();
        var level = args.ContainsKey("level") ? Enum.Parse<LogLevel>(args["level"].GetString(), true) : LogLevel.Info;
        var category = args.ContainsKey("category") ? args["category"].GetString() : "agent";
        Log(category, msg, level);
        return $"{{\"ok\":true,\"category\":\"{category}\",\"level\":\"{level}\"}}";
    }

    private string GetFileLogs(Dictionary<string, JsonElement> args)
    {
        var category = args["category"].GetString();
        int limit = args.ContainsKey("limit") ? args["limit"].GetInt32() : 50;
        try
        {
            var dir = System.IO.Path.Combine(Godot.ProjectSettings.GlobalizePath("user://"), "mcp_logs");
            var files = System.IO.Directory.GetFiles(dir, $"{category}_*.log").OrderBy(f => f).ToList();
            if (files.Count == 0) return $"{{\"count\":0,\"entries\":[]}}";

            // Read last N lines from the latest file
            var lines = new List<string>();
            for (int fi = files.Count - 1; fi >= 0 && lines.Count < limit; fi--)
            {
                var fileLines = System.IO.File.ReadAllLines(files[fi]);
                for (int li = fileLines.Length - 1; li >= 0 && lines.Count < limit; li--)
                    if (!string.IsNullOrWhiteSpace(fileLines[li]))
                        lines.Add(fileLines[li]);
            }
            lines.Reverse();

            var arr = new JsonArray();
            foreach (var l in lines) arr.Add(l);
            return JsonSerializer.Serialize(new JsonObject { ["count"] = arr.Count, ["entries"] = arr });
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string GetFileLogSummary()
    {
        try
        {
            var dir = System.IO.Path.Combine(Godot.ProjectSettings.GlobalizePath("user://"), "mcp_logs");
            if (!System.IO.Directory.Exists(dir)) return "{\"categories\":[]}";
            var files = System.IO.Directory.GetFiles(dir, "*.log");
            var groups = files.GroupBy(f => System.IO.Path.GetFileName(f).Split('_')[0]);
            var arr = new JsonArray();
            foreach (var g in groups)
            {
                long totalSize = g.Sum(f => new System.IO.FileInfo(f).Length);
                int totalLines = g.Sum(f => System.IO.File.ReadAllLines(f).Length);
                arr.Add(new JsonObject { ["category"] = g.Key, ["files"] = g.Count(), ["entries"] = totalLines, ["size_bytes"] = totalSize });
            }
            return JsonSerializer.Serialize(new JsonObject { ["categories"] = arr });
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }
}

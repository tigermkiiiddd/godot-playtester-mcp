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
            "Get UI Control nodes as nested tree (like DOM). Each node has type, name, rect, children. Filter by type and visibility.",
            p("{\"visible_only\":{\"type\":\"boolean\"},\"types\":{\"type\":\"string\"}}", "object"),
            a => GetUILayout(a));

        // ── UI Control ───────────────────────────────────────────────────

        Reg("click_element", "Click a UI element by name or path. Uses element's rect center.",
            p("{\"name\":{\"type\":\"string\"},\"path\":{\"type\":\"string\"},\"button\":{\"type\":\"string\"},\"offset_x\":{\"type\":\"number\"},\"offset_y\":{\"type\":\"number\"}}", "object"),
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

        Reg("press_key", "Press or release a raw keyboard key (W, A, Space, Escape, Enter, F1, etc).",
            p("{\"key\":{\"type\":\"string\"},\"action\":{\"type\":\"string\"}}", "object"),
            a => PressKey(a["key"].GetString(), a["action"].GetString()));

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
        return JsonSerializer.Serialize(result, JsonOpts);
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

    // ── get_ui_layout (tree structure like DOM) ─────────────────────────

    private string GetUILayout(Dictionary<string, JsonElement> args)
    {
        bool visOnly = !args.ContainsKey("visible_only") || args["visible_only"].GetBoolean();
        string[] types = null;
        if (args.ContainsKey("types") && !string.IsNullOrEmpty(args["types"].GetString()))
            types = args["types"].GetString().Split(',');

        var tree = GetTree();
        if (tree?.Root == null) return "{\"error\":\"No scene tree\"}";

        var roots = new JsonArray();
        foreach (var child in tree.Root.GetChildren())
        {
            if (child == this) continue;
            var sub = BuildUITreeNode(child, visOnly, types, 0);
            if (sub != null) roots.Add(sub);
        }
        return JsonSerializer.Serialize(new JsonObject { ["roots"] = roots }, JsonOpts);
    }

    private JsonNode BuildUITreeNode(Node node, bool visOnly, string[] types, int depth)
    {
        if (node == null || node == this) return null;

        if (node is Control c)
        {
            if (visOnly && !c.Visible) return null;
            if (types != null && !types.Any(t => node.GetType().Name.Contains(t.Trim(), StringComparison.OrdinalIgnoreCase))) return null;

            var e = new JsonObject
            {
                ["type"] = node.GetType().Name,
                ["name"] = c.Name.ToString(),
                ["path"] = c.GetPath().ToString(),
                ["visible"] = c.Visible,
                ["depth"] = depth
            };
            var r = c.GetGlobalRect();
            e["rect"] = new JsonArray { Math.Round(r.Position.X, 0), Math.Round(r.Position.Y, 0), Math.Round(r.Size.X, 0), Math.Round(r.Size.Y, 0) };

            // Focus
            e["focused"] = c.HasFocus();

            // Editable (LineEdit / TextEdit)
            if (c is LineEdit le) { e["editable"] = le.Editable; e["text"] = le.Text; }
            else if (c is TextEdit te) { e["editable"] = te.Editable; e["text"] = te.Text; }

            // Mouse filter
            e["mouse_filter"] = c.MouseFilter.ToString();

            // Disabled
            if (c is BaseButton bb) e["disabled"] = bb.Disabled;

            // Type-specific fields
            string textContent = null;
            switch (c)
            {
                case CheckBox cb: e["pressed"] = cb.ButtonPressed; e["text"] = cb.Text; textContent = cb.Text; break;
                case OptionButton ob: e["selected"] = ob.Selected; e["item_count"] = ob.ItemCount; e["text"] = ob.Text; textContent = ob.Text; break;
                case Button b: e["text"] = b.Text; textContent = b.Text; break;
                case Label l: e["text"] = l.Text; textContent = l.Text; break;
                case ProgressBar pb: e["value"] = Math.Round(pb.Value, 1); e["max_value"] = Math.Round(pb.MaxValue, 1); break;
                case Slider s: e["value"] = Math.Round(s.Value, 2); e["min_value"] = Math.Round(s.MinValue, 2); e["max_value"] = Math.Round(s.MaxValue, 2); break;
                case ItemList il: e["item_count"] = il.ItemCount; break;
                case SpinBox sb: e["value"] = Math.Round(sb.Value, 2); e["min_value"] = Math.Round(sb.MinValue, 2); e["max_value"] = Math.Round(sb.MaxValue, 2); break;
                case TabBar tb: e["tab_count"] = tb.TabCount; e["current_tab"] = tb.CurrentTab; break;
            }

            // Font size + text rendered size (for controls with text)
            try
            {
                var fontSize = c.GetThemeFontSize("font_size");
                if (fontSize > 0)
                {
                    e["font_size"] = fontSize;
                    if (!string.IsNullOrEmpty(textContent))
                    {
                        var font = c.GetThemeFont("font");
                        var textSize = font.GetStringSize(textContent, HorizontalAlignment.Left, -1, fontSize);
                        e["text_size"] = new JsonArray { Math.Round(textSize.X, 0), Math.Round(textSize.Y, 0) };
                    }
                }
            }
            catch { }

            // Custom control domain data via SetMeta("mcp_data", jsonString)
            if (c.HasMeta("mcp_data"))
            {
                var v = c.GetMeta("mcp_data");
                if (v.VariantType == Variant.Type.String)
                {
                    try
                    {
                        var parsed = JsonNode.Parse(v.AsString());
                        if (parsed is JsonObject obj) e["data"] = obj;
                    }
                    catch { }
                }
            }

            // Recurse into children
            var childArr = new JsonArray();
            int childCount = 0;
            foreach (var ch in c.GetChildren())
            {
                var childNode = BuildUITreeNode(ch, visOnly, types, depth + 1);
                if (childNode != null) { childArr.Add(childNode); childCount++; }
            }
            e["children_count"] = childCount;
            if (childCount > 0) e["children"] = childArr;

            return e;
        }
        else
        {
            // Non-Control node: recurse looking for Control descendants
            var childNodes = new List<JsonNode>();
            foreach (var ch in node.GetChildren())
            {
                var childNode = BuildUITreeNode(ch, visOnly, types, depth);
                if (childNode != null) childNodes.Add(childNode);
            }
            // Merge if only one child (skip non-UI intermediate nodes)
            if (childNodes.Count == 1) return childNodes[0];
            if (childNodes.Count > 1)
            {
                var arr = new JsonArray();
                foreach (var cn in childNodes) arr.Add(cn);
                return new JsonObject { ["children"] = arr };
            }
            return null;
        }
    }

    // ── UI Control Tools ─────────────────────────────────────────────────

    private string ClickElement(Dictionary<string, JsonElement> args)
    {
        var control = FindUIElement(args);
        if (control == null) return "{\"error\":\"Element not found. Provide 'name' or 'path'.\"}";

        var rect = control.GetGlobalRect();
        float ox = args.ContainsKey("offset_x") ? args["offset_x"].GetSingle() : 0f;
        float oy = args.ContainsKey("offset_y") ? args["offset_y"].GetSingle() : 0f;
        float x = (float)Math.Round(rect.Position.X + rect.Size.X / 2 + ox, 0);
        float y = (float)Math.Round(rect.Position.Y + rect.Size.Y / 2 + oy, 0);

        // Move virtual cursor
        _simMousePos = new Vector2(x, y);

        // For buttons, directly emit pressed signal (bypasses CanvasLayer routing issues)
        if (control is BaseButton bb)
        {
            bb.EmitSignal(BaseButton.SignalName.Pressed);
        }
        else
        {
            // Non-button: try Input.ParseInputEvent (works for controls not on CanvasLayer)
            string button = args.ContainsKey("button") ? args["button"].GetString() : "left";
            var btnIdx = button.ToLowerInvariant() switch { "right" => MouseButton.Right, "middle" => MouseButton.Middle, _ => MouseButton.Left };
            Input.ParseInputEvent(new InputEventMouseButton { Pressed = true, Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y), ButtonIndex = btnIdx });
            Input.ParseInputEvent(new InputEventMouseButton { Pressed = false, Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y), ButtonIndex = btnIdx });
        }

        return $"{{\"ok\":true,\"name\":\"{control.Name}\",\"x\":{x},\"y\":{y}}}";
    }

    private string TypeText(Dictionary<string, JsonElement> args)
    {
        var target = args["target"].GetString();
        var text = args["text"].GetString();
        var mode = args.ContainsKey("mode") ? args["mode"].GetString() : "set";

        // Find by name or path
        var control = FindControlByName(target) ?? FindControlByPath(target);
        if (control == null) return $"{{\"error\":\"Text target not found: {target}\"}}";

        if (mode == "set")
        {
            if (control is LineEdit le) le.Text = text;
            else if (control is TextEdit te) te.Text = text;
            else return $"{{\"error\":\"Element {control.Name} is not a text input (type={control.GetType().Name})\"}}";
            return $"{{\"ok\":true,\"target\":\"{target}\",\"text\":\"{text}\",\"mode\":\"set\"}}";
        }

        // type mode: simulate keystrokes
        if (control is LineEdit || control is TextEdit)
        {
            control.GrabFocus();
            foreach (var ch in text)
            {
                var kc = MapCharToKey(ch);
                Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = kc, Unicode = (uint)ch });
                Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = kc, Unicode = (uint)ch });
            }
            return $"{{\"ok\":true,\"target\":\"{target}\",\"text\":\"{text}\",\"mode\":\"type\"}}";
        }
        return $"{{\"error\":\"Element {control.Name} is not a text input\"}}";
    }

    private string GetFocusedElement()
    {
        var tree = GetTree();
        if (tree?.Root == null) return "{\"error\":\"No scene tree\"}";
        var focused = FindFocusedControl(tree.Root);
        if (focused == null) return "{\"focused\":false}";
        var r = focused.GetGlobalRect();
        var o = new JsonObject
        {
            ["focused"] = true,
            ["type"] = focused.GetType().Name,
            ["name"] = focused.Name.ToString(),
            ["path"] = focused.GetPath().ToString(),
            ["rect"] = new JsonArray { Math.Round(r.Position.X, 0), Math.Round(r.Position.Y, 0), Math.Round(r.Size.X, 0), Math.Round(r.Size.Y, 0) }
        };
        return JsonSerializer.Serialize(o, JsonOpts);
    }

    private string SelectOption(Dictionary<string, JsonElement> args)
    {
        int index = args["index"].GetInt32();
        var control = FindUIElement(args);
        if (control == null) return "{\"error\":\"Element not found\"}";

        if (control is OptionButton ob)
        {
            if (index < 0 || index >= ob.ItemCount) return $"{{\"error\":\"Index {index} out of range (0-{ob.ItemCount - 1})\"}}";
            ob.Select(index);
            return $"{{\"ok\":true,\"selected\":{index},\"text\":\"{ob.GetItemText(index)}\"}}";
        }
        if (control is ItemList il)
        {
            if (index < 0 || index >= il.ItemCount) return $"{{\"error\":\"Index {index} out of range (0-{il.ItemCount - 1})\"}}";
            il.Select(index);
            return $"{{\"ok\":true,\"selected\":{index}}}";
        }
        return $"{{\"error\":\"Element {control.Name} is not OptionButton/ItemList (type={control.GetType().Name})\"}}";
    }

    private string Hover(float x, float y)
    {
        try
        {
            _simMousePos = new Vector2(x, y);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y) });
            return $"{{\"ok\":true,\"x\":{x},\"y\":{y}}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string DoubleClick(string button, float x, float y)
    {
        try
        {
            _simMousePos = new Vector2(x, y);
            var btn = button.ToLowerInvariant() switch { "right" => MouseButton.Right, "middle" => MouseButton.Middle, _ => MouseButton.Left };
            for (int i = 0; i < 2; i++)
            {
                Input.ParseInputEvent(new InputEventMouseButton { Pressed = true, Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y), ButtonIndex = btn, DoubleClick = i == 1 });
                Input.ParseInputEvent(new InputEventMouseButton { Pressed = false, Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y), ButtonIndex = btn });
            }
            return $"{{\"ok\":true,\"button\":\"{button}\",\"x\":{x},\"y\":{y}}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string DragMouse(Dictionary<string, JsonElement> args)
    {
        float fromX = args["from_x"].GetSingle();
        float fromY = args["from_y"].GetSingle();
        float toX = args["to_x"].GetSingle();
        float toY = args["to_y"].GetSingle();
        float duration = args.ContainsKey("duration") ? args["duration"].GetSingle() : 0.3f;
        string button = args.ContainsKey("button") ? args["button"].GetString() : "left";

        // Use macro system for frame-by-frame drag
        var id = $"macro_{++_macroCounter:D3}";
        var steps = new List<MacroStep>
        {
            new MacroStep { Type = MacroStepType.Drag, X = fromX, Y = fromY, TargetX = toX, TargetY = toY, Duration = duration, Button = button }
        };
        _macros[id] = new MacroRun
        {
            Id = id, Name = "drag", Steps = steps, Status = "running",
            StartTime = Time.GetTicksMsec() / 1000.0
        };
        return $"{{\"ok\":true,\"macro_id\":\"{id}\",\"from\":[{fromX},{fromY}],\"to\":[{toX},{toY}],\"duration\":{duration}}}";
    }

    // ── UI helpers ───────────────────────────────────────────────────────

    private Control FindUIElement(Dictionary<string, JsonElement> args)
    {
        if (args.ContainsKey("path") && !string.IsNullOrEmpty(args["path"].GetString()))
            return FindControlByPath(args["path"].GetString());
        if (args.ContainsKey("name") && !string.IsNullOrEmpty(args["name"].GetString()))
            return FindControlByName(args["name"].GetString());
        return null;
    }

    private Control FindControlByName(string name)
    {
        var tree = GetTree();
        if (tree?.Root == null) return null;
        return FindControlByNameRecursive(tree.Root, name);
    }

    private Control FindControlByNameRecursive(Node node, string name)
    {
        if (node is Control c && c.Name.ToString() == name) return c;
        foreach (var child in node.GetChildren())
        {
            var found = FindControlByNameRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private Control FindControlByPath(string path)
    {
        try { return GetNodeOrNull<Control>(path); }
        catch { return null; }
    }

    private static Control FindFocusedControl(Node node)
    {
        if (node is Control c && c.HasFocus()) return c;
        foreach (var child in node.GetChildren())
        {
            var found = FindFocusedControl(child);
            if (found != null) return found;
        }
        return null;
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

    // ── screenshot ───────────────────────────────────────────────────────

    private string TakeScreenshot(string format, int quality)
    {
        try
        {
            var img = GetViewport().GetTexture().GetImage();
            byte[] buf = format == "png" ? img.SavePngToBuffer() : img.SaveJpgToBuffer();
            return JsonSerializer.Serialize(new JsonObject { ["format"] = format, ["width"] = img.GetWidth(), ["height"] = img.GetHeight(), ["size_bytes"] = buf.Length, ["data"] = Convert.ToBase64String(buf) }, JsonOpts);
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
        return JsonSerializer.Serialize(result, JsonOpts);
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
        _healthLabel = new Label { Name = "McpHealthLabel" };
        _healthLabel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _healthLabel.OffsetLeft = 10;
        _healthLabel.OffsetTop = -24;
        _healthLabel.OffsetRight = -10;
        _healthLabel.OffsetBottom = -2;
        _healthLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.6f, 0.6f));
        _healthLabel.AddThemeFontSizeOverride("font_size", 12);
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

    // ── virtual cursor ──────────────────────────────────────────────────

    private void CreateVirtualCursor()
    {
        if (!ShowCursor) return;
        _cursorLayer = new CanvasLayer { Name = "McpCursorLayer", Layer = 1000 };

        // Crosshair: two colored lines forming a + shape
        _cursorCross = new Control { Name = "McpCursor" };
        _cursorCross.SetAnchorsPreset(Control.LayoutPreset.TopLeft);

        // Horizontal bar (32px wide, 4px tall)
        var hBar = new ColorRect
        {
            Name = "HBar",
            Color = new Color(1f, 0.2f, 0.8f, 0.95f),
            Size = new Vector2(32, 4),
            Position = new Vector2(-16, -2)
        };
        _cursorCross.AddChild(hBar);

        // Vertical bar (4px wide, 32px tall)
        var vBar = new ColorRect
        {
            Name = "VBar",
            Color = new Color(1f, 0.2f, 0.8f, 0.95f),
            Size = new Vector2(4, 32),
            Position = new Vector2(-2, -16)
        };
        _cursorCross.AddChild(vBar);

        // Center dot (6x6)
        var dot = new ColorRect
        {
            Name = "Dot",
            Color = new Color(1f, 1f, 1f, 1f),
            Size = new Vector2(6, 6),
            Position = new Vector2(-3, -3)
        };
        _cursorCross.AddChild(dot);

        _cursorLayer.AddChild(_cursorCross);
        _mainQueue.Enqueue(() => GetTree().Root.CallDeferred("add_child", _cursorLayer));
    }

    private void UpdateVirtualCursor()
    {
        if (!ShowCursor) return;
        if (_cursorCross == null) CreateVirtualCursor();
        if (_cursorCross == null) return;
        _cursorCross.Position = _simMousePos;
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
        if (include == "all" || include == "screenshots") { r["screenshot_count"] = t.Screenshots.Count; r["screenshots"] = JsonSerializer.SerializeToNode(t.Screenshots, JsonOpts); }
        if (include == "all" || include == "logs")
        {
            r["logs"] = JsonSerializer.SerializeToNode(t.Logs, JsonOpts);
            r["assert_results"] = new JsonObject { ["passed"] = t.AssertPassed, ["failed"] = t.AssertFailed, ["failures"] = JsonSerializer.SerializeToNode(t.AssertFailures, JsonOpts) };
        }
        return JsonSerializer.Serialize(r, JsonOpts);
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
        }, JsonOpts);
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

        return JsonSerializer.Serialize(new JsonObject { ["count"] = arr.Count, ["total_in_buffer"] = count, ["entries"] = arr }, JsonOpts);
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
            var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "debug_logs");
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
            return JsonSerializer.Serialize(new JsonObject { ["count"] = arr.Count, ["entries"] = arr }, JsonOpts);
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string GetFileLogSummary()
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "debug_logs");
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
            return JsonSerializer.Serialize(new JsonObject { ["categories"] = arr }, JsonOpts);
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }
}

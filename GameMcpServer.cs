using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// GameMcpServer — exposes a running Godot game to AI agents via the MCP protocol.
///
/// Setup:
///   1. Copy this file into your project (e.g. addons/game_mcp/GameMcpServer.cs)
///   2. In Project → Project Settings → Autoload, add GameMcpServer
///   3. The server starts automatically when the game runs
///
/// Game Development Protocol:
///   - Tag game objects with Godot Groups: "player", "enemies", "npcs", "items", etc.
///   - Name UI Control nodes so MCP can extract layout info
///   - Register metrics for numeric monitoring
/// </summary>
public partial class GameMcpServer : Node
{
    public static GameMcpServer Instance { get; private set; }

    [Export] public int Port = 9876;
    [Export] public bool Enabled = true;
    [Export] public string ServerName = "godot-playtester";
    [Export] public NodePath PlayerPath;

    private HttpListener _listener;
    private Thread _serverThread;
    private volatile bool _running;
    private readonly List<McpTool> _tools = new();
    private readonly Dictionary<string, Func<Dictionary<string, JsonElement>, object>> _handlers = new();

    // Metrics
    private readonly Dictionary<string, MetricEntry> _metrics = new();
    private readonly Dictionary<string, List<MetricSample>> _metricHistory = new();
    private const int MAX_HISTORY_SECONDS = 60;
    private const int MAX_TEST_HISTORY_SECONDS = 600;

    // Test Runner
    private readonly Dictionary<string, TestRun> _tests = new();
    private int _testCounter;

    // Log capture
    private readonly List<string> _capturedLogs = new();
    private bool _captureLogs;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        if (!Enabled) return;
        RegisterBuiltinTools();
        StartServer();
        GD.Print($"[GameMcp] Playtester MCP server listening on http://localhost:{Port}");
    }

    public override void _ExitTree()
    {
        StopServer();
        Instance = null;
    }

    public override void _Process(double delta)
    {
        SampleMetrics(delta);
        UpdateTests(delta);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════════════════

    public void RegisterTool(string name, string description,
        Func<Dictionary<string, JsonElement>, object> handler)
    {
        var tool = new McpTool
        {
            Name = name, Description = description,
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["required"] = new JsonArray(),
            }
        };
        _tools.Add(tool);
        _handlers[name] = handler;
        GD.Print($"[GameMcp] Registered tool: {name}");
    }

    public void RegisterTool(string name, string description,
        JsonObject inputSchema, Func<Dictionary<string, JsonElement>, object> handler)
    {
        var tool = new McpTool { Name = name, Description = description, InputSchema = inputSchema };
        _tools.Add(tool);
        _handlers[name] = handler;
        GD.Print($"[GameMcp] Registered tool: {name}");
    }

    /// <summary>Register a metric with a getter function.</summary>
    public void RegisterMetric(string name, Func<object> getter, double sampleRate = 1.0)
    {
        _metrics[name] = new MetricEntry { Getter = getter, SampleRate = sampleRate };
        _metricHistory[name] = new List<MetricSample>();
        GD.Print($"[GameMcp] Registered metric: {name}");
    }

    /// <summary>Register a metric that reads a node property.</summary>
    public void RegisterMetric(string name, string nodePath, string propertyName, double sampleRate = 1.0)
    {
        RegisterMetric(name, () =>
        {
            var node = GetNodeOrNull(nodePath);
            return node == null ? "node_not_found" : node.Get(propertyName);
        }, sampleRate);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HTTP SERVER
    // ═══════════════════════════════════════════════════════════════════════

    private void StartServer()
    {
        _running = true;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _serverThread = new Thread(ServerLoop) { IsBackground = true };
        _serverThread.Start();
    }

    private void StopServer()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    private void ServerLoop()
    {
        while (_running)
        {
            try
            {
                var context = _listener.GetContext();
                HandleRequestAsync(context).GetAwaiter().GetResult();
            }
            catch (HttpListenerException) when (!_running) { }
            catch (Exception e) { if (_running) GD.PrintErr($"[GameMcp] {e.Message}"); }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (context.Request.HttpMethod == "OPTIONS") { response.StatusCode = 200; response.Close(); return; }
        if (context.Request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            var body = Encoding.UTF8.GetBytes("{\"error\":\"Method not allowed\"}");
            response.ContentType = "application/json"; response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body); response.Close(); return;
        }

        string requestBody;
        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
            requestBody = await reader.ReadToEndAsync();

        string responseBody;
        try { responseBody = ProcessMcpMessage(requestBody); }
        catch (Exception e) { responseBody = RpcError(null, -32603, e.Message); }

        var bytes = Encoding.UTF8.GetBytes(responseBody);
        response.StatusCode = 200; response.ContentType = "application/json"; response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes); response.Close();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MCP PROTOCOL
    // ═══════════════════════════════════════════════════════════════════════

    private string ProcessMcpMessage(string json)
    {
        var node = JsonNode.Parse(json).AsObject();
        var method = node["method"]?.GetValue<string>() ?? "";
        var id = node["id"];
        var @params = node["params"]?.AsObject();

        string result = method switch
        {
            "initialize" => HandleInit(@params),
            "notifications/initialized" => "",
            "tools/list" => HandleToolsList(),
            "tools/call" => HandleToolsCall(@params),
            "ping" => "{}",
            _ => RpcError(id, -32601, $"Unknown method: {method}")
        };

        return method == "notifications/initialized" ? "" : RpcResult(id, result);
    }

    private string HandleInit(JsonObject p)
    {
        var name = p?["clientInfo"]?["name"]?.GetValue<string>() ?? "unknown";
        GD.Print($"[GameMcp] Client connected: {name}");
        return JsonSerializer.Serialize(new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "2.0.0" }
        });
    }

    private string HandleToolsList()
    {
        var arr = new JsonArray();
        foreach (var t in _tools) arr.Add(JsonSerializer.SerializeToNode(t));
        return JsonSerializer.Serialize(new JsonObject { ["tools"] = arr });
    }

    private string HandleToolsCall(JsonObject p)
    {
        var toolName = p?["name"]?.GetValue<string>() ?? "";
        var args = new Dictionary<string, JsonElement>();
        if (p?["arguments"] is JsonObject ao)
            foreach (var kv in ao) args[kv.Key] = kv.Value.GetValue<JsonElement>();

        if (!_handlers.TryGetValue(toolName, out var handler))
            throw new Exception($"Unknown tool: {toolName}");

        var result = ExecuteOnMainThread(handler, args);
        return JsonSerializer.Serialize(new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result?.ToString() ?? "null" } }
        });
    }

    // ── Thread Safety ───────────────────────────────────────────────────

    private T ExecuteOnMainThread<T>(Func<Dictionary<string, JsonElement>, T> func, Dictionary<string, JsonElement> args)
    {
        T result = default; Exception ex = null;
        var done = new ManualResetEventSlim(false);
        CallDeferred(() => { try { result = func(args); } catch (Exception e) { ex = e; } finally { done.Set(); } });
        done.Wait(10000);
        if (ex != null) throw ex;
        return result;
    }

    // ── JSON-RPC ────────────────────────────────────────────────────────

    private static string RpcResult(JsonNode id, string result) =>
        JsonSerializer.Serialize(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = JsonNode.Parse(result) });

    private static string RpcError(JsonNode id, int code, string msg) =>
        JsonSerializer.Serialize(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = msg } });

    // ═══════════════════════════════════════════════════════════════════════
    //  TOOL REGISTRATION
    // ═══════════════════════════════════════════════════════════════════════

    private void RegisterBuiltinTools()
    {
        // ── Original 6 ───────────────────────────────────────────────────

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

        // ── New: Game State & UI ────────────────────────────────────────

        Reg("get_game_state",
            "Get structured game world state by Godot Groups. Returns objects with world/screen positions, distances. Supports filtering by group, area, radius, and pagination.",
            p("{\"groups\":{\"type\":\"string\"},\"radius\":{\"type\":\"number\"},\"near_x\":{\"type\":\"number\"},\"near_y\":{\"type\":\"number\"},\"near_radius\":{\"type\":\"number\"},\"area_x\":{\"type\":\"number\"},\"area_y\":{\"type\":\"number\"},\"area_w\":{\"type\":\"number\"},\"area_h\":{\"type\":\"number\"},\"limit\":{\"type\":\"integer\"},\"offset\":{\"type\":\"integer\"}}", "object"),
            a => GetGameState(a));

        Reg("get_ui_layout",
            "Get all UI Control nodes with layout info (type, name, rect, text, value). Filter by type and visibility.",
            p("{\"visible_only\":{\"type\":\"boolean\"},\"types\":{\"type\":\"string\"}}", "object"),
            a => GetUILayout(a));

        // ── New: Input ───────────────────────────────────────────────────

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

        // ── New: Screenshot ──────────────────────────────────────────────

        Reg("screenshot",
            "Capture current frame as base64 image. Prefer get_game_state/get_ui_layout for structured data — use screenshot only when you need to see the visuals.",
            p("{\"format\":{\"type\":\"string\"},\"quality\":{\"type\":\"integer\"}}", "object"),
            a => TakeScreenshot(
                a.ContainsKey("format") ? a["format"].GetString() : "jpeg",
                a.ContainsKey("quality") ? a["quality"].GetInt32() : 80));

        // ── New: Metrics ─────────────────────────────────────────────────

        Reg("register_metric", "Register a numeric metric for monitoring.",
            p("{\"name\":{\"type\":\"string\"},\"source_type\":{\"type\":\"string\"},\"node_path\":{\"type\":\"string\"},\"property_name\":{\"type\":\"string\"},\"sample_rate\":{\"type\":\"number\"}}", "object", new[] { "name" }),
            a => RegisterMetricViaMcp(a));

        Reg("get_metrics", "Get registered metric values. Formats: latest, timeline, csv.",
            p("{\"names\":{\"type\":\"string\"},\"format\":{\"type\":\"string\"}}", "object"),
            a => GetMetrics(a));

        // ── New: Test Runner ─────────────────────────────────────────────

        Reg("start_test",
            "Start a background test: run a scene, collect metrics and optionally capture frames. Returns test_id.",
            p("{\"scene_path\":{\"type\":\"string\"},\"duration\":{\"type\":\"number\"},\"capture_frames\":{\"type\":\"number\"}}", "object", new[] { "scene_path" }),
            a => StartTest(a));

        Reg("get_test_results",
            "Get results from a background test: metrics timeline, screenshots, logs.",
            p("{\"test_id\":{\"type\":\"string\"},\"include\":{\"type\":\"string\"}}", "object", new[] { "test_id" }),
            a => GetTestResults(a["test_id"].GetString(),
                a.ContainsKey("include") ? a["include"].GetString() : "all"));
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

        // Parse filters
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

        if (player is Node2D p2d)
        {
            result["player"] = BuildNodeInfo(p2d, camera, p2d);
        }

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

            result[g] = new JsonObject
            {
                ["total"] = filtered.Count,
                ["returned"] = paged.Count,
                ["items"] = items
            };
        }

        return JsonSerializer.Serialize(result);
    }

    private static JsonNode BuildNodeInfo(Node2D node, Camera2D camera, Node2D player)
    {
        var o = new JsonObject
        {
            ["name"] = node.Name,
            ["type"] = node.GetType().Name,
            ["path"] = node.GetPath(),
            ["world_pos"] = new JsonArray { Math.Round(node.GlobalPosition.X, 1), Math.Round(node.GlobalPosition.Y, 1) }
        };
        if (camera != null)
        {
            var sp = camera.UnprojectPosition(node.GlobalPosition);
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

                var e = new JsonObject
                {
                    ["type"] = child.GetType().Name,
                    ["name"] = c.Name,
                    ["visible"] = c.Visible,
                    ["disabled"] = c.Disabled
                };
                var r = c.GetGlobalRect();
                e["rect"] = new JsonArray { Math.Round(r.Position.X, 0), Math.Round(r.Position.Y, 0), Math.Round(r.Size.X, 0), Math.Round(r.Size.Y, 0) };

                switch (child)
                {
                    case Button b: e["text"] = b.Text; break;
                    case Label l: e["text"] = l.Text; break;
                    case ProgressBar pb: e["value"] = Math.Round(pb.Value, 1); e["max_value"] = Math.Round(pb.MaxValue, 1); break;
                    case LineEdit le: e["text"] = le.Text; break;
                    case Slider s: e["value"] = Math.Round(s.Value, 2); e["min_value"] = Math.Round(s.MinValue, 2); e["max_value"] = Math.Round(s.MaxValue, 2); break;
                    case CheckBox cb: e["pressed"] = cb.ButtonPressed; break;
                    case ItemList il: e["item_count"] = il.ItemCount; break;
                    case OptionButton ob: e["selected"] = ob.Selected; e["item_count"] = ob.ItemCount; break;
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
            byte[] buf = new byte[img.GetWidth() * img.GetHeight() * 4];
            if (format == "png")
                buf = img.SavePngToBuffer();
            else
                img.SaveJpgToBuffer(buf, quality);

            return JsonSerializer.Serialize(new JsonObject
            {
                ["format"] = format,
                ["width"] = img.GetWidth(),
                ["height"] = img.GetHeight(),
                ["size_bytes"] = buf.Length,
                ["data"] = Convert.ToBase64String(buf)
            });
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    // ── press_key ────────────────────────────────────────────────────────

    private string PressKey(string key, string action)
    {
        try
        {
            var ev = new InputEventKey { Pressed = action == "press", Keycode = MapKey(key) };
            Input.ParseInputEvent(ev); Input.AccumulateInput(ev);
            GetTree()?.Root?.PropagateInputEvent(ev);
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

    // ── click_mouse ──────────────────────────────────────────────────────

    private string ClickMouse(string button, string action, float x, float y)
    {
        try
        {
            var ev = new InputEventMouseButton
            {
                Pressed = action == "press",
                Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y),
                ButtonIndex = button.ToLowerInvariant() switch { "right" => MouseButton.Right, "middle" => MouseButton.Middle, _ => MouseButton.Left }
            };
            Input.ParseInputEvent(ev); Input.AccumulateInput(ev);
            GetTree()?.Root?.PropagateInputEvent(ev);
            return $"{{\"ok\":true,\"button\":\"{button}\",\"x\":{x},\"y\":{y}}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    // ── move_mouse ───────────────────────────────────────────────────────

    private string MoveMouse(float x, float y)
    {
        try
        {
            var ev = new InputEventMouseMotion { Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y) };
            Input.ParseInputEvent(ev); Input.AccumulateInput(ev);
            GetTree()?.Root?.PropagateInputEvent(ev);
            return $"{{\"ok\":true,\"x\":{x},\"y\":{y}}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    // ── scroll_mouse ─────────────────────────────────────────────────────

    private string ScrollMouse(int amount)
    {
        try
        {
            var idx = amount > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            for (int i = 0; i < Math.Abs(amount); i++)
            {
                var ev = new InputEventMouseButton { ButtonIndex = idx, Pressed = true };
                Input.ParseInputEvent(ev); Input.AccumulateInput(ev);
            }
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
                if (!_metricHistory.ContainsKey(kv.Key))
                    _metricHistory[kv.Key] = new List<MetricSample>();
                var h = _metricHistory[kv.Key];
                h.Add(new MetricSample(Time.GetTicksMsec() / 1000.0, val));

                var maxSec = MAX_HISTORY_SECONDS;
                foreach (var t in _tests.Values)
                    if (t.Status == "running") maxSec = Math.Max(maxSec, MAX_TEST_HISTORY_SECONDS);
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
        _captureLogs = true;
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

            // Capture frames
            if (t.CaptureFramesInterval > 0)
            {
                t.FrameCaptureTimer += delta;
                if (t.FrameCaptureTimer >= t.CaptureFramesInterval)
                {
                    t.FrameCaptureTimer = 0;
                    try
                    {
                        var img = GetViewport().GetTexture().GetImage();
                        var buf = new byte[img.GetWidth() * img.GetHeight() * 4];
                        img.SaveJpgToBuffer(buf, 70);
                        t.Screenshots.Add(Convert.ToBase64String(buf));
                    }
                    catch { }
                }
            }

            // Capture logs
            if (_capturedLogs.Count > 0) { t.Logs.AddRange(_capturedLogs); _capturedLogs.Clear(); }

            // Timeout
            if (elapsed >= t.MaxDuration)
            {
                t.Status = "completed"; t.Duration = elapsed; _captureLogs = false;

                foreach (var mk in _metricHistory)
                    t.MetricsSnapshot[mk.Key] = new List<MetricSample>(mk.Value);

                int pass = 0, fail = 0; var failures = new List<string>();
                foreach (var log in t.Logs)
                {
                    if (log.Contains("ASSERT PASS")) pass++;
                    if (log.Contains("ASSERT FAIL")) { fail++; failures.Add(log); }
                }
                t.AssertPassed = pass; t.AssertFailed = fail; t.AssertFailures = failures;
                GD.Print($"[GameMcp] Test done: {t.Id} duration={elapsed:F1}s asserts={pass}p/{fail}f");
            }
        }
    }

    private string GetTestResults(string testId, string include)
    {
        if (!_tests.TryGetValue(testId, out var t))
            return $"{{\"error\":\"Test not found: {testId}\"}}";

        var r = new JsonObject
        {
            ["test_id"] = t.Id, ["status"] = t.Status,
            ["scene"] = t.ScenePath, ["duration_sec"] = Math.Round(t.Duration, 1)
        };

        if (include == "all" || include == "metrics")
        {
            var metrics = new JsonObject();
            var final = new JsonObject();
            foreach (var kv in t.MetricsSnapshot)
            {
                var tl = new JsonArray();
                foreach (var s in kv.Value) tl.Add(new JsonArray { Math.Round(s.Time, 2), FmtVal(s.Value) });
                metrics[kv.Key] = tl;
                if (kv.Value.Count > 0) final[kv.Key] = FmtVal(kv.Value[^1].Value);
            }
            r["metrics_timeline"] = metrics;
            r["final_values"] = final;
        }

        if (include == "all" || include == "screenshots")
        {
            r["screenshot_count"] = t.Screenshots.Count;
            r["screenshots"] = JsonSerializer.SerializeToNode(t.Screenshots);
        }

        if (include == "all" || include == "logs")
        {
            r["logs"] = JsonSerializer.SerializeToNode(t.Logs);
            r["assert_results"] = new JsonObject
            {
                ["passed"] = t.AssertPassed, ["failed"] = t.AssertFailed,
                ["failures"] = JsonSerializer.SerializeToNode(t.AssertFailures)
            };
        }

        return JsonSerializer.Serialize(r);
    }

    // ── original implementations ─────────────────────────────────────────

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
        sb.AppendLine($"{indent}  \"path\": \"{n.GetPath()}\",");
        sb.AppendLine($"{indent}  \"type\": \"{n.GetType().Name}\",");
        sb.AppendLine($"{indent}  \"name\": \"{n.Name}\",");
        sb.AppendLine($"{indent}  \"children\": {c}");
        if (c > 0)
        {
            sb.AppendLine($"{indent}  \"child_types\": [");
            for (int i = 0; i < c; i++)
            {
                var ch = n.GetChild(i);
                sb.Append($"{indent}    \"{ch.GetType().Name}\"{(i < c - 1 ? "," : "")}\n");
            }
            sb.AppendLine($"{indent}  ]");
        }
        sb.Append($"{indent}}}");
        return sb.ToString();
    }

    private string GetNodeProperties(string path)
    {
        var n = GetNodeOrNull(path);
        if (n == null) return $"{{\"error\":\"Node not found: {path}\"}}";
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"path\":\"{path}\",\"type\":\"{n.GetType().Name}\",\"name\":\"{n.Name}\"");
        if (n is Node2D n2d)
        {
            sb.Append($",\"position\":\"{n2d.GlobalPosition}\"");
            sb.Append($",\"visible\":{n2d.Visible}");
        }
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
            if (prop == "position" && val is JsonArray pa && pa.Count >= 2 && n is Node2D n2d)
                n2d.GlobalPosition = new Vector2(pa[0].GetValue<float>(), pa[1].GetValue<float>());
            else if (prop == "visible")
                n.Visible = val is string s ? s.ToLower() == "true" : Convert.ToBoolean(val);
            else
                n.Set(prop, val switch { float f => f, int i => (long)i, string s => s, _ => val });
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
            foreach (var a in arr)
            {
                var v = a.AsValue();
                if (v.TryGetValue(out string s)) ga.Add(s);
                else if (v.TryGetValue(out float f)) ga.Add((double)f);
                else if (v.TryGetValue(out bool b)) ga.Add(b);
                else ga.Add(a.ToString());
            }
            var result = n.Call(method, ga);
            return $"{{\"ok\":true,\"result\":\"{result}\"}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string SimulateInputAction(string action, string key)
    {
        try
        {
            var ev = new InputEventAction { Action = new StringName(key), Pressed = action == "press" };
            Input.ParseInputEvent(ev); Input.AccumulateInput(ev);
            GetTree()?.Root?.PropagateInputEvent(ev);
            return $"{{\"ok\":true,\"action\":\"{action}\",\"key\":\"{key}\"}}";
        }
        catch (Exception e) { return $"{{\"error\":\"{e.Message}\"}}"; }
    }

    private string GetGameInfo()
    {
        var tree = GetTree();
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["engine_version"] = Engine.GetVersionInfo()["string"].AsString(),
            ["fps"] = Engine.GetFramesPerSecond(),
            ["window_size"] = $"{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}",
            ["current_scene"] = tree?.CurrentScene?.SceneFilePath ?? "unknown",
            ["server_name"] = ServerName,
            ["tools_count"] = _tools.Count,
            ["metrics_count"] = _metrics.Count,
        });
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  SUPPORT TYPES
// ═══════════════════════════════════════════════════════════════════════════

public class McpTool
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public JsonObject InputSchema { get; set; } = new()
    {
        ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray(),
    };
}

public class MetricEntry
{
    public Func<object> Getter;
    public double SampleRate = 1.0;
    public double Elapsed;
}

public struct MetricSample
{
    public double Time;
    public object Value;
    public MetricSample(double time, object value) { Time = time; Value = value; }
}

public class TestRun
{
    public string Id;
    public string ScenePath;
    public float MaxDuration;
    public float CaptureFramesInterval;
    public string Status;
    public double StartTime;
    public double Duration;
    public double FrameCaptureTimer;
    public Dictionary<string, List<MetricSample>> MetricsSnapshot;
    public List<string> Screenshots;
    public List<string> Logs;
    public int AssertPassed;
    public int AssertFailed;
    public List<string> AssertFailures;
}

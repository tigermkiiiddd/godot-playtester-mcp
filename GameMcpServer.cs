using Godot;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

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
    internal readonly Dictionary<string, MetricEntry> _metrics = new();
    internal readonly Dictionary<string, List<MetricSample>> _metricHistory = new();
    private const int MAX_HISTORY_SECONDS = 60;
    private const int MAX_TEST_HISTORY_SECONDS = 600;

    // Test Runner
    internal readonly Dictionary<string, TestRun> _tests = new();
    private int _testCounter;

    // Macro Runner
    internal readonly Dictionary<string, MacroRun> _macros = new();
    internal int _macroCounter;

    // Log capture
    private readonly List<string> _capturedLogs = new();
    private bool _captureLogs;

    // HUD
    private Label _healthLabel;
    private int _requestCount;

    // Thread-safe action queue (HTTP thread → main thread)
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _mainQueue = new();

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        if (!Enabled) return;
        RegisterBuiltinTools();
        RegisterMacroTools();
        StartServer();
        CallDeferred(MethodName.CreateHealthHud);
        GD.Print($"[GameMcp] Playtester MCP server listening on http://localhost:{Port}");
    }

    public override void _ExitTree()
    {
        StopServer();
        Instance = null;
    }

    public override void _Process(double delta)
    {
        while (_mainQueue.TryDequeue(out var action))
            action();
        SampleMetrics(delta);
        UpdateTests(delta);
        UpdateMacros(delta);
        UpdateHealthHud();
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
            InputSchema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray() }
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

    public void RegisterMetric(string name, Func<object> getter, double sampleRate = 1.0)
    {
        _metrics[name] = new MetricEntry { Getter = getter, SampleRate = sampleRate };
        _metricHistory[name] = new List<MetricSample>();
        GD.Print($"[GameMcp] Registered metric: {name}");
    }

    public void RegisterMetric(string name, string nodePath, string propertyName, double sampleRate = 1.0)
    {
        RegisterMetric(name, () =>
        {
            var node = GetNodeOrNull(nodePath);
            return node == null ? "node_not_found" : node.Get(propertyName);
        }, sampleRate);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  SUPPORT TYPES
// ═══════════════════════════════════════════════════════════════════════════

public class McpTool
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public JsonObject InputSchema { get; set; } = new() { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray() };
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

#nullable disable
using Godot;
namespace GodotPlaytester;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Encodings.Web;
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
public partial class GameMcpServer : Node, IInputProvider
{
    // Shared JSON options: Chinese/Unicode output as raw UTF-8, readable escapes
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

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
    internal readonly Dictionary<string, RingBuffer<MetricSample>> _metricHistory = new();
    private const int MAX_HISTORY_SAMPLES = 6000;
    private const int MAX_HISTORY_SECONDS = 60;
    private const int MAX_TEST_HISTORY_SECONDS = 600;

    // Test Runner
    internal readonly Dictionary<string, TestRun> _tests = new();
    private int _testCounter;

    // Macro Runner
    internal readonly Dictionary<string, MacroRun> _macros = new();
    internal int _macroCounter;

    // Log capture
    private const int AI_LOG_CAPACITY = 1000;
    private const int DEBUG_LOG_CAPACITY = 2000;
    private readonly LogEntry[] _aiLogRing = new LogEntry[AI_LOG_CAPACITY];
    private int _aiLogHead, _aiLogCount;
    private readonly LogEntry[] _debugLogRing = new LogEntry[DEBUG_LOG_CAPACITY];
    private int _debugLogHead, _debugLogCount;
    private readonly Dictionary<string, System.IO.StreamWriter> _fileWriters = new();

    // HUD
    private Label _healthLabel;
    private int _requestCount;

    // Virtual cursor
    [Export] public bool ShowCursor = true;
    private CanvasLayer _cursorLayer;
    private Control _cursorCross;
    private Vector2 _simMousePos; // initialized to screen center in _Ready
    private bool _simMouseLeftDown;   // simulated left button state
    private bool _simMouseRightDown;  // simulated right button state
    private bool _prevMouseLeftDown;  // previous frame state (for edge detection)
    private bool _simMouseLeftJustPressed;  // cached per-frame edge
    private bool _simMouseLeftJustReleased; // cached per-frame edge

    // IInputProvider — explicit interface implementation
    Vector2 IInputProvider.MousePosition => _simMousePos;
    bool IInputProvider.MouseLeftDown => _simMouseLeftDown;
    bool IInputProvider.MouseLeftJustPressed => _simMouseLeftJustPressed;
    bool IInputProvider.MouseLeftJustReleased => _simMouseLeftJustReleased;

    // Legacy public accessors (kept for backward compat)
    public Vector2 SimMousePos => _simMousePos;
    public bool SimMouseLeftDown => _simMouseLeftDown;
    public bool SimMouseLeftJustPressed => _simMouseLeftJustPressed;
    public bool SimMouseLeftJustReleased => _simMouseLeftJustReleased;

    // Thread-safe action queue (HTTP thread → main thread)
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _mainQueue = new();

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always; // 游戏暂停时服务器照常响应，否则所有调用 10s 静默超时
    }

    public override void _Ready()
    {
        if (!Enabled || !OS.IsDebugBuild()) return; // 发布版永不开端口
        _simMousePos = DisplayServer.WindowGetSize() / 2; // center of screen
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
        // Snapshot edge detection at start of frame, before any updates
        _simMouseLeftJustPressed = _simMouseLeftDown && !_prevMouseLeftDown;
        _simMouseLeftJustReleased = !_simMouseLeftDown && _prevMouseLeftDown;

        while (_mainQueue.TryDequeue(out var action))
            action();
        SampleMetrics(delta);
        UpdateTests(delta);
        UpdateMacros(delta);
        UpdateHealthHud();
        UpdateVirtualCursor();
        // Update previous state at END of frame for next frame's edge detection
        _prevMouseLeftDown = _simMouseLeftDown;
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
        _metricHistory[name] = new RingBuffer<MetricSample>(MAX_HISTORY_SAMPLES);
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

    // ── Logging API ───────────────────────────────────────────────────

    /// <summary>Log an important game event to the AI ring buffer (1000 entries).</summary>
    public void Log(string category, string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(LogType.AI, level, Time.GetTicksMsec() / 1000.0, category, message);
        WriteToRing(_aiLogRing, ref _aiLogHead, ref _aiLogCount, AI_LOG_CAPACITY, entry);
        // Auto-capture to running tests
        foreach (var kv in _tests)
            if (kv.Value.Status == "running")
                kv.Value.Logs.Add($"[{level}] [{category}] {message}");
    }

    /// <summary>Log a warning.</summary>
    public void LogWarn(string category, string message) => Log(category, message, LogLevel.Warn);

    /// <summary>Log an error.</summary>
    public void LogError(string category, string message) => Log(category, message, LogLevel.Error);

    /// <summary>Log high-volume data to a file (user://mcp_logs/category_date.log).</summary>
    public void FileLog(string category, string message)
    {
        try
        {
            var dir = ProjectSettings.GlobalizePath("user://mcp_logs");
            System.IO.Directory.CreateDirectory(dir);
            var date = DateTime.Now.ToString("yyyyMMdd");
            var path = System.IO.Path.Combine(dir, $"{category}_{date}.log");
            if (!_fileWriters.TryGetValue(path, out var writer) || writer.BaseStream == null)
            {
                writer = new System.IO.StreamWriter(path, append: true) { AutoFlush = true };
                _fileWriters[path] = writer;
            }
            var ts = Math.Round(Time.GetTicksMsec() / 1000.0, 3);
            writer.WriteLine($"{{\"time\":{ts},\"msg\":\"{message.Replace("\"", "\\\"")}\"}}");
        }
        catch { /* file log is best-effort */ }
    }

    /// <summary>Capture a debug log message (replaces GD.Print for MCP visibility).</summary>
    public void DebugLog(string message, LogLevel level = LogLevel.Debug)
    {
        var entry = new LogEntry(LogType.Debug, level, Time.GetTicksMsec() / 1000.0, "debug", message);
        WriteToRing(_debugLogRing, ref _debugLogHead, ref _debugLogCount, DEBUG_LOG_CAPACITY, entry);
    }

    /// <summary>Clear all ring buffers.</summary>
    public void ClearLogs()
    {
        _aiLogHead = _aiLogCount = 0;
        _debugLogHead = _debugLogCount = 0;
    }

    private static void WriteToRing(LogEntry[] ring, ref int head, ref int count, int capacity, LogEntry entry)
    {
        ring[head] = entry;
        head = (head + 1) % capacity;
        if (count < capacity) count++;
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

// ── Log Types ─────────────────────────────────────────────────────

public enum LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }
public enum LogType { AI = 0, File = 1, Debug = 2 }

public struct LogEntry
{
    public LogType Type;
    public LogLevel Level;
    public double Timestamp;
    public string Category;
    public string Message;

    public LogEntry(LogType type, LogLevel level, double timestamp, string category, string message)
    { Type = type; Level = level; Timestamp = timestamp; Category = category; Message = message; }
}

/// <summary>Image tool-call result — routed to MCP "image" content blocks instead of text/base64-in-JSON.</summary>
public sealed class McpImageResult
{
    public string Base64;
    public string MimeType;
}

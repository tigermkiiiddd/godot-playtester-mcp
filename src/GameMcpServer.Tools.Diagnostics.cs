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
    // ── screenshot ───────────────────────────────────────────────────────

    private McpImageResult TakeScreenshot(string format, int quality, int maxWidth)
    {
        var img = GetViewport().GetTexture().GetImage();
        if (maxWidth > 0 && img.GetWidth() > maxWidth)
        {
            int newHeight = (int)(img.GetHeight() * (float)maxWidth / img.GetWidth());
            img.Resize(maxWidth, newHeight, Image.Interpolation.Lanczos);
        }
        byte[] buf = format == "png" ? img.SavePngToBuffer() : img.SaveJpgToBuffer();
        return new McpImageResult
        {
            Base64 = Convert.ToBase64String(buf),
            MimeType = format == "png" ? "image/png" : "image/jpeg"
        };
    }

    // ── time_scale ───────────────────────────────────────────────────────

    private string TimeScale(Dictionary<string, JsonElement> args)
    {
        if (args.ContainsKey("scale"))
        {
            var s = args["scale"].GetDouble();
            if (s < 0.1 || s > 20.0) return "{\"error\":\"scale out of range [0.1, 20]\"}";
            Engine.TimeScale = s;
        }
        return $"{{\"ok\":true,\"time_scale\":{Engine.TimeScale}}}";
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
            _metricHistory[name] = new RingBuffer<MetricSample>(MAX_HISTORY_SAMPLES);
        }
        return $"{{\"ok\":true,\"name\":\"{name}\"}}";
    }

    private string GetMetrics(Dictionary<string, JsonElement> args)
    {
        string[] names = null;
        if (args.ContainsKey("names"))
        {
            var n = args["names"];
            if (n.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(n.GetString()))
                names = n.GetString().Split(',');
            else if (n.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in n.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                        list.Add(item.GetString());
                if (list.Count > 0) names = list.ToArray();
            }
        }

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
        _healthLabel = new Label
        {
            Name = "McpHealthLabel",
            AnchorLeft = 0f,
            AnchorTop = 1f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 10,
            OffsetTop = -36,
            OffsetRight = -10,
            OffsetBottom = -4,
            GrowVertical = Control.GrowDirection.Begin
        };
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
                if (!_metricHistory.ContainsKey(kv.Key)) _metricHistory[kv.Key] = new RingBuffer<MetricSample>(MAX_HISTORY_SAMPLES);
                var h = _metricHistory[kv.Key];
                h.Push(new MetricSample(Time.GetTicksMsec() / 1000.0, val));
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
            var dir = ProjectSettings.GlobalizePath("user://mcp_logs");
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
            var dir = ProjectSettings.GlobalizePath("user://mcp_logs");
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

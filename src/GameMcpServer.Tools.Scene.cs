using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class GameMcpServer
{
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
        if (n is Node2D n2d) { sb.Append($",\"position\":\"{n2d.GlobalPosition}\""); sb.Append($",\"visible\":{(n2d.Visible ? "true" : "false")}"); }
        sb.Append("}");
        return sb.ToString();
    }

    private string SetNodeProperty(string path, string prop, JsonElement valEl)
    {
        var n = GetNodeOrNull(path);
        if (n == null) return $"{{\"error\":\"Node not found: {path}\"}}";
        try
        {
            // Handle value by its JSON type
            switch (valEl.ValueKind)
            {
                case JsonValueKind.String:
                    var s = valEl.GetString();
                    if (prop == "visible" && n is CanvasItem ci) { ci.Visible = s.ToLower() == "true"; }
                    else n.Set(new StringName(prop), Variant.From(s));
                    break;
                case JsonValueKind.Number:
                    if (valEl.TryGetInt64(out long lv)) n.Set(new StringName(prop), Variant.From((int)lv));
                    else n.Set(new StringName(prop), Variant.From(valEl.GetDouble()));
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    n.Set(new StringName(prop), Variant.From(valEl.GetBoolean()));
                    break;
                case JsonValueKind.Array:
                    var arr = JsonSerializer.Deserialize<JsonElement>(valEl.GetRawText());
                    if (prop == "position" && arr.GetArrayLength() >= 2 && n is Node2D n2d)
                        n2d.GlobalPosition = new Vector2(arr[0].GetSingle(), arr[1].GetSingle());
                    else n.Set(new StringName(prop), Variant.From(valEl.GetRawText()));
                    break;
                default:
                    n.Set(new StringName(prop), Variant.From(valEl.GetRawText()));
                    break;
            }
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

    private Node FindPlayer()
    {
        if (!string.IsNullOrEmpty(PlayerPath)) return GetNodeOrNull(PlayerPath);
        return GetTree()?.GetFirstNodeInGroup("player");
    }
}

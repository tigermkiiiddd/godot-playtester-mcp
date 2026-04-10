using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class GameMcpServer
{
    private void RegisterMacroTools()
    {
        Reg("execute_macro",
            "Execute a sequence of timed input actions (macro). Supports: hold_key, tap_key, repeat_key, combo_keys, move_distance, move_to (8dir/4dir/free), click, drag (press-move-release), double_click, type_text (character-by-character), wait. Returns macro_id immediately; query progress with get_macro_status.",
            p("{\"steps\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\"},\"keys\":{\"type\":\"string\"},\"duration\":{\"type\":\"number\"},\"count\":{\"type\":\"integer\"},\"interval\":{\"type\":\"number\"},\"direction\":{\"type\":\"string\"},\"distance\":{\"type\":\"number\"},\"target_x\":{\"type\":\"number\"},\"target_y\":{\"type\":\"number\"},\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"button\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"},\"char_interval\":{\"type\":\"number\"},\"from_x\":{\"type\":\"number\"},\"from_y\":{\"type\":\"number\"},\"to_x\":{\"type\":\"number\"},\"to_y\":{\"type\":\"number\"},\"delay\":{\"type\":\"number\"},\"label\":{\"type\":\"string\"},\"mode\":{\"type\":\"string\"}},\"required\":[\"action\"]}},\"name\":{\"type\":\"string\"}}", "object", new[] { "steps" }),
            ExecuteMacroHandler);

        Reg("get_macro_status",
            "Get the status and per-step progress of a macro.",
            p("{\"macro_id\":{\"type\":\"string\"},\"include\":{\"type\":\"string\"}}", "object", new[] { "macro_id" }),
            GetMacroStatusHandler);

        Reg("cancel_macro",
            "Cancel a running macro and release all held keys.",
            p("{\"macro_id\":{\"type\":\"string\"}}", "object", new[] { "macro_id" }),
            CancelMacroHandler);

        Reg("list_macros",
            "List all macros, optionally filtered by status (running/completed/cancelled/error).",
            p("{\"status\":{\"type\":\"string\"}}", "object"),
            ListMacrosHandler);
    }

    // ── Handlers ──────────────────────────────────────────────────────────

    private string ExecuteMacroHandler(Dictionary<string, JsonElement> args)
    {
        try
        {
            var name = args.ContainsKey("name") ? args["name"].GetString() : "";
            var stepsArr = args["steps"];

            var macro = new MacroRun
            {
                Id = $"macro_{++_macroCounter:D3}",
                Name = name ?? "",
                StartTime = Time.GetTicksMsec() / 1000.0,
            };

            // Parse steps
            foreach (JsonElement stepEl in stepsArr.EnumerateArray())
            {
                var step = ParseMacroStep(stepEl);
                if (step == null) continue;
                macro.Steps.Add(step);
            }

            if (macro.Steps.Count == 0)
                return "{\"error\":\"No valid steps in macro\"}";

            // Enqueue to start on main thread
            var id = macro.Id;
            _macros[id] = macro;

            return $"{{\"macro_id\":\"{macro.Id}\",\"name\":\"{macro.Name}\",\"step_count\":{macro.Steps.Count},\"status\":\"running\"}}";
        }
        catch (Exception e)
        {
            return $"{{\"error\":\"{e.Message.Replace("\"", "'")}\",\"type\":\"{e.GetType().Name}\",\"stack\":\"{e.StackTrace?.Replace("\\", "/").Replace("\"", "'").Replace("\n", "|")}\"}}";
        }
    }

    private string GetMacroStatusHandler(Dictionary<string, JsonElement> args)
    {
        var macroId = args["macro_id"].GetString();
        if (!_macros.TryGetValue(macroId, out var macro))
            return $"{{\"error\":\"Macro not found: {macroId}\"}}";

        var include = args.ContainsKey("include") ? args["include"].GetString() : "all";

        var r = new JsonObject
        {
            ["macro_id"] = macro.Id,
            ["name"] = macro.Name,
            ["status"] = macro.Status,
            ["current_step"] = macro.CurrentStepIndex,
            ["total_steps"] = macro.Steps.Count,
            ["elapsed_sec"] = Math.Round(Time.GetTicksMsec() / 1000.0 - macro.StartTime, 2),
            ["error"] = macro.ErrorMessage ?? (string)null
        };

        if (include == "all" || include == "steps")
        {
            var steps = new JsonArray();
            foreach (var s in macro.Steps)
            {
                var so = new JsonObject
                {
                    ["action"] = s.Type.ToString().ToLower(),
                    ["status"] = s.Status,
                    ["label"] = s.Label ?? "",
                };
                if (s.Keys.Length > 0) so["keys"] = string.Join(",", s.Keys);
                if (s.Type == MacroStepType.HoldKey || s.Type == MacroStepType.ComboKeys || s.Type == MacroStepType.Wait)
                    so["duration"] = s.Duration;
                if (s.Type == MacroStepType.RepeatKey)
                {
                    so["count"] = s.Count;
                    so["interval"] = s.Interval;
                    so["repeat_index"] = s.RepeatIndex;
                }
                if (s.Type == MacroStepType.MoveDistance)
                {
                    so["direction"] = s.Direction;
                    so["distance"] = s.Distance;
                }
                if (s.Type == MacroStepType.MoveTo)
                {
                    so["target_x"] = s.TargetX;
                    so["target_y"] = s.TargetY;
                    so["mode"] = s.Mode;
                }
                if (s.Type == MacroStepType.Drag)
                {
                    so["from"] = new JsonArray { s.X, s.Y };
                    so["to"] = new JsonArray { s.TargetX, s.TargetY };
                }
                if (s.Type == MacroStepType.TypeText)
                {
                    so["text"] = s.Text;
                    so["char_index"] = s.CharIndex;
                    so["total_chars"] = s.Text.Length;
                }
                if (s.Elapsed > 0) so["elapsed"] = Math.Round(s.Elapsed, 3);
                if (s.ErrorMessage != null) so["error"] = s.ErrorMessage;
                steps.Add(so);
            }
            r["steps"] = steps;
        }

        return JsonSerializer.Serialize(r);
    }

    private string CancelMacroHandler(Dictionary<string, JsonElement> args)
    {
        var macroId = args["macro_id"].GetString();
        if (!_macros.TryGetValue(macroId, out var macro))
            return $"{{\"error\":\"Macro not found: {macroId}\"}}";

        if (macro.Status != "running")
            return $"{{\"ok\":false,\"reason\":\"Macro is {macro.Status}\"}}";

        var released = macro.HeldKeys.ToList();
        CancelMacroRun(macro);
        return $"{{\"ok\":true,\"macro_id\":\"{macroId}\",\"released_keys\":[\"{string.Join("\",\"", released)}\"]}}";
    }

    private string ListMacrosHandler(Dictionary<string, JsonElement> args)
    {
        var filter = args.ContainsKey("status") ? args["status"].GetString() : null;
        var arr = new JsonArray();

        foreach (var kv in _macros)
        {
            var m = kv.Value;
            if (filter != null && m.Status != filter) continue;
            arr.Add(new JsonObject
            {
                ["macro_id"] = m.Id,
                ["name"] = m.Name,
                ["status"] = m.Status,
                ["current_step"] = m.CurrentStepIndex,
                ["total_steps"] = m.Steps.Count
            });
        }

        return JsonSerializer.Serialize(new JsonObject { ["macros"] = arr });
    }

    // ── Step Parser ───────────────────────────────────────────────────────

    private MacroStep ParseMacroStep(JsonElement el)
    {
        var actionStr = el.TryGetProperty("action", out var actionEl) ? actionEl.GetString() : "";
        if (string.IsNullOrEmpty(actionStr)) return null;

        var step = new MacroStep();

        // Parse action type
        step.Type = actionStr.ToLower() switch
        {
            "hold_key" => MacroStepType.HoldKey,
            "tap_key" => MacroStepType.TapKey,
            "repeat_key" => MacroStepType.RepeatKey,
            "combo_keys" => MacroStepType.ComboKeys,
            "move_distance" => MacroStepType.MoveDistance,
            "move_to" => MacroStepType.MoveTo,
            "click" => MacroStepType.Click,
            "wait" => MacroStepType.Wait,
            "drag" => MacroStepType.Drag,
            "double_click" => MacroStepType.DoubleClick,
            "type_text" => MacroStepType.TypeText,
            _ => MacroStepType.Wait
        };

        // Parse common fields
        if (el.TryGetProperty("label", out var labelEl)) step.Label = labelEl.GetString() ?? "";

        // Parse keys
        if (el.TryGetProperty("keys", out var keysEl))
        {
            var keysStr = keysEl.GetString() ?? "";
            step.Keys = keysStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim()).ToArray();
            step.KeyCodes = step.Keys.Select(k => MapKey(k)).ToArray();
        }

        // Parse timing
        if (el.TryGetProperty("duration", out var durEl)) step.Duration = durEl.GetDouble();
        if (el.TryGetProperty("count", out var cntEl)) step.Count = cntEl.GetInt32();
        if (el.TryGetProperty("interval", out var intEl)) step.Interval = intEl.GetDouble();
        if (el.TryGetProperty("delay", out var delayEl)) step.Duration = delayEl.GetDouble();

        // Parse distance
        if (el.TryGetProperty("direction", out var dirEl)) step.Direction = dirEl.GetString() ?? "";
        if (el.TryGetProperty("distance", out var distEl)) step.Distance = distEl.GetSingle();

        // Parse mouse
        if (el.TryGetProperty("x", out var xEl)) step.X = xEl.GetSingle();
        if (el.TryGetProperty("y", out var yEl)) step.Y = yEl.GetSingle();
        if (el.TryGetProperty("button", out var btnEl)) step.Button = btnEl.GetString() ?? "left";

        // Parse move_to target
        if (el.TryGetProperty("target_x", out var txEl)) step.TargetX = txEl.GetSingle();
        if (el.TryGetProperty("target_y", out var tyEl)) step.TargetY = tyEl.GetSingle();
        if (el.TryGetProperty("mode", out var modeEl)) step.Mode = modeEl.GetString() ?? "8dir";

        // Parse drag: from_x/from_y are X/Y, to_x/to_y are TargetX/TargetY
        if (el.TryGetProperty("from_x", out var fxEl)) step.X = fxEl.GetSingle();
        if (el.TryGetProperty("from_y", out var fyEl)) step.Y = fyEl.GetSingle();
        if (el.TryGetProperty("to_x", out var toxEl)) step.TargetX = toxEl.GetSingle();
        if (el.TryGetProperty("to_y", out var toyEl)) step.TargetY = toyEl.GetSingle();

        // Parse type_text
        if (el.TryGetProperty("text", out var textEl)) step.Text = textEl.GetString() ?? "";
        if (el.TryGetProperty("char_interval", out var ciEl)) step.CharInterval = ciEl.GetDouble();

        // Set direction key for move_distance if not provided
        if (step.Type == MacroStepType.MoveDistance && step.Keys.Length == 0 && !string.IsNullOrEmpty(step.Direction))
        {
            var dirKey = step.Direction.ToLower() switch
            {
                "up" => "W", "down" => "S", "left" => "A", "right" => "D",
                _ => ""
            };
            if (!string.IsNullOrEmpty(dirKey))
            {
                step.Keys = new[] { dirKey };
                step.KeyCodes = new[] { MapKey(dirKey) };
            }
        }

        return step;
    }
}

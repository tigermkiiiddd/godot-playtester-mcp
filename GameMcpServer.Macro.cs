using Godot;
using System;
using System.Linq;

public partial class GameMcpServer
{
    // ── Macro Execution Engine (runs in _Process on main thread) ─────────

    private void UpdateMacros(double delta)
    {
        // Prune completed macros older than 5 minutes
        var cutoff = Time.GetTicksMsec() / 1000.0 - 300;
        var toRemove = _macros.Where(kv =>
            kv.Value.Status != "running" &&
            kv.Value.StartTime + kv.Value.Duration < cutoff)
            .Select(kv => kv.Key).ToList();
        foreach (var id in toRemove) _macros.Remove(id);

        // Process running macros
        foreach (var kv in _macros.ToList())
        {
            var macro = kv.Value;
            if (macro.Status != "running") continue;

            if (macro.CurrentStepIndex >= macro.Steps.Count)
            {
                CompleteMacro(macro);
                continue;
            }

            var step = macro.Steps[macro.CurrentStepIndex];

            // Start pending step
            if (step.Status == "pending")
                StartStep(macro, step);

            // Process active step
            if (step.Status == "running")
                ProcessStep(macro, step, delta);

            // Advance if completed — continue same frame for instant steps
            if (step.Status == "completed" || step.Status == "error")
            {
                if (step.Status == "error")
                {
                    macro.Status = "error";
                    macro.ErrorMessage = step.ErrorMessage;
                    ReleaseAllKeys(macro);
                    LogError("macro", $"Macro error: {macro.Id} step {macro.CurrentStepIndex}: {step.ErrorMessage}");
                    continue;
                }
                macro.CurrentStepIndex++;
            }
        }
    }

    private void StartStep(MacroRun macro, MacroStep step)
    {
        step.Status = "running";
        step.Elapsed = 0;

        switch (step.Type)
        {
            case MacroStepType.TapKey:
                foreach (var kc in step.KeyCodes)
                    Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = kc });
                foreach (var kc in step.KeyCodes)
                    Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = kc });
                step.Status = "completed";
                break;

            case MacroStepType.Click:
                var btn = step.Button.ToLower() switch
                {
                    "right" => MouseButton.Right,
                    "middle" => MouseButton.Middle,
                    _ => MouseButton.Left
                };
                Input.ParseInputEvent(new InputEventMouseButton { Pressed = true, Position = new Vector2(step.X, step.Y), GlobalPosition = new Vector2(step.X, step.Y), ButtonIndex = btn });
                Input.ParseInputEvent(new InputEventMouseButton { Pressed = false, Position = new Vector2(step.X, step.Y), GlobalPosition = new Vector2(step.X, step.Y), ButtonIndex = btn });
                step.Status = "completed";
                break;

            case MacroStepType.Wait:
                if (step.Duration <= 0) step.Status = "completed";
                break;

            case MacroStepType.HoldKey:
            case MacroStepType.ComboKeys:
                foreach (var kc in step.KeyCodes)
                    Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = kc });
                foreach (var k in step.Keys)
                    macro.HeldKeys.Add(k);
                break;

            case MacroStepType.RepeatKey:
                if (step.KeyCodes.Length > 0)
                {
                    Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = step.KeyCodes[0] });
                    macro.HeldKeys.Add(step.Keys[0]);
                    step.KeyPressed = true;
                    step.RepeatIndex = 0;
                    step.RepeatTimer = 0;
                }
                break;

            case MacroStepType.MoveDistance:
            case MacroStepType.MoveTo:
                var player = FindPlayer();
                if (player is Node2D n2d)
                {
                    step.StartPosition = n2d.GlobalPosition;
                }
                else
                {
                    step.Status = "error";
                    step.ErrorMessage = "No player found for move_distance";
                    break;
                }

                // move_to: calculate direction keys dynamically
                if (step.Type == MacroStepType.MoveTo)
                {
                    step.LastCheckPos = n2d.GlobalPosition;
                    step.StuckTimer = 0;

                    // free mode: smooth any-angle movement (direct position, no keys)
                    if (step.Mode == "free")
                    {
                        // no key simulation, ProcessStep handles position directly
                        break;
                    }

                    var keys = new System.Collections.Generic.List<string>();
                    var codes = new System.Collections.Generic.List<Key>();
                    float dx = step.TargetX - n2d.GlobalPosition.X;
                    float dy = step.TargetY - n2d.GlobalPosition.Y;

                    if (step.Mode == "4dir")
                    {
                        // 4dir: only one axis at a time, X first
                        if (Math.Abs(dx) > 5f)
                        {
                            keys.Add(dx > 0 ? "D" : "A");
                            codes.Add(dx > 0 ? Key.D : Key.A);
                        }
                        else if (Math.Abs(dy) > 5f)
                        {
                            keys.Add(dy > 0 ? "S" : "W");
                            codes.Add(dy > 0 ? Key.S : Key.W);
                        }
                    }
                    else // 8dir: diagonal allowed
                    {
                        if (dx > 5) { keys.Add("D"); codes.Add(Key.D); }
                        else if (dx < -5) { keys.Add("A"); codes.Add(Key.A); }
                        if (dy > 5) { keys.Add("S"); codes.Add(Key.S); }
                        else if (dy < -5) { keys.Add("W"); codes.Add(Key.W); }
                    }
                    step.Keys = keys.ToArray();
                    step.KeyCodes = codes.ToArray();
                }

                foreach (var kc in step.KeyCodes)
                    Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = kc });
                foreach (var k in step.Keys)
                    macro.HeldKeys.Add(k);
                break;
        }
    }

    private void ProcessStep(MacroRun macro, MacroStep step, double delta)
    {
        step.Elapsed += delta;

        switch (step.Type)
        {
            case MacroStepType.HoldKey:
            case MacroStepType.ComboKeys:
                if (step.Elapsed >= step.Duration)
                {
                    ReleaseStepKeys(macro, step);
                    step.Status = "completed";
                }
                break;

            case MacroStepType.Wait:
                if (step.Elapsed >= step.Duration)
                    step.Status = "completed";
                break;

            case MacroStepType.RepeatKey:
                ProcessRepeatKey(macro, step, delta);
                break;

            case MacroStepType.MoveDistance:
            case MacroStepType.MoveTo:
                ProcessMoveDistance(macro, step, delta);
                break;
        }
    }

    private void ProcessRepeatKey(MacroRun macro, MacroStep step, double delta)
    {
        if (step.KeyCodes.Length == 0) { step.Status = "completed"; return; }

        step.RepeatTimer += delta;

        if (step.KeyPressed)
        {
            // Hold for ~1 frame, then release
            if (step.RepeatTimer >= 0.05)
            {
                Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = step.KeyCodes[0] });
                macro.HeldKeys.Remove(step.Keys[0]);
                step.KeyPressed = false;
                step.RepeatTimer = 0;
            }
        }
        else
        {
            // Wait for interval, then press again
            if (step.RepeatTimer >= step.Interval)
            {
                step.RepeatIndex++;
                if (step.RepeatIndex >= step.Count)
                {
                    step.Status = "completed";
                }
                else
                {
                    Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = step.KeyCodes[0] });
                    macro.HeldKeys.Add(step.Keys[0]);
                    step.KeyPressed = true;
                    step.RepeatTimer = 0;
                }
            }
        }
    }

    private void ProcessMoveDistance(MacroRun macro, MacroStep step, double delta)
    {
        var player = FindPlayer() as Node2D;
        if (player == null)
        {
            step.Status = "error";
            step.ErrorMessage = "Player node lost during move_distance";
            return;
        }

        var current = player.GlobalPosition;

        // move_to: free mode — smooth any-angle movement at player's own speed
        if (step.Type == MacroStepType.MoveTo && step.Mode == "free")
        {
            if (player == null) { step.Status = "error"; step.ErrorMessage = "No player"; return; }
            var target = new Vector2(step.TargetX, step.TargetY);
            var diff = target - player.GlobalPosition;
            var dist = diff.Length();
            if (dist <= 2f)
            {
                player.GlobalPosition = target;
                step.Status = "completed";
                return;
            }
            // Read player's own speed, fallback 200
            var speed = 200f;
            var speedVal = player.Get("Speed");
            if (speedVal.VariantType != Variant.Type.Nil)
                speed = (float)speedVal;
            var move = diff.Normalized() * speed * (float)delta;
            if (move.Length() > dist) move = diff;
            player.GlobalPosition += move;
            return;
        }

        // move_to: 4dir/8dir — check each axis independently + stuck detection
        if (step.Type == MacroStepType.MoveTo)
        {
            bool xDone = true, yDone = true;
            var stillNeeded = new System.Collections.Generic.List<string>();
            var stillCodes = new System.Collections.Generic.List<Key>();
            var heldCopy = new System.Collections.Generic.List<string>(macro.HeldKeys);

            if (Math.Abs(current.X - step.TargetX) > 5f)
            {
                xDone = false;
                var k = current.X < step.TargetX ? "D" : "A";
                stillNeeded.Add(k);
                stillCodes.Add(MapKey(k));
            }
            if (Math.Abs(current.Y - step.TargetY) > 5f)
            {
                yDone = false;
                var k = current.Y < step.TargetY ? "S" : "W";
                stillNeeded.Add(k);
                stillCodes.Add(MapKey(k));
            }

            if (xDone && yDone)
            {
                ReleaseStepKeys(macro, step);
                step.Status = "completed";
                return;
            }

            // Stuck detection: if not moving for 0.5s, stop
            if (current.DistanceTo(step.LastCheckPos) < 1f)
            {
                step.StuckTimer += delta;
                if (step.StuckTimer > 0.5)
                {
                    ReleaseStepKeys(macro, step);
                    step.Status = "completed";
                    step.ErrorMessage = "Stuck (obstacle)";
                    return;
                }
            }
            else
            {
                step.StuckTimer = 0;
                step.LastCheckPos = current;
            }

            // Release keys no longer needed, press newly needed ones
            foreach (var k in heldCopy)
            {
                if (!stillNeeded.Contains(k))
                {
                    Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = MapKey(k) });
                    macro.HeldKeys.Remove(k);
                }
            }
            foreach (var k in stillNeeded)
            {
                if (!macro.HeldKeys.Contains(k))
                {
                    Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = MapKey(k) });
                    macro.HeldKeys.Add(k);
                }
            }
            step.Keys = stillNeeded.ToArray();
            step.KeyCodes = stillCodes.ToArray();
        }
        else // move_distance: check single-axis traveled distance
        {
            float traveled = step.Direction.ToLower() switch
            {
                "right" => current.X - step.StartPosition.X,
                "left" => step.StartPosition.X - current.X,
                "up" => step.StartPosition.Y - current.Y,
                "down" => current.Y - step.StartPosition.Y,
                _ => 0f
            };

            if (traveled >= step.Distance)
            {
                ReleaseStepKeys(macro, step);
                step.Status = "completed";
                return;
            }
        }

        // Safety timeout: 10 seconds
        if (step.Elapsed > 10.0)
        {
            ReleaseStepKeys(macro, step);
            step.Status = "completed";
            step.ErrorMessage = "Distance timeout (10s)";
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ReleaseStepKeys(MacroRun macro, MacroStep step)
    {
        foreach (var kc in step.KeyCodes)
            Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = kc });
        foreach (var k in step.Keys)
            macro.HeldKeys.Remove(k);
    }

    private void ReleaseAllKeys(MacroRun macro)
    {
        foreach (var k in macro.HeldKeys.ToList())
            Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = MapKey(k) });
        macro.HeldKeys.Clear();
    }

    private void CompleteMacro(MacroRun macro)
    {
        macro.Status = "completed";
        macro.Duration = Time.GetTicksMsec() / 1000.0 - macro.StartTime;
        ReleaseAllKeys(macro);
        Log("macro", $"Macro done: {macro.Id} name={macro.Name} steps={macro.Steps.Count} duration={macro.Duration:F1}s");
    }

    private void CancelMacroRun(MacroRun macro)
    {
        var step = macro.CurrentStepIndex < macro.Steps.Count
            ? macro.Steps[macro.CurrentStepIndex] : null;
        if (step != null && step.Status == "running")
            ReleaseStepKeys(macro, step);
        ReleaseAllKeys(macro);
        macro.Status = "cancelled";
        macro.Duration = Time.GetTicksMsec() / 1000.0 - macro.StartTime;
        Log("macro", $"Macro cancelled: {macro.Id}");
    }
}

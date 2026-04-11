# Debugging Guide

Common pitfalls, root causes, and fixes when working with the Godot Playtester MCP.

## Quick Diagnosis

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| MCP tools don't appear in Claude Code | PascalCase field names in tools/list | Fix `HandleToolsList()` to use lowercase keys (see below) |
| `click_element` returns "not found" for Chinese names | GBK encoding corrupts non-ASCII | Use ASCII-only node names |
| `InvalidOperationException: "node already has a parent"` | JsonNode reused without DeepClone | Use `id?.DeepClone()` in RpcResult/RpcError |
| Drag immediately cancels | Real mouse / MCP drag mutual exclusion bug | Add `_dragFromRealMouse` flag |
| ESC/tap_key not working | `Input.IsKeyPressed()` polling misses same-frame press+release | Use `hold_key` with duration |
| `JsonElement` extraction error on array params | `GetValue<JsonElement>()` fails on non-leaf nodes | Use `JsonSerializer.Deserialize<JsonElement>(...)` |
| Old game code running on port | Two Godot processes, old one occupies port | `taskkill` old process, verify single process |
| DLL not updating | Incremental build doesn't detect changes | `dotnet build --no-incremental` |

## Critical Issues (Detailed)

### MCP Tools Not Appearing (tools/list Field Names)

**Symptom**: MCP server shows ✓ but no tools appear in Claude Code.

**Root cause**: MCP spec requires lowercase field names (`name`, `description`, `inputSchema`). C# `JsonSerializer.SerializeToNode()` uses PascalCase property names from the `McpTool` class.

```csharp
// WRONG — PascalCase from JsonSerializer
foreach (var t in _tools) arr.Add(JsonSerializer.SerializeToNode(t, JsonOpts));

// CORRECT — manual lowercase keys
foreach (var t in _tools)
{
    arr.Add(new JsonObject
    {
        ["name"] = t.Name,
        ["description"] = t.Description,
        ["inputSchema"] = JsonSerializer.SerializeToNode(t.InputSchema, JsonOpts),
    });
}
```

### ASCII-Only Node Names

**Symptom**: `click_element`, `get_node_properties` return "not found" for Chinese node names.

**Root cause**: Windows terminal encoding (GBK/CP936) corrupts non-ASCII characters in JSON request bodies.

**Rule**: `Name` property = ASCII only. `Text` property (display) = any language. `mcp_data` values = any language (server-side UTF-8).

```csharp
new Button { Name = "HealBtn", Text = "治疗" };     // ✓
new Button { Name = "治疗按钮", Text = "治疗" };     // ✗
```

### JsonNode Parent Ownership

**Symptom**: `InvalidOperationException: "The node already has a parent."` on every request.

**Root cause**: Parsed `id` belongs to request JSON tree. Cannot insert into new JsonObject.

```csharp
// WRONG
new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ... }

// CORRECT
new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ... }
```

### CanvasLayer Input Routing

`Input.ParseInputEvent` does NOT route to CanvasLayer children. Godot's built-in drag-and-drop (`ForceDrag`, `_CanDropData`, `_DropData`) completely fails inside CanvasLayer.

**Solutions**:
1. Buttons: `click_element` uses `EmitSignal(BaseButton.SignalName.Pressed)` — bypasses routing.
2. Drag: dual-source polling — physical mouse in `_Input` + MCP in `_Process`. See SKILL.md for full pattern.

### Real Mouse / MCP Drag Mutual Exclusion

Without a `_dragFromRealMouse` flag, MCP drag gets cancelled by real mouse poll seeing `_dragging=true` but `IsMouseButtonPressed(Left)=false`.

```csharp
// Each poll method guards the other source's drags
if (_dragging && !_dragFromRealMouse) return;  // in real mouse poll
if (_dragging && _dragFromRealMouse) return;   // in MCP poll
```

**Trap**: Do NOT put `if (_dragging) return;` at the top — this prevents position updates and release detection.

### press_key/tap_key Not Working

`Input.IsKeyPressed()` polled in `_Process`/`_PhysicsProcess` may miss same-frame press+release from `tap_key`. Use `hold_key` with a duration instead:

```json
{"action": "hold_key", "keys": "Escape", "duration": 0.5}
```

### Thread Safety: ConcurrentQueue, Not CallDeferred

MCP HTTP server runs on background thread. All Godot API calls must be marshaled to main thread. Use `ConcurrentQueue<Action>` drained in `_Process()`, not `Callable.From().CallDeferred()` (deadlocks with `ManualResetEventSlim`).

### Multiple Godot Processes

If changes don't take effect, check for multiple Godot processes:

```bash
tasklist | grep Godot
taskkill //F //PID <old_pid>
```

Then `dotnet build --no-incremental` and relaunch.

## Log Debugging Workflow

When something doesn't work, follow this diagnostic chain:

1. `get_game_info()` — Is the game running? FPS > 0?
2. `get_logs()` — Did the game log anything about the action?
3. `get_debug_logs()` — Any debug output?
4. `get_ui_layout()` — Is the UI element actually visible?
5. `get_game_state()` — Is the player/object in expected position?

If logs show nothing after an operation, the operation likely didn't reach game code (wrong name, invisible node, routing issue).

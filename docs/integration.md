# Integration Guide

How to integrate the MCP playtester into a Godot 4 (.NET) game. Game code conventions that make MCP tools work.

## Step 1: Copy Files

Copy all `GameMcpServer*.cs` + `RingBuffer.cs` + `IInputProvider.cs` files from `src/` into your project (e.g., `addons/game_mcp/`). Copy `mcp-http-bridge.mjs` as the MCP stdio bridge.

## Step 2: Add Autoload

In Project Settings → Autoload, add `GameMcpServer` (path: `addons/game_mcp/GameMcpServer.cs`). Set port via `[Export] public int Port = 9876;`.

## Step 3: Configure Claude Code

In `.mcp.json`:

```json
{
  "mcpServers": {
    "godot-playtester": {
      "command": "node",
      "args": ["addons/mcp-http-bridge.mjs"],
      "env": { "MCP_HTTP_URL": "http://localhost:9876" }
    }
  }
}
```

## Step 4: Tag Game Objects

### Groups (for `get_game_state`)

| Group | Required | Example |
|-------|----------|---------|
| `player` | Yes | `AddToGroup("player")` on player node |
| `enemies` | Recommended | `AddToGroup("enemies")` on each enemy |
| `npcs` | Recommended | `AddToGroup("npcs")` on NPC nodes |
| `items` | Recommended | `AddToGroup("items")` on pickups |
| `interactables` | Recommended | Doors, chests, switches |
| `mcp_ignore` | Optional | Exclude from `get_ui_layout` |

### UI Node Names

**CRITICAL**: All node `Name` properties that MCP needs to reference must be **ASCII-only**. Display `Text` can be any language.

```csharp
new Button { Name = "HealBtn", Text = "治疗" };     // ✓
new Button { Name = "治疗按钮", Text = "治疗" };     // ✗ click_element fails
```

### Button Callback Convention (Disabled Guard)

`click_element` uses `EmitSignal(BaseButton.SignalName.Pressed)` which **bypasses the `Disabled` property**. Every button callback MUST guard:

```csharp
// WRONG — assumes signal only fires when button is interactive
healBtn.Pressed += () => DoHeal();

// CORRECT — callback validates its own preconditions
healBtn.Pressed += () => {
    if (healBtn.Disabled) return;
    DoHeal();
};
```

**Principle**: MCP operates at the signal level and does not replicate UI state checks. Game code owns its state guards — signal firing ≠ user intent.

### UI Data (mcp_data)

Expose domain data via `SetMeta("mcp_data", jsonString)`:

```csharp
public void Refresh()
{
    SetMeta("mcp_data", new JsonObject
    {
        ["item_name"] = ItemName,       // any language OK (server-side UTF-8)
        ["item_count"] = ItemCount,
        ["item_rarity"] = ItemRarity,
        ["item_type"] = ItemType,
    }.ToJsonString());
}
```

## Step 5: Add MCP Logging

**Every significant game action MUST write to MCP log.** `GD.Print()` alone is invisible to AI agents.

```csharp
// UI events
invBtn.Pressed += () => {
    _inventoryPanel.Visible = !_inventoryPanel.Visible;
    GameMcpServer.Instance?.Log("ui", _inventoryPanel.Visible ? "背包打开" : "背包关闭");
};

// Drag events
private void StartDrag(InventoryCell source, Vector2 pos) {
    _dragging = true;
    GameMcpServer.Instance?.Log("inventory", $"拖拽开始: {source.Data.ItemName}");
}

private void EndDrag(InventoryCell target) {
    GameMcpServer.Instance?.Log("inventory", $"拖拽完成: {_dragSource.Data.ItemName} → {target?.Data.ItemName}");
}

// Toasts / messages
private void ShowToast(string msg) {
    _toastLabel.Text = msg;
    _toastLabel.Visible = true;
    GameMcpServer.Instance?.Log("ui", msg);
}
```

**Minimum events to log**: UI open/close, button actions, drag start/complete, scene transitions, player state changes, errors.

## Step 6: Register Metrics

Register once in `_Ready()` via `CallDeferred()`. NEVER register every frame.

```csharp
public override void _Ready()
{
    AddToGroup("player");
    CallDeferred(MethodName.RegisterMcpMetrics);
}

private void RegisterMcpMetrics()
{
    if (GameMcpServer.Instance == null) return;
    GameMcpServer.Instance.RegisterMetric("player_x", () => GlobalPosition.X);
    GameMcpServer.Instance.RegisterMetric("hp", () => Health);
}
```

## Step 7: Dual-Pipeline Input — Physical Mouse + MCP Virtual Mouse

**This step is OPTIONAL.** The MCP virtual mouse already emits both touch and mouse events at the OS-event level, so most games need no special handling. Only follow this pattern if your game unifies physical and virtual pointer input into a single custom field/pipeline.

### The Core Problem

Godot has two completely independent event pipelines:
- **Mouse events**: `InputEventMouseMotion`, `InputEventMouseButton`
- **Touch events**: `InputEventScreenDrag`, `InputEventScreenTouch`

HUD Controls with `mouse_filter="Stop"` **consume mouse events** in the GUI processing phase, preventing them from reaching `_UnhandledInput`. But touch events pass through Controls unimpeded.

### Architecture: Unified `_mouseWorldPos` via Dual Pipeline

Both input sources write to a single `_mouseWorldPos` field. All consumers (preview, click handling) read only this field.

```
Physical mouse motion  → InputEventMouseMotion  → _Input → _mouseWorldPos = GetGlobalMousePosition()
Virtual mouse motion   → InputEventScreenDrag   → _Input → _mouseWorldPos = ScreenToWorld(pos)
Physical mouse click   → InputEventMouseButton  → _Input → HandleClick(_mouseWorldPos)
Virtual mouse click    → InputEventScreenTouch  → _Input → HandleClick(_mouseWorldPos)
```

**Why `_Input` instead of `_UnhandledInput`?** Controls with `MouseFilter.Stop` consume `InputEventMouseButton` before `_UnhandledInput`. `_Input` fires BEFORE GUI processing, so it always receives events.

### Game Code Pattern

```csharp
private Vector2 _mouseWorldPos;

public override void _Input(InputEvent @event)
{
    if (CurrentPhase == GamePhase.Title || CurrentPhase == GamePhase.Settlement)
        return;

    // Physical mouse movement
    if (@event is InputEventMouseMotion)
    {
        _mouseWorldPos = GetGlobalMousePosition();
    }
    // MCP virtual mouse movement (touch drag, NOT blocked by HUD Controls)
    else if (@event is InputEventScreenDrag drag)
    {
        _mouseWorldPos = ScreenToWorld(drag.Position);
    }
    // MCP virtual mouse click (touch event)
    else if (@event is InputEventScreenTouch touch && touch.Pressed && touch.Index == 0)
    {
        _mouseWorldPos = ScreenToWorld(touch.Position);
        HandleClick(_mouseWorldPos);
    }
    // Physical mouse click (must be in _Input, not _UnhandledInput!)
    else if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
    {
        HandleClick(_mouseWorldPos);
    }
}

private Vector2 ScreenToWorld(Vector2 screenPos)
{
    var cam = GetNodeOrNull<Camera2D>("Camera2D");
    Vector2 camPos = cam != null ? cam.Position : Vector2.Zero;
    Vector2 zoom = cam != null ? cam.Zoom : new Vector2(22f, 22f);
    Vector2 vpSize = GetViewport().GetVisibleRect().Size;
    return (screenPos - vpSize / 2f) / zoom + camPos;
}

private void HandleClick(Vector2 mouseWorld)
{
    if (CurrentPhase == GamePhase.Combat || _timeScaler.IsPaused) return;
    // ... unified click logic: place tower, select object, etc.
}
```

### MCP Tools.Input: Send Both Touch + Mouse Events

The MCP server must send touch events (for game logic) AND mouse events (for HUD button compatibility):

```csharp
// MoveMouse: touch drag (game logic) + mouse motion (HUD cursor update)
private string MoveMouse(float x, float y)
{
    _simMousePos = new Vector2(x, y);
    Input.ParseInputEvent(new InputEventScreenDrag { Position = new Vector2(x, y), Index = 0 });
    Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y) });
    return $"{{\"ok\":true,\"x\":{x},\"y\":{y}}}";
}

// ClickMouse: touch press (game logic) + mouse button (UI button compat)
private string ClickMouse(string button, string action, float x, float y)
{
    _simMousePos = new Vector2(x, y);
    bool pressed = action == "press";
    Input.ParseInputEvent(new InputEventScreenTouch { Pressed = pressed, Position = new Vector2(x, y), Index = 0 });
    var btnIdx = button.ToLowerInvariant() switch { "right" => MouseButton.Right, "middle" => MouseButton.Middle, _ => MouseButton.Left };
    Input.ParseInputEvent(new InputEventMouseButton { Pressed = pressed, Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y), ButtonIndex = btnIdx });
    if (btnIdx == MouseButton.Left) _simMouseLeftDown = pressed;
    else if (btnIdx == MouseButton.Right) _simMouseRightDown = pressed;
    return $"{{\"ok\":true,\"button\":\"{button}\",\"x\":{x},\"y\":{y}}}";
}
```

### Key Design Decisions

1. **Touch events as virtual mouse channel**: `InputEventScreenTouch`/`InputEventScreenDrag` are NOT consumed by `mouse_filter="Stop"` Controls. This is the key advantage — virtual input reaches `_Input` regardless of HUD layout.

2. **Dual events for compatibility**: MCP sends both touch AND mouse events. Touch events drive game logic (placement, selection). Mouse events ensure `click_element` (via `EmitSignal`) and HUD buttons still work.

3. **No `_Process` polling needed**: All input is handled in `_Input` via Godot's native event pipeline. No need for `PollMcpMouse()` or frame-level polling.

4. **Coordinate conversion difference**: Physical mouse uses `GetGlobalMousePosition()` (already in world coords via Camera2D). Touch events only have screen coords and need manual `ScreenToWorld()` conversion.

5. **Phase guard in `_Input`**: Always check game phase at the top of `_Input` to avoid NullReferenceException when objects are being cleaned up.

## Step 8: Drag-and-Drop (Physical Mouse Priority)

For CanvasLayer-based drag-and-drop UI, physical mouse uses `_Input` with `InputEventMouseButton`/`InputEventMouseMotion`, and MCP simulated mouse uses `_Process` polling. Both share the same `StartDrag`/`EndDrag` logic.

**Physical mouse** goes through `_Input`. **MCP simulated mouse** is polled from `GameMcpServer.Instance` fields.

```csharp
// ── State ──────────────────────────────────────────────────────
private bool _physMouseDown;
private bool _dragging;
private Control _dragPreview;
private InventoryCell _dragSource;

// ── Keyboard shortcuts (in _UnhandledInput) ────────────────────
public override void _UnhandledInput(InputEvent ev)
{
    if (ev is InputEventKey { Pressed: true, Keycode: Key.I })
        _inventoryPanel.Visible = !_inventoryPanel.Visible;
}

// ── Physical mouse drag (in _Input, NOT _UnhandledInput!) ──────
// _Input fires before GUI processing, so it receives events even
// when child Controls have MouseFilter.Stop
public override void _Input(InputEvent ev)
{
    if (!_inventoryPanel.Visible) return;

    if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
    {
        if (mb.Pressed && !_dragging)
        {
            var cell = HitTestCell(mb.GlobalPosition);
            if (cell != null && !string.IsNullOrEmpty(cell.ItemName))
            {
                StartDrag(cell, mb.GlobalPosition);
                _physMouseDown = true;
            }
        }
        else if (!mb.Pressed && _physMouseDown && _dragging)
        {
            EndDrag(HitTestCell(mb.GlobalPosition));
            _physMouseDown = false;
        }
    }

    if (ev is InputEventMouseMotion mm && _dragging && _physMouseDown)
    {
        if (_dragPreview != null)
            _dragPreview.Position = mm.GlobalPosition;
    }
}

// ── MCP simulated mouse drag (polled in _Process) ─────────────
public override void _Process(double delta)
{
    if (_inventoryPanel.Visible)
        PollSimMouseForDrag();
}

private void PollSimMouseForDrag()
{
    if (_physMouseDown) return; // Physical mouse takes priority

    var mcp = GameMcpServer.Instance;
    if (mcp == null) return;

    var mousePos = mcp.SimMousePos;
    bool justPressed = mcp.SimMouseLeftJustPressed;
    bool justReleased = mcp.SimMouseLeftJustReleased;

    if (justPressed && !_dragging)
    {
        var cell = HitTestCell(mousePos);
        if (cell != null && !string.IsNullOrEmpty(cell.ItemName))
            StartDrag(cell, mousePos);
    }

    if (_dragging)
    {
        if (_dragPreview != null)
            _dragPreview.Position = mousePos;

        if (justReleased)
            EndDrag(HitTestCell(mousePos));
    }
}

// ── Hit test: find which inventory cell is under the cursor ────
private InventoryCell HitTestCell(Vector2 mousePos)
{
    for (int row = 0; row < Rows; row++)
        for (int col = 0; col < Cols; col++)
        {
            var cell = _cells[col, row];
            if (cell == null || !cell.Visible) continue;
            if (cell.GetGlobalRect().HasPoint(mousePos))
                return cell;
        }
    return null;
}
```

For buttons, `click_element` uses `EmitSignal(BaseButton.SignalName.Pressed)` which bypasses the CanvasLayer routing issue.

## Step 9: Map/Area Selection via Dual-Channel Polling

**Scenario**: Isometric map with drag-to-select tile regions. The drag happens on a Node2D (IsoScene), not a CanvasLayer Control, but the HUD that reads the drag state lives inside a CanvasLayer.

**Solution**: Merge both channels into a single `_Process` polling method with coordinate conversion:

```csharp
private void HandleZoneInput()
{
    if (_isoScene == null) return;

    bool physDown = Input.IsMouseButtonPressed(MouseButton.Left);
    var mcp = GameMcpServer.Instance;
    bool mcpDown = mcp != null && mcp.SimMouseLeftDown;
    bool mouseDown = physDown || mcpDown;

    if (mouseDown)
    {
        Vector2 worldPos;
        if (!physDown && mcpDown)
        {
            var vpSize = GetViewport().GetVisibleRect().Size;
            var cam = _isoScene.GetParent()?.GetNodeOrNull<Camera2D>("Camera2D");
            var camPos = cam != null ? cam.Position : Vector2.Zero;
            var zoom = cam != null ? cam.Zoom : new Vector2(22f, 22f);
            worldPos = (mcp.SimMousePos - vpSize / 2f) / zoom + camPos;
        }
        else
        {
            worldPos = _isoScene.ToLocal(_isoScene.GetGlobalMousePosition());
        }
        var tile = IsoScene.WorldToTile(worldPos);
        if (!IsInstanceValid(_isoScene)) return;
        if (!_isoScene.IsInBounds(tile.X, tile.Y)) return;

        if (!_dragging) { _dragging = true; _dragStartTile = tile; _dragEndTile = tile; }
        else { _dragEndTile = tile; }
    }
    else if (_dragging)
    {
        FinalizeDragRectangle();
        _dragging = false;
    }
}
```

**Key points**:
- `Input.IsMouseButtonPressed()` only reflects physical hardware — always check `mcp.SimMouseLeftDown` too
- `SimMousePos` is screen coordinates — must convert via camera zoom and position to get world coordinates
- Physical mouse uses `GetGlobalMousePosition()` / `ToLocal()`, MCP uses manual screen→world math
- Use `execute_macro` with `drag` action for MCP zone selection

**MCP test pattern** for selecting specific tiles — use single-point drags:
```json
{"steps": [
  {"action": "drag", "from_x": 516, "from_y": 268, "to_x": 516, "to_y": 268, "duration": 0.3, "button": "left"},
  {"action": "wait", "delay": 0.2},
  {"action": "drag", "from_x": 546, "from_y": 282, "to_x": 546, "to_y": 282, "duration": 0.3, "button": "left"},
  {"action": "wait", "delay": 0.2}
]}
```

## Build & Run

After code changes:

```bash
dotnet build --no-incremental    # force rebuild
# Then run game from Godot editor
```

If MCP tools don't appear, check for stale Godot processes:
```bash
tasklist | grep Godot
taskkill //F //PID <old_pid>
```

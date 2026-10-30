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
panelBtn.Pressed += () => {
    _panel.Visible = !_panel.Visible;
    GameMcpServer.Instance?.Log("ui", _panel.Visible ? "panel_open" : "panel_close");
};

// Drag events
private void StartDrag(DragSlot source, Vector2 pos) {
    _dragging = true;
    GameMcpServer.Instance?.Log("drag", $"drag_start: {source.Name}");
}

private void EndDrag(DragSlot target) {
    _dragging = false;
    GameMcpServer.Instance?.Log("drag", $"drag_end: {target?.Name}");
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

## Step 7: Choose a Virtual Pointer Integration

**This step is OPTIONAL.** The built-in pointer tools send mouse events through `Viewport.PushInput`, so standard Godot `Control` nodes and gameplay code that handles mouse events normally need no adapter. They do not emit a second touch-event stream and they do not enter through the operating system.

Use the normal event path first:

```csharp
public override void _Input(InputEvent @event)
{
    if (@event is InputEventMouseMotion motion)
        UpdatePointerPreview(motion.Position);
    else if (@event is InputEventMouseButton button &&
             button.ButtonIndex == MouseButton.Left && button.Pressed)
        HandlePointerAction(button.Position);
}
```

If the game deliberately polls a custom input abstraction instead of consuming Godot mouse events, read the playtester through `IInputProvider` in `_Process`. This keeps physical and automated input behind one game-owned contract without inventing touch events:

```csharp
private IInputProvider _testInput;

public override void _Ready()
{
    _testInput = GameMcpServer.Instance;
}

public override void _Process(double delta)
{
    if (_testInput == null) return;
    UpdatePointerPreview(_testInput.MousePosition);
    if (_testInput.MouseLeftJustPressed)
        HandlePointerAction(_testInput.MousePosition);
}
```

Keep evidence boundaries explicit: viewport injection can verify Godot-side event and GUI routing, while `mode="os"` is required for claims about window focus, operating-system occlusion, or DPI coordinate mapping. `click_element(force=true)` emits a signal directly and is only a diagnostic bypass.

## Step 8: Drag-and-Drop (Physical Mouse Priority)

For CanvasLayer-based drag-and-drop UI, physical mouse uses `_Input` with `InputEventMouseButton`/`InputEventMouseMotion`, and MCP simulated mouse uses `_Process` polling. Both share the same `StartDrag`/`EndDrag` logic.

**Physical mouse** goes through `_Input`. **MCP simulated mouse** is polled from `GameMcpServer.Instance` fields. `DragSlot` below stands for whatever your game drags: a card, a grid item, a unit.

```csharp
// ── State ──────────────────────────────────────────────────────
private bool _physMouseDown;
private bool _dragging;
private Control _dragPreview;

// ── Physical mouse drag (in _Input, NOT _UnhandledInput!) ──────
// _Input fires before GUI processing, so it receives events even
// when child Controls have MouseFilter.Stop
public override void _Input(InputEvent ev)
{
    if (!_dragArea.Visible) return;

    if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
    {
        if (mb.Pressed && !_dragging)
        {
            var slot = HitTestSlot(mb.GlobalPosition);
            if (slot != null)
            {
                StartDrag(slot, mb.GlobalPosition);
                _physMouseDown = true;
            }
        }
        else if (!mb.Pressed && _physMouseDown && _dragging)
        {
            EndDrag(HitTestSlot(mb.GlobalPosition));
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
    if (_dragArea.Visible)
        PollSimMouseForDrag();
}

private void PollSimMouseForDrag()
{
    if (_physMouseDown) return; // Physical mouse takes priority

    var mcp = GameMcpServer.Instance;
    if (mcp == null) return;

    var mousePos = mcp.SimMousePos;

    if (mcp.SimMouseLeftJustPressed && !_dragging)
    {
        var slot = HitTestSlot(mousePos);
        if (slot != null)
            StartDrag(slot, mousePos);
    }

    if (_dragging)
    {
        if (_dragPreview != null)
            _dragPreview.Position = mousePos;

        if (mcp.SimMouseLeftJustReleased)
            EndDrag(HitTestSlot(mousePos));
    }
}

// ── Hit test: find the draggable slot under the cursor ─────────
private DragSlot HitTestSlot(Vector2 mousePos)
{
    foreach (var slot in _slots)
        if (slot.Visible && slot.GetGlobalRect().HasPoint(mousePos))
            return slot;
    return null;
}
```

Buttons need none of this: `click_element` goes through viewport GUI picking. Its `force:true` option emits the pressed signal directly, which is a diagnostic bypass rather than proof a player can click.

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

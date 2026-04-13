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

## Step 7: Handle CanvasLayer Drag-and-Drop

If your UI is inside a `CanvasLayer`, Godot's built-in drag-and-drop doesn't work. Implement dual-source polling:

1. **Physical mouse** — poll in `_Process` via `Input.IsMouseButtonPressed()` and `_hudRoot.GetGlobalMousePosition()`
2. **MCP simulated mouse** — poll in `_Process` via `GameMcpServer.Instance.SimMousePos` and edge detection

Use a `_dragFromRealMouse` flag to prevent cross-contamination between the two sources. See SKILL.md "Mouse Polling for Drag-and-Drop" for the complete code pattern.

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

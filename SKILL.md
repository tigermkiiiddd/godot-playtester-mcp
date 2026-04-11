---
name: godot-playtester-mcp
description: |
  Deploy a playtester MCP server into a running Godot game so AI agents can inspect, control, and test it.
  Use when the user wants to add MCP to a Godot game, let AI test/play their game, do balance testing, or remote-control a running Godot game.
  IMPORTANT: Always use MCP tools directly. NEVER use curl + python to query the MCP server — the MCP adapter handles all JSON/encoding automatically.
---

# Godot Playtester MCP

Embeds an MCP server into a Godot 4 (.NET) game. AI agents can query game state, simulate input, capture screenshots, monitor metrics, and run automated tests.

## When to Use

- User wants to add MCP to their Godot game
- User wants AI to test/play their game
- User wants to remote-control a running Godot game from Claude Code
- User wants to do balance testing or automated QA

## Quick Start

Read `${CLAUDE_SKILL_DIR}/deploy.md` for the full deployment guide. Summary:

1. Copy all `GameMcpServer*.cs` + `RingBuffer.cs` files (14 partial class files) into the project
2. Add as Autoload in Project Settings
3. Tag game objects with Godot Groups (see Protocol below)
4. Run the game — MCP server starts on `http://localhost:9876`

## Two Testing Modes

### Real-time Control (Agent drives)

Agent queries game state, makes decisions, sends input, queries again.

```
get_game_state → see enemies/positions → press_key(move) → get_game_state → click_mouse(attack) → ...
```

### Background Test (Script drives)

Agent launches a test scene, it runs autonomously, agent collects results when done.

```
start_test(scene, duration=30) → ... wait ... → get_test_results(metrics, screenshots, logs)
```

## Game Development Protocol

The MCP server reads game state through **Godot Groups** and **Control node names**. Games must follow these conventions for MCP to work.

### Group Naming Convention

Tag all game objects with standard groups. The MCP server queries these groups automatically.

| Group | Purpose | Required |
|-------|---------|----------|
| `player` | Player node (must be exactly one) | Yes |
| `enemies` | Enemy characters | Recommended |
| `npcs` | NPC characters | Recommended |
| `items` | Ground pickups | Recommended |
| `interactables` | Doors, chests, switches | Recommended |
| `projectiles` | Bullets, spells | Optional |
| `triggers` | Trigger zones | Optional |
| `mcp_ui` | UI node that should appear in `get_ui_layout` whitelist | For non-standard controls |
| `mcp_ignore` | Never appear in `get_ui_layout` (hard exclusion) | For noisy nodes |

```csharp
// In your game scripts:
public override void _Ready()
{
    AddToGroup("player");
    // or: AddToGroup("enemies");
}
```

### UI Naming Convention

**Node names MUST be ASCII-only (English).** All Control nodes that need MCP interaction (clicking, typing, dragging) must use English names. Chinese/Unicode node names break `click_element`, `call_node_method`, and `get_node_properties` when requests pass through Windows terminals (GBK encoding corrupts non-ASCII characters). Display text (`Text` property) can still be Chinese — only the `Name` property must be ASCII.

```csharp
// WRONG — Chinese node name, click_element will fail
var healBtn = new Button { Name = "治疗按钮", Text = "治疗" };

// CORRECT — English node name, Chinese display text
var healBtn = new Button { Name = "HealBtn", Text = "治疗" };
```

Name all UI Control nodes descriptively. MCP extracts type, text, value, and rect automatically.

### get_ui_layout Whitelist Rules

`get_ui_layout` uses a **whitelist** approach by default (`tagged_only=true`):

**Always included (whitelist):**
- Nodes with `mcp_data` metadata (game code registered domain data)
- Nodes in `mcp_ui` group (game code explicitly tagged)
- Interactive control types: `Button`, `Label`, `LineEdit`, `TextEdit`, `CheckBox`, `OptionButton`, `ProgressBar`, `Slider`, `SpinBox`, `TabBar`, `ItemList`, `TextureRect`, `RichTextLabel`

**Always excluded:**
- Nodes in `mcp_ignore` group (hard exclusion, even with `tagged_only=false`)
- Everything else (ColorRect, plain Control, etc.)

**Parameters:**
- `tagged_only=true` (default): whitelist mode, only show useful nodes
- `tagged_only=false`: show everything (except `mcp_ignore`)
- `path="/root/root/HUDLayer/HUD"`: only inspect subtree at given path (local view)
- `max_depth=8`: limit recursion depth
- `visible_only=true` (default): skip invisible nodes
- `types="Button,Label"`: filter by control type names

```csharp
// MCP will extract: type=Button, text=Attack, rect=[10,500,80,30]
var attackBtn = new Button { Name = "AttackBtn", Text = "Attack" };

// MCP will extract: type=ProgressBar, value=85, max_value=100
var hpBar = new ProgressBar { Name = "HPBar", MaxValue = 100, Value = 85 };

// MCP will extract: type=Label, text="Score: 1520"
var scoreLabel = new Label { Name = "ScoreLabel", Text = "Score: 1520" };
```

### Custom Control Data Convention (mcp_data)

Custom controls can expose domain-specific data to `get_ui_layout` via `SetMeta("mcp_data", jsonString)`. This appears as a `"data"` field in the UI tree output.

```csharp
// Example: inventory cell exposing item details
public void Refresh()
{
    SetMeta("mcp_data", new JsonObject
    {
        ["item_name"] = ItemName,
        ["item_icon"] = "iron_sword",
        ["item_count"] = ItemCount,
        ["item_rarity"] = "common",
        ["item_type"] = "weapon",
        ["item_description"] = "A basic iron sword",
        ["item_weight"] = 3.5,
        ["item_value"] = 120,
    }.ToJsonString());
}
```

`get_ui_layout` will return this cell with:
```json
{"type": "InventoryCell", "name": "Cell_0_0", "rect": [...], "data": {"item_name": "铁剑", "item_icon": "iron_sword", ...}}
```

Note: Node `Name` is ASCII (`Cell_0_0`), but `mcp_data` values can be any language (`铁剑`).

### Mouse Polling for Drag-and-Drop

`Input.ParseInputEvent` does NOT route events to CanvasLayer children. For drag-and-drop in CanvasLayer-based UI, game scripts need **both** physical mouse support (via `_Input`) and MCP simulated mouse support (via `_Process` polling).

**IMPORTANT**: Use `_Input` (not `_UnhandledInput`) for physical mouse drag. Controls with `MouseFilter.Stop` consume events before `_UnhandledInput` fires. `_Input` fires before GUI processing and always receives the events.

**Physical mouse** goes through `_Input` with `InputEventMouseButton` and `InputEventMouseMotion`. **MCP simulated mouse** is polled from `GameMcpServer.Instance` fields. Both share the same `StartDrag` / `EndDrag` logic.

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

### Metric Registration Convention

Register numeric metrics for time-series monitoring during tests.

```csharp
public override void _Ready()
{
    CallDeferred(RegisterMcpMetrics);
}

private void RegisterMcpMetrics()
{
    if (GameMcpServer.Instance == null) return;

    // Simple getter
    GameMcpServer.Instance.RegisterMetric("player_hp", () => Health);
    GameMcpServer.Instance.RegisterMetric("score", () => Score);

    // Node property reader
    GameMcpServer.Instance.RegisterMetric("enemy_count",
        "/root/Main", "enemy_count");

    // Computed value
    GameMcpServer.Instance.RegisterMetric("kda",
        () => $"{Kills}/{Deaths}/{Assists}", sampleRate: 2.0);
}
```

### Log Registration Convention

Three-tier logging for AI debugging. **Every `GD.Print()` for important events MUST be paired with a corresponding MCP log call.**

```csharp
// AI Log — important events (ring buffer, MCP queryable)
// This is the PRIMARY log tier. Use for anything the AI agent needs to verify.
GameMcpServer.Instance?.Log("scene", "Loading scene: BattleArena");
GameMcpServer.Instance?.LogWarn("player", "HP below 20%");
GameMcpServer.Instance?.LogError("system", "Save file corrupted");

// File Log — high-volume data (disk, MCP queryable)
// Use for combat damage, economy transactions, etc.
GameMcpServer.Instance?.FileLog("combat", $"enemy={enemyId} damage={dmg} hp={hp}");
GameMcpServer.Instance?.FileLog("economy", $"spent={amount} on={item}");

// Debug Log — replaces GD.Print for MCP visibility
// Use for development debug output
GameMcpServer.Instance?.DebugLog("Pathfinding recalculated, nodes=42");
```

**Best practice — pair every GD.Print with MCP Log:**

```csharp
// UI button callbacks — ALWAYS log
invBtn.Pressed += () => {
    _inventoryPanel.Visible = !_inventoryPanel.Visible;
    GameMcpServer.Instance?.Log("ui", _inventoryPanel.Visible ? "背包打开" : "背包关闭");
};

// Drag events — ALWAYS log start and end
private void StartDrag(InventoryCell source, Vector2 pos) {
    _dragging = true;
    GameMcpServer.Instance?.Log("inventory", $"拖拽开始: {source.Data.ItemName} ({source.Col},{source.Row})");
}

private void EndDrag(InventoryCell target) {
    GameMcpServer.Instance?.Log("inventory", $"拖拽完成: {_dragSource.Data.ItemName} → {(target?.Data.ItemName ?? "空格")}");
}

// Toast/message — ALWAYS log so agent can verify
private void ShowToast(string msg) {
    _toastLabel.Text = msg;
    _toastLabel.Visible = true;
    GameMcpServer.Instance?.Log("ui", msg);
}
```

### CRITICAL: Log Closed-Loop Rule

Every significant game action **must** write to at least one MCP log tier (AI Log preferred). This ensures the AI agent can verify its operations by reading logs — not just trusting `ok: true` returns.

**Rule**: `GD.Print()` alone is NOT sufficient. It only goes to Godot console (invisible to AI). Add `GameMcpServer.Instance?.Log()` alongside every `GD.Print()` for important events.

```csharp
// WRONG — only Godot console, AI can't verify
GD.Print("[背包] 打开");

// CORRECT — both Godot console AND MCP AI Log
GD.Print("[背包] 打开");
GameMcpServer.Instance?.Log("ui", "背包打开");
```

**Minimum events that MUST log:**
- UI open/close (panels, dialogs, menus)
- Button clicks that trigger game actions (heal, buy, craft)
- Drag-and-drop start/complete
- Scene transitions
- Player state changes (HP change, level up, death)
- Error/failure conditions

**Verification pattern** (agent-side):
```
1. Agent sends command (click_element, execute_macro, etc.)
2. Agent reads get_logs() to verify game logged the action
3. If log entry missing → operation may not have reached game code
```

## Built-in Tools

### State & Structure (primary feedback channels)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `get_game_state` | Structured world state by Groups. Returns positions, distances. **Main feedback channel.** | `groups`, `radius`, `near_x/y`, `limit`, `offset` |
| `get_ui_layout` | UI Controls as **nested tree** (like DOM). Type, name, rect, text, children, focus, editable. **Whitelist mode** by default: only shows tagged/interactive nodes. Grid compression: `detail=compact` (default, core fields only) or `detail=full` (all mcp_data fields). | `visible_only`, `types`, `tagged_only` (default true), `path` (subtree root), `max_depth` (default 8), `detail` (compact/full) |
| `get_scene_tree` | Full scene tree structure | none |
| `get_node_properties` | Properties of a specific node | `path` |
| `get_game_info` | FPS, window size, engine version | none |

### UI Control

| Tool | Description | Parameters |
|------|-------------|------------|
| `click_element` | Click UI element by name or path (uses rect center) | `name`, `path`, `button`, `offset_x/y` |
| `type_text` | Input text into LineEdit/TextEdit (set or type mode) | `target`, `text`, `mode` (set/type) |
| `select_option` | Select item in OptionButton/ItemList by index | `name`, `path`, `index` |
| `get_focused_element` | Get currently focused UI Control | none |
| `drag` | Drag from one point to another (uses macro engine) | `from_x/y`, `to_x/y`, `duration`, `button` |
| `double_click` | Double-click at screen coordinates | `button`, `x`, `y` |
| `hover` | Move mouse to position | `x`, `y` |

### Input Control

| Tool | Description | Parameters |
|------|-------------|------------|
| `press_key` | Raw keyboard key (W, Space, Escape, etc) | `key`, `action` (press/release) |
| `click_mouse` | Mouse button at screen coords | `button` (left/right/middle), `action`, `x`, `y` |
| `move_mouse` | Move mouse cursor. Without `duration`: instant teleport. With `duration`: smooth animated move. | `x`, `y`, `duration` |
| `scroll_mouse` | Mouse wheel | `amount` (int) |
| `simulate_input` | Mapped action (ui_up, ui_accept) | `action`, `key` |

### Visual & Metrics

| Tool | Description | Parameters |
|------|-------------|------------|
| `screenshot` | Capture frame as base64 image. **Use sparingly — prefer structured queries.** | `format` (jpeg/png), `quality` |
| `register_metric` | Register a numeric metric | `name`, `source_type`, `node_path`, `sample_rate` |
| `get_metrics` | Get metric values | `names`, `format` (latest/timeline/csv) |

### Test Runner

| Tool | Description | Parameters |
|------|-------------|------------|
| `start_test` | Start background test scene | `scene_path`, `duration`, `capture_frames` |
| `get_test_results` | Get test results | `test_id`, `include` (all/metrics/screenshots/logs) |

### Log System (three-tier)

| Tool | Description | Parameters |
|------|-------------|------------|
| `get_logs` | Query AI log ring buffer (important events) | `min_level`, `category`, `since`, `limit`, `order` |
| `get_debug_logs` | Query debug log ring buffer (GD.Print replacement) | `min_level`, `since`, `limit` |
| `log` | Write an AI agent log entry | `message`, `level`, `category` |
| `get_file_logs` | Read recent entries from a file log | `category`, `limit` |
| `get_file_log_summary` | File log statistics per category | none |
| `clear_logs` | Clear all ring buffers | none |

**Three log tiers:**

1. **AI Log** — Important game events (scene change, death, level up, UI open/close). Ring buffer 1000 entries. Game code: `GameMcpServer.Instance?.Log("scene", "Loading BattleArena")`
2. **File Log** — High-volume data (combat damage, economy). Written to `user://mcp_logs/category_date.log`. Game code: `GameMcpServer.Instance?.FileLog("combat", $"enemy={id} damage={dmg}")`
3. **Debug Log** — Replaces GD.Print for MCP visibility. Ring buffer 2000 entries. Game code: `GameMcpServer.Instance?.DebugLog("Pathfinding recalculated")`

### Macro System (scripted input sequences)

| Tool | Description | Parameters |
|------|-------------|------------|
| `execute_macro` | Execute a timed input sequence. Returns `macro_id` immediately. | `steps` (array), `name` (optional) |
| `get_macro_status` | Get progress of a running macro | `macro_id`, `include` (all/steps) |
| `cancel_macro` | Cancel running macro, release all held keys | `macro_id` |
| `list_macros` | List macros, optionally filtered by status | `status` (running/completed/cancelled/error) |

**Step action types**: `hold_key` (press for duration), `tap_key` (instant press+release), `repeat_key` (press N times with interval), `combo_keys` (multiple keys simultaneously), `move_distance` (hold direction until player moves X pixels), `move_to` (walk to target world coordinates, supports 8dir/4dir/free), `click` (mouse), `drag` (press-move-release), `double_click` (fast two clicks), `type_text` (character-by-character input), `wait` (delay).

**move_to modes** (set `mode` parameter):
- `8dir` (default) — diagonal allowed, WASD, axes finish independently. For 8-directional games.
- `4dir` — single axis only, L-shaped path (X then Y). For 4-directional games.
- `free` — smooth any-angle movement, directly sets position at player's own Speed. For free-movement games.

```json
// Example: diagonal walk 0.5s → wait 0.3s → attack
{"steps": [
  {"action": "combo_keys", "keys": "W,D", "duration": 0.5},
  {"action": "wait", "delay": 0.3},
  {"action": "tap_key", "keys": "F"}
]}

// Example: repeat attack 10 times
{"steps": [{"action": "repeat_key", "keys": "F", "count": 10, "interval": 0.5}]}

// Example: move right 200 pixels
{"steps": [{"action": "move_distance", "direction": "right", "distance": 200}]}

// Example: walk to world coordinate (8dir, diagonal)
{"steps": [{"action": "move_to", "target_x": 300, "target_y": -200}]}

// Example: walk to coordinate (4dir, L-path)
{"steps": [{"action": "move_to", "target_x": 300, "target_y": -200, "mode": "4dir"}]}

// Example: smooth any-angle move (free mode)
{"steps": [{"action": "move_to", "target_x": 150, "target_y": 80, "mode": "free"}]}

// Example: drag from (100,200) to (400,300) over 0.5s
{"steps": [{"action": "drag", "from_x": 100, "from_y": 200, "to_x": 400, "to_y": 300, "duration": 0.5}]}

// Example: double-click at position
{"steps": [{"action": "double_click", "x": 300, "y": 400, "button": "left"}]}

// Example: type text character by character
{"steps": [{"action": "type_text", "text": "Hello World", "char_interval": 0.05}]}
```

### Node Manipulation

| Tool | Description | Parameters |
|------|-------------|------------|
| `set_node_property` | Set a property on a node | `path`, `property`, `value` |
| `call_node_method` | Call a method on a node | `path`, `method`, `args` |

## Agent Usage Guide

**IMPORTANT: NEVER use `curl` + `python` to query the MCP server.** Always use MCP tools directly through the configured MCP adapter. The adapter handles JSON parsing, Chinese text, and protocol details automatically. Raw curl on Windows terminals produces garbled Chinese and requires manual JSON parsing.

When testing a game, follow this priority:

1. **`get_game_state`** — understand world state (enemies, items, positions)
2. **`get_ui_layout`** — find buttons, read HP bars, check inventory UI
3. **`get_metrics`** — read numeric trends (HP over time, score progression)
4. **`press_key` / `click_mouse`** — take action based on structured data
5. **`screenshot`** — only when you need to see visuals (layout bugs, animations, missing sprites)

### Example: Agent picks up a health potion

```
1. get_game_state(groups="items") → finds HealthPotion at screen_pos [480, 290]
2. click_mouse(button="left", x=480, y=515)  // adjust for UI offset if needed
3. get_game_state() → confirms item picked up, player HP changed
```

### Example: Agent tests balance (Hero A vs Hero B)

```
1. register_metric(name="hero_a_hp", source_type="node_property", node_path="/root/Battle/HeroA", property_name="Health")
2. register_metric(name="hero_b_hp", source_type="node_property", node_path="/root/Battle/HeroB", property_name="Health")
3. start_test(scene_path="res://test/BattleSim.tscn", duration=60, capture_frames=5)
4. ... wait 60 seconds ...
5. get_test_results(test_id="test_001", include="all")
6. Analyze metrics_timeline: who died first? HP curves?
```

### Example: Agent walks and attacks using macro

```
1. get_game_state() → see enemy at distance
2. execute_macro(name="approach_and_attack", steps=[
     {"action": "hold_key", "keys": "W", "duration": 1.5},
     {"action": "wait", "delay": 0.2},
     {"action": "tap_key", "keys": "F"}
   ]) → macro_001 running
3. get_macro_status(macro_id="macro_001") → wait for completion
4. get_game_state() → verify enemy dead or player position changed
```

## Connecting Claude Code

```json
{
  "mcpServers": {
    "godot-playtester": {
      "command": "npx",
      "args": ["-y", "@anthropic-ai/mcp-adapter-http", "http://localhost:9876"]
    }
  }
}
```

## Minimal Quick-Start Example

A complete working test: WASD player with trail, MCP reads position and controls movement.

### 1. Scene Structure (created via Godot MCP `create_scene` + `add_node`)

```
TrailTest (Node2D)
├── Player (CharacterBody2D) [group: "player"]
│   ├── CollisionShape2D
│   │   └── Shape: RectangleShape2D (4x4)
│   └── TrailLine (Line2D)
├── Background (ColorRect)
└── Camera2D
```

### 2. Player Script (`scripts/Player.cs`)

```csharp
using Godot;
using System.Collections.Generic;

public partial class Player : CharacterBody2D
{
    [Export] public float Speed { get; set; } = 200f;
    [Export] public float TrailInterval { get; set; } = 0.05f;

    private float _trailTimer;
    private readonly List<Vector2> _trailPoints = new();
    private const int MaxTrailPoints = 200;

    public override void _Ready()
    {
        AddToGroup("player");
        CallDeferred(MethodName.RegisterMcpMetrics);
    }

    public override void _PhysicsProcess(double delta)
    {
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) input.Y += 1;
        if (Input.IsKeyPressed(Key.A)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D)) input.X += 1;

        Velocity = input.Normalized() * Speed;
        MoveAndSlide();

        if (Velocity.LengthSquared() > 1f)
        {
            _trailTimer += (float)delta;
            if (_trailTimer >= TrailInterval)
            {
                _trailTimer = 0f;
                _trailPoints.Add(GlobalPosition);
                if (_trailPoints.Count > MaxTrailPoints)
                    _trailPoints.RemoveAt(0);
            }
        }

        var trail = GetNodeOrNull<Line2D>("TrailLine");
        if (trail != null)
        {
            trail.ClearPoints();
            foreach (var pt in _trailPoints)
                trail.AddPoint(ToLocal(pt));
        }
    }

    private void RegisterMcpMetrics()
    {
        if (GameMcpServer.Instance == null) return;
        GameMcpServer.Instance.RegisterMetric("player_x", () => GlobalPosition.X);
        GameMcpServer.Instance.RegisterMetric("player_y", () => GlobalPosition.Y);
        GameMcpServer.Instance.RegisterMetric("trail_length", () => _trailPoints.Count);
        GameMcpServer.Instance.RegisterMetric("speed", () => Velocity.Length());
    }
}
```

### 3. Verify via curl

```bash
# Read state → Player at (0,0)
curl -s -X POST http://localhost:9876 -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_game_state","arguments":{}}}'

# Move player up for 1 second
curl -s ... -d '{"...press_key...W...press"}'
sleep 1
curl -s ... -d '{"...press_key...W...release"}'

# Read metrics → player_y changed, trail_length > 0
curl -s ... -d '{"...get_metrics...latest"}'
# Result: {"player_x":0,"player_y":-256.667,"trail_length":25,"speed":0}
```

## Pitfalls & Gotchas

See **debugging.md** for full diagnosis table and detailed fixes.

**Most common issues:**
- MCP tools not appearing → `HandleToolsList()` must use lowercase keys (`name`, `description`, `inputSchema`)
- Chinese node names fail → Use ASCII-only `Name` property, any-language `Text`
- `JsonNode parent` error → Use `id?.DeepClone()` in RpcResult
- Drag cancels immediately → Add `_dragFromRealMouse` flag for mutual exclusion
- `tap_key` not working → Use `hold_key` with duration instead
- Stale DLL → `dotnet build --no-incremental`
- Multiple Godot processes → `tasklist | grep Godot` and kill old one

## Playtest Template

See **testing.md** for the full three-phase playtest process (Prep → Execute → Report).

**Key rules:**
1. Write test cases BEFORE operating (`log` intent)
2. Test one thing at a time, verify with `get_logs()` after each
3. Every test must have log evidence (closed-loop)

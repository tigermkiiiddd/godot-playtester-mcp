---
name: godot-play-tester
description: |
  Deploy a runtime MCP server into a running Godot 4 (.NET) game for AI-driven playtesting and automated QA.
  Use when the user wants to: test a Godot game, playtest gameplay, run automated game tests, balance test,
  simulate player input, capture game screenshots, monitor game metrics, verify game behavior,
  remote-control a running Godot game, or do closed-loop game testing.
  Includes a three-phase testing workflow with MANDATORY boundary/edge case testing: Prep (write test cases including cancel/backward/state-recovery scenarios) → Execute (happy path + boundary cases, one at a time with log verification) → Report (evidence-based summary).
---

# Godot Play Tester

Embeds an MCP server (all classes in the `GodotPlaytester` namespace) into a Godot 4 (.NET) game. AI agents can query game state, simulate input, capture screenshots, monitor metrics, and run automated tests.

The library core is **genre-free**: state query, UI control, raw input simulation, macros, metrics, logging, and testing work the same for an action game, a turn-based tactics game, a point-and-click adventure, or a card game. A couple of helpers (`move_to`/`move_distance`) assume a single WASD-style avatar and are called out below as action-game-specific. For anything else, games expose their own vocabulary as MCP tools — see "Adapting to Your Game (Semantic Tools)".

## When to Use

- User wants to add MCP to their Godot game
- User wants AI to test/play their game
- User wants to remote-control a running Godot game from Claude Code
- User wants to do balance testing or automated QA

## Requirements

### Required
- Godot 4.6+ with .NET/C# support
- The server only runs in **debug builds** — release builds skip it entirely, so it never ships to players

### Recommended
- An editor-side MCP server for creating scenes, adding nodes, running the project, and reading debug output (e.g. [godot-mcp](https://github.com/Coding-Solo/godot-mcp)). Pairs with this runtime MCP for a full edit→build→run→test loop, but is not required — the runtime server works standalone against any running debug build.

## Installation

Two options:

**(a) Git submodule** (tracks upstream):
```bash
git submodule add https://github.com/tigermkiiiddd/godot-playtester-mcp.git addons/godot_playtester_mcp
```
Then add `res://addons/godot_playtester_mcp/src/GameMcpServer.cs` as an Autoload in Project Settings.

**(b) Copy files**: copy all 15 files from `src/` anywhere in your project, then add `GameMcpServer.cs` as an Autoload.

Either way: all classes live in the `GodotPlaytester` namespace, the server starts only in debug builds, and it listens on `http://localhost:9876` by default (`[Export] public int Port`).

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

The MCP server reads game state through **Godot Groups** you tag objects with, and **Control node names**. Some conventions below are real library contracts (`mcp_ui`, `mcp_ignore`, ASCII node names); others are just a default vocabulary you're free to replace with your own via registered tools.

### Group Naming Convention

`get_game_state` discovers world objects through Godot Groups. The table below is a **default vocabulary** suited to action/avatar games — it is not required. Games define their own ontology; `get_game_state(groups=...)` accepts any group names you've tagged objects with.

| Group | Purpose | Notes |
|-------|---------|-------|
| `player` | Player-controlled unit | Only needed by tools that reference "the player" specifically, e.g. `move_to`/`radius` filtering. Turn-based, multi-unit, or non-avatar games can skip it entirely and query their own groups. |
| `enemies` | Enemy characters | Default suggestion |
| `npcs` | NPC characters | Default suggestion |
| `items` | Ground pickups | Default suggestion |
| `interactables` | Doors, chests, switches | Default suggestion |
| `projectiles` | Bullets, spells | Default suggestion |
| `triggers` | Trigger zones | Default suggestion |
| `mcp_ui` | UI node that should appear in `get_ui_layout` whitelist | Library contract — for non-standard controls |
| `mcp_ignore` | Never appear in `get_ui_layout` (hard exclusion) | Library contract — for noisy nodes |

```csharp
// In your game scripts:
public override void _Ready()
{
    AddToGroup("player");
    // or: AddToGroup("enemies"); or any group name your game defines
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

### Button Callback Convention (Defense in Depth)

By default `click_element` refuses to click a `Disabled` or not-visible button (a real player couldn't click it either) — see Pitfalls. Passing `force:true` bypasses that check, and also skips hit-testing, so it should be used sparingly (e.g. deliberately confirming a disabled button truly does nothing when triggered).

Because `force` exists, button callbacks should still guard their own preconditions defensively:

```csharp
// Defensive — callback validates its own preconditions even if the signal fires unexpectedly
healBtn.Pressed += () => {
    if (healBtn.Disabled) return;
    DoHeal();
};
```

**Principle**: MCP operates at the signal level, it does not replicate UI state checks. Game code owns its own state guards.

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

### Virtual Mouse Input

The MCP virtual mouse (`click_mouse`, `move_mouse`, `drag`, and macro `click`/`drag` steps) emits both touch events (`InputEventScreenTouch`/`InputEventScreenDrag`) and mouse events (`InputEventMouseButton`/`InputEventMouseMotion`) at the OS-event level, so most games need no special handling — it looks like ordinary input to both UI Controls and gameplay code.

If your game unifies physical and virtual pointer input into a single custom pipeline (e.g. one field read by both click-to-move and drag-select logic), see `docs/integration.md` for that (optional) pattern.

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

## Adapting to Your Game (Semantic Tools)

This is the **primary** mechanism for fitting the MCP surface to your game's genre. The library core is genre-free; genre vocabulary belongs to the game, exposed via tools the game registers itself:

```csharp
public override void _Ready()
{
    CallDeferred(RegisterMcpTools);
}

private void RegisterMcpTools()
{
    if (GameMcpServer.Instance == null) return;

    GameMcpServer.Instance.RegisterTool(
        "my_move_unit",
        "Move a unit to a target grid cell on the tactics grid.",
        Schema("{\"unit_id\":{\"type\":\"string\"},\"target_cell\":{\"type\":\"string\"}}", "object"),
        args =>
        {
            var unitId = args["unit_id"].GetString();
            var cell = args["target_cell"].GetString();
            BattleController.Instance.RequestMove(unitId, cell); // fire-and-forget
            return "{\"ok\":true}";
        });

    // e.g. for a tactics game: my_use_skill, my_end_turn, my_select_unit ...
}
```

Route each handler through your game's own logic layer — the same code path a human click or keypress would trigger — instead of reimplementing rules inside the handler.

**Threading rule**: handlers run on the Godot main thread. Don't `await` a long-running or async game operation inline — fire it off (queue an action, set a flag, kick off a coroutine-equivalent) and return immediately. Have the agent poll `get_game_state`, `get_logs`, or a registered query tool to observe when the action completes.

## Built-in Tools

### State & Structure (primary feedback channels)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `get_game_state` | Structured world state by Groups. Returns positions, distances. **Main feedback channel.** | `groups`, `radius`, `near_x/y`, `limit`, `offset` |
| `get_ui_layout` | UI Controls as **nested tree** (like DOM). Type, name, rect, text, children, focus, editable. **Whitelist mode** by default: only shows tagged/interactive nodes. Grid compression: `detail=compact` (default, core fields only) or `detail=full` (all mcp_data fields). | `visible_only`, `types`, `tagged_only` (default true), `path` (subtree root), `max_depth` (default 8), `detail` (compact/full) |
| `get_scene_tree` | Full scene tree structure, recursive | `max_depth` (default 6) |
| `get_node_properties` | Properties of a specific node | `path` |
| `get_game_info` | FPS, window size, engine version | none |

### UI Control

| Tool | Description | Parameters |
|------|-------------|------------|
| `click_element` | Click UI element by name or path (uses rect center). Refuses `Disabled` or not-visible buttons by default — a real player couldn't click them. `force:true` bypasses that check (and hit-testing); use sparingly. `mode:"os"` performs a real click (SendInput) with true hit-testing instead of the default engine-level signal/event injection. | `name`, `path`, `button`, `offset_x/y`, `force`, `mode` (virtual/os) |
| `type_text` | Input text into LineEdit/TextEdit (set or type mode) | `target`, `text`, `mode` (set/type) |
| `select_option` | Select item in OptionButton/ItemList by index | `name`, `path`, `index` |
| `get_focused_element` | Get currently focused UI Control | none |
| `drag` | Drag from one point to another (uses macro engine) | `from_x/y`, `to_x/y`, `duration`, `button` |
| `double_click` | Double-click at screen coordinates | `button`, `x`, `y`, `mode` (virtual/os) |
| `hover` | Move mouse to position | `x`, `y` |

### Input Control

Two input channels for the mouse tools below, selected via `mode`:

| mode | How | Trade-off |
|------|-----|-----------|
| `virtual` (default) | Injects `InputEventMouse*` straight into the engine (`Input.ParseInputEvent`) | Fast, headless-ok — but bypasses the real OS→window→engine chain, so it can't catch (or reproduce) focus/occlusion/`mouse_filter`/DPI bugs |
| `os` | Drives the real system cursor via Win32 `SendInput`/`SetCursorPos` | True input chain, catches those bugs — requires a visible+focused window, Windows-only, occupies the physical mouse during the test; `move_mouse` ignores `duration` in this mode |

| Tool | Description | Parameters |
|------|-------------|------------|
| `press_key` | Raw keyboard key (W, Space, Escape, etc) | `key`, `action` (press/release) |
| `click_mouse` | Mouse button at screen coords | `button` (left/right/middle), `action`, `x`, `y`, `mode` (virtual/os) |
| `move_mouse` | Move mouse cursor. Without `duration`: instant teleport. With `duration`: smooth animated move (virtual mode only). | `x`, `y`, `duration`, `mode` (virtual/os) |
| `scroll_mouse` | Mouse wheel | `amount` (int) |
| `simulate_input` | Mapped action (ui_up, ui_accept) | `action`, `key` |

### Visual & Metrics

| Tool | Description | Parameters |
|------|-------------|------------|
| `screenshot` | Capture current frame, returned as an MCP image content block. **Use sparingly — prefer structured queries.** | `format` (jpeg/png), `quality`, `max_width` (default 960, downsizes oversized captures) |
| `register_metric` | Register a numeric metric | `name`, `source_type`, `node_path`, `sample_rate` |
| `get_metrics` | Get metric values | `names`, `format` (latest/timeline/csv) |

### Diagnostics

| Tool | Description | Parameters |
|------|-------------|------------|
| `time_scale` | Get or set `Engine.TimeScale` (range 0.1–20). Fast-forward playtests (e.g. long balance runs). Omit `scale` to just read the current value. | `scale` (optional) |

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

**`move_distance` and `move_to` are action-game helpers**: they simulate WASD-style movement keys/positions against a single `player`-group avatar. For turn-based, point-and-click, or strategy games, skip these two step types and register semantic tools instead (see "Adapting to Your Game" above) — e.g. a `my_move_unit` tool that moves a specific unit on a grid, rather than holding a direction key.

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

## Game Launch Sequence

Before any playtester MCP tool call, the game must be running. Sequence:

1. **`dotnet build`** — compile C# changes (many editor "run project" actions do not trigger C# compilation on their own)
2. **Launch the game** — prefer your editor-side MCP server's run tool if you have one (it manages process lifecycle and surfaces debug output for you); otherwise launch the Godot binary directly with `godot --path <project>` (or run an exported debug build)
3. **Playtester `get_game_info`** — confirm the game is running (check `uptime` > 0 and `session_id` is fresh)

Launching through an editor-managed process is preferred when available, for clean lifecycle management and captured debug output — but a plain shell launch works too; you'll just need your own way to read stdout/stderr and to clean up stray processes (see Pitfalls).

If code changes are needed mid-test:
```
stop the game → edit code → dotnet build → relaunch → get_game_info (verify fresh session)
```

## Agent Usage Guide

**Prefer MCP tools directly over shelling out to `curl`.** The MCP adapter handles JSON parsing, non-ASCII text, and protocol details automatically. On Windows terminals specifically, raw `curl` output is displayed through the GBK codepage, which garbles Chinese (or other non-ASCII) text and forces manual JSON parsing — use it only for isolated debugging, and be ready to re-decode the output.

When testing a game, follow this priority:

1. **`get_game_state`** — understand world state (enemies, items, positions)
2. **`get_ui_layout`** — find buttons, read HP bars, check inventory UI
3. **`get_metrics`** — read numeric trends (HP over time, score progression)
4. **`press_key` / `click_mouse`** (or your game's registered semantic tools) — take action based on structured data
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

### Example: Agent walks and attacks using macro (action game)

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

## Recommended: Pair with an Editor-Side MCP

For the best development workflow, use alongside an editor-side MCP server such as [godot-mcp](https://github.com/Coding-Solo/godot-mcp) (`@coding-solo/godot-mcp`). The two kinds of MCP servers complement each other:

| | Editor-side MCP | Playtester MCP (this library, runtime) |
|---|---|---|
| **Works when** | Editor is open | Game is running |
| **Does** | Create scenes, add nodes, run project, read debug output | Query game state, simulate input, capture screenshots, metrics |
| **Scope** | Project structure & assets | Live game simulation |

**Typical workflow:**
1. Editor-side MCP: create scene, add nodes, attach scripts
2. `dotnet build` (many editor "run project" actions do NOT trigger C# compilation)
3. Editor-side MCP: launch the game
4. Playtester MCP: `get_game_info` to confirm the game is running (check `uptime` and `session_id` fields)
5. Playtester MCP: test gameplay, verify behavior, capture metrics
6. If code changes needed: stop the game → edit code → `dotnet build` → relaunch

Launching through the editor-side MCP is convenient (it manages process lifecycle and provides debug output), but not required — the runtime server works the same regardless of how the game process was started. After launching, call `get_game_info` to confirm the session is fresh (check `uptime` and `session_id` — a new `session_id` means a fresh start).

## Connecting Claude Code

```json
{
  "mcpServers": {
    "godot": {
      "command": "npx",
      "args": ["@coding-solo/godot-mcp"],
      "env": {
        "GODOT_PATH": "path/to/godot executable"
      }
    },
    "godot-playtester": {
      "type": "http",
      "url": "http://localhost:9876"
    }
  }
}
```

Direct HTTP connection — no bridge script. The server negotiates MCP protocol versions (2024-11-05 / 2025-03-26 / 2025-06-18), returns 202 for notifications, and reports tool failures via `isError` per spec. When the game isn't running, the client reports a connection error for that server; other servers are unaffected.

## Pitfalls & Gotchas

See **docs/debugging.md** for full diagnosis table and detailed fixes.

**Most common issues:**
- Chinese node names fail → Use ASCII-only `Name` property, any-language `Text`
- `click_element` refuses a `Disabled` or not-visible button → intentional (a real player couldn't click it either). Check `get_ui_layout` for `disabled: true`, or pass `force:true` to bypass — but `force` also skips hit-testing, so use it sparingly
- `tap_key` not working → Use `hold_key` with duration instead
- Stale DLL → `dotnet build --no-incremental`
- Multiple Godot processes → find and kill stray old processes (e.g. `tasklist | grep Godot` on Windows) before relaunching

## Playtest Template

See **docs/testing.md** for the full three-phase playtest process (Prep → Execute → Report).

**Key rules:**
1. Write test cases BEFORE operating (`log` intent) — **IRON LAW, no exceptions**
2. Every test case MUST include boundary/edge cases (cancel, backward nav, state recovery) — not optional
3. Test one thing at a time, verify with `get_logs()` after each
4. Every test must have log evidence (closed-loop)
5. After boundary tests, verify game is NOT frozen: `get_game_state()` + `get_ui_layout()`

**Boundary test categories to check for every feature:**
- Cancel/abort mid-operation (cancel zone selection, close dialog midway)
- Backward navigation (go back to previous step in multi-step flow)
- State recovery (after interruption, is UI/game still usable?)
- Repeated actions (open/close rapidly, click twice)
- Out-of-order (skip steps, wrong sequence)

## Documentation Index

| Document | Content |
|----------|---------|
| `docs/deploy.md` | Step-by-step installation and configuration |
| `docs/integration.md` | Game code conventions, drag-and-drop patterns, CanvasLayer handling |
| `docs/testing.md` | Three-phase playtest process (Prep → Execute → Report) |
| `docs/test-case-template.md` | Copy-pasteable test case templates with naming conventions (Template A/B/C) |
| `docs/debugging.md` | Common pitfalls, root causes, and fixes |
| `docs/requirements.md` | Dependencies and recommended tools |
| `docs/quick-start.md` | Minimal working example (WASD player with trail) |

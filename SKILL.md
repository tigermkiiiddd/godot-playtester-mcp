---
name: godot-playtester-mcp
description: |
  Deploy a playtester MCP server into a running Godot game so AI agents can inspect, control, and test it.
  Use when the user wants to add MCP to a Godot game, let AI test/play their game, do balance testing, or remote-control a running Godot game.
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

1. Copy `GameMcpServer.cs` into the project
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

```csharp
// In your game scripts:
public override void _Ready()
{
    AddToGroup("player");
    // or: AddToGroup("enemies");
}
```

### UI Naming Convention

Name all UI Control nodes descriptively. MCP extracts type, text, value, and rect automatically.

```csharp
// MCP will extract: type=Button, text=Attack, rect=[10,500,80,30]
var attackBtn = new Button { Name = "AttackBtn", Text = "Attack" };

// MCP will extract: type=ProgressBar, value=85, max_value=100
var hpBar = new ProgressBar { Name = "HPBar", MaxValue = 100, Value = 85 };

// MCP will extract: type=Label, text="Score: 1520"
var scoreLabel = new Label { Name = "ScoreLabel", Text = "Score: 1520" };
```

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

## Built-in Tools

### State & Structure (primary feedback channels)

| Tool | Description | Key Parameters |
|------|-------------|----------------|
| `get_game_state` | Structured world state by Groups. Returns positions, distances. **Main feedback channel.** | `groups`, `radius`, `near_x/y`, `limit`, `offset` |
| `get_ui_layout` | All UI Controls with rect, text, value. **Use to find buttons to click.** | `visible_only`, `types` |
| `get_scene_tree` | Full scene tree structure | none |
| `get_node_properties` | Properties of a specific node | `path` |
| `get_game_info` | FPS, window size, engine version | none |

### Input Control

| Tool | Description | Parameters |
|------|-------------|------------|
| `press_key` | Raw keyboard key (W, Space, Escape, etc) | `key`, `action` (press/release) |
| `click_mouse` | Mouse button at screen coords | `button` (left/right/middle), `action`, `x`, `y` |
| `move_mouse` | Move mouse cursor | `x`, `y` |
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

### Node Manipulation

| Tool | Description | Parameters |
|------|-------------|------------|
| `set_node_property` | Set a property on a node | `path`, `property`, `value` |
| `call_node_method` | Call a method on a node | `path`, `method`, `args` |

## Agent Usage Guide

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

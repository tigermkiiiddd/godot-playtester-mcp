# Godot Playtester MCP

**Embed an MCP server into your Godot 4 (.NET) game, let AI agents play and test it.**

```
┌──────────────┐         HTTP          ┌─────────────────┐
│  AI Agent    │◄────────────────────►│  Godot Game      │
│  (Claude)    │   localhost:9876      │  (running)       │
│              │                       │                  │
│  read state  │   get_game_state  ──►│  player HP 85    │
│  click UI    │   click_element  ───►│  button pressed  │
│  press key   │   press_key  ───────►│  W key held      │
│  run macros  │   execute_macro  ───►│  auto-battle 30s │
│  screenshot  │   screenshot  ──────►│  [base64 image]  │
└──────────────┘                       └─────────────────┘
```

## What It Does

AI agents connect to your **running** Godot game and can:

| Capability | Examples |
|---|---|
| **Read game state** | Player position, enemy HP, item locations, distances |
| **Control UI** | Click buttons, type text, drag items, select options |
| **Simulate input** | Keyboard (WASD), mouse click/move/scroll, combos |
| **Run macro scripts** | Auto-battle, walk paths, repeat actions, timed sequences |
| **Monitor metrics** | HP over time, score curves, frame rate |
| **Capture visuals** | Screenshots for layout verification |
| **Read/write logs** | Three-tier logging (AI log, file log, debug log) |
| **Run automated tests** | Balance testing, regression, QA scenarios |

**34 built-in tools** covering state query, UI control, input simulation, macro execution, metrics, testing, and logging.

## Quick Start (3 steps)

**Step 1** — Copy 14 C# files into your project:

```bash
cp src/*.cs <your_project>/addons/game_mcp/
```

**Step 2** — Register as Autoload in Godot:

Project Settings → Autoload → Add `GameMcpServer.cs`

**Step 3** — Tag your game objects:

```csharp
public override void _Ready()
{
    AddToGroup("player");   // one line, that's it
}
```

Run the game. MCP server starts on `http://localhost:9876`. Done.

## Zero-Intrusion Design

- No base classes to inherit, no architecture changes, no dependencies
- Game code runs exactly as before — MCP is a passive observer
- Minimal changes: tag objects with Groups + name UI controls descriptively
- Optional: register metrics, expose custom data via `SetMeta("mcp_data", ...)`

## Connect Claude Code

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

## Tools Overview

### State & Structure
`get_game_state` `get_ui_layout` `get_scene_tree` `get_node_properties` `get_game_info`

### UI Control
`click_element` `type_text` `select_option` `get_focused_element` `drag` `double_click` `hover`

### Input Control
`press_key` `click_mouse` `move_mouse` `scroll_mouse` `simulate_input`

### Macro System (scripted input sequences)
`execute_macro` `get_macro_status` `cancel_macro` `list_macros`

Supports: `hold_key`, `tap_key`, `repeat_key`, `combo_keys`, `move_distance`, `move_to` (8dir/4dir/free), `click`, `drag`, `double_click`, `type_text`, `wait`

### Visual & Metrics
`screenshot` `register_metric` `get_metrics`

### Test Runner
`start_test` `get_test_results`

### Log System (three-tier)
`get_logs` `get_debug_logs` `log` `get_file_logs` `get_file_log_summary` `clear_logs`

### Node Manipulation
`set_node_property` `call_node_method`

## Documentation

| Document | Content |
|----------|---------|
| [Deploy Guide](docs/deploy.md) | Step-by-step installation and configuration |
| [Integration Guide](docs/integration.md) | Game code conventions (Groups, UI names, logging) |
| [Testing Guide](docs/testing.md) | Three-phase playtest process (Prep → Execute → Report) |
| [Debugging Guide](docs/debugging.md) | Common pitfalls, root causes, and fixes |
| [Requirements](docs/requirements.md) | Dependencies and recommended tools |

## Project Structure

```
├── README.md          ← You are here
├── src/               ← C# source files (copy these to your Godot project)
│   ├── GameMcpServer.cs              Core: lifecycle, public API
│   ├── GameMcpServer.Http.cs         HTTP server, JSON-RPC protocol
│   ├── GameMcpServer.Helpers.cs      Shared utilities
│   ├── GameMcpServer.Result.cs       JSON response builders
│   ├── GameMcpServer.Tools.cs        Tool registration dispatcher
│   ├── GameMcpServer.Tools.Scene.cs  Scene tree & node tools
│   ├── GameMcpServer.Tools.Query.cs  Game state & UI query tools
│   ├── GameMcpServer.Tools.UI.cs     UI interaction tools
│   ├── GameMcpServer.Tools.Input.cs  Input simulation tools
│   ├── GameMcpServer.Tools.Diagnostics.cs  Metrics, testing, logging
│   ├── GameMcpServer.MacroTypes.cs   Macro data types
│   ├── GameMcpServer.Macro.cs        Macro execution engine
│   ├── GameMcpServer.MacroTools.cs   Macro MCP tools
│   └── RingBuffer.cs                 Ring buffer utility
├── docs/              ← Documentation
│   ├── deploy.md
│   ├── integration.md
│   ├── testing.md
│   ├── debugging.md
│   └── requirements.md
└── skill.md           ← Claude Code skill definition
```

## Requirements

- Godot 4.6+ with .NET/C# support
- Recommended: [godot-mcp](https://github.com/Coding-Solo/godot-mcp) (editor-side MCP for creating scenes, running project)

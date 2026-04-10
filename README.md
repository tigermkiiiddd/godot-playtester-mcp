# Godot Playtester MCP

Embed an MCP (Model Context Protocol) server into a running Godot 4 (.NET) game. AI agents can inspect game state, simulate input, execute scripted macros, capture screenshots, monitor metrics, and run automated tests — all while the game is running.

## Zero-Intrusion Design

Copy 6 files into `addons/game_mcp/`, register as Autoload, done. No architecture changes, no new dependencies, no base classes to inherit. Game code runs exactly as before — MCP is just a passive observer.

**Minimal game code changes:**
- Tag game objects with Groups: `AddToGroup("player")` — one line per object
- Name UI controls descriptively — you should do this anyway
- Optionally register metrics or expose custom data via `SetMeta("mcp_data", ...)`

## How It Works

1. Copy all `GameMcpServer*.cs` files (6 partial classes) into your project
2. Register `GameMcpServer` as Autoload in Project Settings
3. Tag game objects with Godot Groups (`player`, `enemies`, `items`, etc.)
4. Run the game — MCP server starts on `http://localhost:9876`
5. AI agents connect via MCP adapter and control the game

## 34 Built-in Tools

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

## Files

| File | Purpose |
|------|---------|
| `GameMcpServer.cs` | Core: Node lifecycle, public API, JSON options |
| `GameMcpServer.Http.cs` | HTTP server, MCP JSON-RPC protocol |
| `GameMcpServer.Tools.cs` | 34 built-in tool implementations |
| `GameMcpServer.MacroTypes.cs` | Macro step data types |
| `GameMcpServer.Macro.cs` | Macro execution engine (runs in _Process) |
| `GameMcpServer.MacroTools.cs` | Macro MCP tools (execute/cancel/list) |
| `SKILL.md` | Full documentation with conventions and pitfalls |
| `deploy.md` | Step-by-step deployment guide |

## Requirements

- Godot 4.6+ with .NET support
- Game uses C# scripts

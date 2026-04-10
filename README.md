# Godot Playtester MCP

Embed an MCP (Model Context Protocol) server into a running Godot 4 (.NET) game. AI agents can inspect game state, simulate input, execute scripted macros, capture screenshots, monitor metrics, and run automated tests — all while the game is running.

## How It Works

1. Add the `GameMcpServer` partial class files to your Godot project
2. Register as an Autoload in Project Settings
3. Tag game objects with Godot Groups (`player`, `enemies`, `items`, etc.)
4. Run the game — MCP server starts on `http://localhost:9876`
5. AI agents (Claude Code, etc.) connect and control the game

## 21 Built-in Tools

### State & Structure
`get_game_state` `get_ui_layout` `get_scene_tree` `get_node_properties` `get_game_info`

### Input Control
`press_key` `click_mouse` `move_mouse` `scroll_mouse` `simulate_input`

### Macro System (scripted input sequences)
`execute_macro` `get_macro_status` `cancel_macro` `list_macros`

Supports: `hold_key`, `tap_key`, `repeat_key`, `combo_keys`, `move_distance`, `click`, `wait`

### Visual & Metrics
`screenshot` `register_metric` `get_metrics`

### Test Runner
`start_test` `get_test_results`

### Node Manipulation
`set_node_property` `call_node_method`

## Quick Example

```bash
# Walk forward for 1.5 seconds, then attack
curl -s -X POST http://localhost:9876 -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"execute_macro","arguments":{"name":"walk_and_attack","steps":[{"action":"hold_key","keys":"W","duration":1.5},{"action":"tap_key","keys":"F"}]}}}'
```

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
| `GameMcpServer.cs` | Core: Node lifecycle, public API |
| `GameMcpServer.Http.cs` | HTTP server, MCP protocol |
| `GameMcpServer.Tools.cs` | Built-in tool implementations |
| `GameMcpServer.MacroTypes.cs` | Macro data types |
| `GameMcpServer.Macro.cs` | Macro execution engine |
| `GameMcpServer.MacroTools.cs` | Macro MCP tools |
| `SKILL.md` | Full documentation |
| `deploy.md` | Deployment guide |

## Requirements

- Godot 4 with .NET support
- Game uses C# scripts

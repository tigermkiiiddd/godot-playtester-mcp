# Deploy GameMcpServer into a Godot Project

Step-by-step guide for embedding the Playtester MCP server into any Godot 4 (.NET) project.

## Prerequisites

- Godot 4 with .NET support (Mono/.NET build)
- The game project must use C# scripts

## Step 1: Copy the Plugin

The server is split into partial class files. Copy all of them into your project:

```bash
cp ${CLAUDE_SKILL_DIR}/GameMcpServer*.cs ${CLAUDE_SKILL_DIR}/RingBuffer.cs <project>/addons/game_mcp/
```

Files:
- `GameMcpServer.cs` — Core: Node lifecycle, _Process, public API, thread queue
- `GameMcpServer.Http.cs` — HTTP server, MCP protocol, JSON-RPC
- `GameMcpServer.Helpers.cs` — Shared helper utilities
- `GameMcpServer.Result.cs` — JSON response builders
- `GameMcpServer.Tools.cs` — Tool registration dispatcher
- `GameMcpServer.Tools.Scene.cs` — Scene tree & node manipulation tools
- `GameMcpServer.Tools.Query.cs` — Game state & UI query tools
- `GameMcpServer.Tools.UI.cs` — UI interaction tools
- `GameMcpServer.Tools.Input.cs` — Input simulation tools
- `GameMcpServer.Tools.Diagnostics.cs` — Metrics, testing, logging tools
- `GameMcpServer.MacroTypes.cs` — Macro data types (MacroStepType, MacroStep, MacroRun)
- `GameMcpServer.Macro.cs` — Macro execution engine
- `GameMcpServer.MacroTools.cs` — Macro MCP tools (execute_macro, get_macro_status, cancel_macro, list_macros)
- `RingBuffer.cs` — Generic ring buffer utility

## Step 2: Configure as Autoload

In Godot Editor:
1. **Project → Project Settings → Autoload**
2. Click the folder icon, select `GameMcpServer.cs`
3. Name it `GameMcpServer`
4. Click **Add**

## Step 3: Configure (Optional)

Select the GameMcpServer autoload in the scene tree. In the Inspector:

| Property | Default | Description |
|----------|---------|-------------|
| Port | 9876 | HTTP server port |
| Enabled | true | Set false to disable |
| ServerName | godot-playtester | Server name sent to clients |
| PlayerPath | (empty) | Explicit player node path (auto-detects via "player" group if empty) |

## Step 4: Integrate Game Protocol

### Tag game objects with Groups

In your game scripts, add objects to standard groups:

```csharp
public partial class Player : CharacterBody2D
{
    public override void _Ready()
    {
        AddToGroup("player");
    }
}

public partial class Enemy : CharacterBody2D
{
    public override void _Ready()
    {
        AddToGroup("enemies");
    }
}

public partial class Pickup : Area2D
{
    public override void _Ready()
    {
        AddToGroup("items");
    }
}
```

### Name UI nodes descriptively

```csharp
var hpBar = new ProgressBar { Name = "HPBar" };
var attackBtn = new Button { Name = "AttackBtn", Text = "Attack" };
```

### Register metrics for monitoring

```csharp
public partial class Player : CharacterBody2D
{
    public int Health { get; set; } = 100;
    public int Score { get; set; }

    public override void _Ready()
    {
        AddToGroup("player");
        CallDeferred(RegisterMetrics);
    }

    private void RegisterMetrics()
    {
        if (GameMcpServer.Instance == null) return;

        GameMcpServer.Instance.RegisterMetric("player_hp", () => Health);
        GameMcpServer.Instance.RegisterMetric("score", () => Score);
        GameMcpServer.Instance.RegisterMetric("position_x", () => GlobalPosition.X);
    }
}
```

## Step 5: Run the Game

Run the game normally. The MCP server starts automatically:

```
[GameMcp] Playtester MCP server listening on http://localhost:9876
[GameMcp] Registered tool: get_scene_tree
[GameMcp] Registered tool: get_game_state
[GameMcp] Registered tool: get_ui_layout
...
[GameMcp] Registered metric: player_hp
[GameMcp] Registered metric: score
```

## Step 6: Verify

```bash
# List all tools
curl -X POST http://localhost:9876 -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

# Get game state (enemies, items, positions)
curl -X POST http://localhost:9876 -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_game_state"}}'

# Get UI layout (buttons, bars, labels)
curl -X POST http://localhost:9876 -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_ui_layout"}}'

# Get current metric values
curl -X POST http://localhost:9876 -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_metrics","arguments":{"format":"latest"}}}'
```

## Step 7: Connect Claude Code

Create `.mcp.json` in the Godot project root:

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

## Register Custom Tools

Games can also register custom MCP tools for game-specific actions:

```csharp
public partial class GameManager : Node
{
    public override void _Ready()
    {
        CallDeferred(RegisterCustomTools);
    }

    private void RegisterCustomTools()
    {
        if (GameMcpServer.Instance == null) return;

        GameMcpServer.Instance.RegisterTool("spawn_enemy",
            "Spawn an enemy of a given type at a position",
            new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["enemy_type"] = new JsonObject { ["type"] = "string" },
                    ["x"] = new JsonObject { ["type"] = "number" },
                    ["y"] = new JsonObject { ["type"] = "number" }
                },
                ["required"] = new JsonArray { "enemy_type", "x", "y" }
            },
            args => {
                var type = args["enemy_type"].GetString();
                var x = args["x"].GetSingle();
                var y = args["y"].GetSingle();
                SpawnEnemy(type, new Vector2(x, y));
                return $"Spawned {type} at ({x}, {y})";
            }
        );

        GameMcpServer.Instance.RegisterTool("set_difficulty",
            "Change game difficulty level",
            new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["level"] = new JsonObject { ["type"] = "string", ["description"] = "easy/normal/hard" }
                },
                ["required"] = new JsonArray { "level" }
            },
            args => {
                var level = args["level"].GetString();
                SetDifficulty(level);
                return $"Difficulty set to {level}";
            }
        );
    }
}
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Port already in use | Change Port in Inspector |
| "Node not found" | Paths must start with `/root/` |
| Connection refused | Game must be running first |
| get_game_state returns empty | Tag objects with Godot Groups (`AddToGroup("enemies")`) |
| No screen_pos in results | Scene needs a Camera2D node |
| Metrics show "not_registered_in_code" | Call `RegisterMetric()` from game code, not just via MCP |
| Blank response | Use .NET build of Godot (Mono/.NET version) |

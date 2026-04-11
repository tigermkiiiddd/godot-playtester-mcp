# Godot Playtester MCP — Requirements

## Required

| Dependency | Version | Notes |
|---|---|---|
| Godot Engine | 4.6+ | .NET/C# support |
| Node.js | 18+ | For mcp-http-bridge.mjs |

## Recommended

| Dependency | Version | Notes |
|---|---|---|
| [godot-mcp](https://github.com/Coding-Solo/godot-mcp) | latest | Editor-side MCP: create scenes, add nodes, run project, read debug output. Pairs with this runtime MCP for full edit→build→run→test workflow. Install: `npx @coding-solo/godot-mcp` |

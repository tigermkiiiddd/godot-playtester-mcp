# Minimal Quick-Start Example

A complete working test: WASD player with trail, MCP reads position and controls movement.

## 1. Scene Structure (created via Godot MCP `create_scene` + `add_node`)

```
TrailTest (Node2D)
├── Player (CharacterBody2D) [group: "player"]
│   ├── CollisionShape2D
│   │   └── Shape: RectangleShape2D (4x4)
│   └── TrailLine (Line2D)
├── Background (ColorRect)
└── Camera2D
```

## 2. Player Script (`scripts/Player.cs`)

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

## 3. Verify via curl

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

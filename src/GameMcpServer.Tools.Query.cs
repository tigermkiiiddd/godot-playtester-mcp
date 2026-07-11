#nullable disable
using Godot;
namespace GodotPlaytester;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;


public partial class GameMcpServer
{
    // ── get_game_state ───────────────────────────────────────────────────

    private string GetGameState(Dictionary<string, JsonElement> args)
    {
        var tree = GetTree();
        if (tree == null) return "{\"error\":\"No scene tree\"}";

        string[] groups = null;
        if (args.ContainsKey("groups"))
        {
            var g = args["groups"];
            if (g.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(g.GetString()))
                groups = g.GetString().Split(',');
            else if (g.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in g.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                        list.Add(item.GetString());
                if (list.Count > 0) groups = list.ToArray();
            }
        }

        float? radius = args.ContainsKey("radius") ? args["radius"].GetSingle() : null;
        float? nearX = args.ContainsKey("near_x") ? args["near_x"].GetSingle() : null;
        float? nearY = args.ContainsKey("near_y") ? args["near_y"].GetSingle() : null;
        float? nearR = args.ContainsKey("near_radius") ? args["near_radius"].GetSingle() : null;
        float? areaX = args.ContainsKey("area_x") ? args["area_x"].GetSingle() : null;
        float? areaY = args.ContainsKey("area_y") ? args["area_y"].GetSingle() : null;
        float? areaW = args.ContainsKey("area_w") ? args["area_w"].GetSingle() : null;
        float? areaH = args.ContainsKey("area_h") ? args["area_h"].GetSingle() : null;
        int limit = args.ContainsKey("limit") ? args["limit"].GetInt32() : 20;
        int offset = args.ContainsKey("offset") ? args["offset"].GetInt32() : 0;

        var player = FindPlayer();
        var camera = GetViewport().GetCamera2D();
        var result = new JsonObject();

        Node2D p2d = player as Node2D;
        if (p2d != null) result["player"] = BuildNodeInfo(p2d, camera, p2d);

        var tracked = groups ?? new[] { "enemies", "npcs", "items", "interactables", "projectiles", "triggers" };
        foreach (var group in tracked)
        {
            var g = group.Trim();
            var nodes = tree.GetNodesInGroup(g);
            var filtered = new List<Node2D>();

            foreach (Node n in nodes)
            {
                if (n == player || n is not Node2D n2d) continue;
                var pos = n2d.GlobalPosition;
                if (radius.HasValue && p2d != null && pos.DistanceTo(p2d.GlobalPosition) > radius.Value) continue;
                if (nearX.HasValue && nearY.HasValue && nearR.HasValue && pos.DistanceTo(new Vector2(nearX.Value, nearY.Value)) > nearR.Value) continue;
                if (areaX.HasValue && areaY.HasValue && areaW.HasValue && areaH.HasValue)
                {
                    if (pos.X < areaX.Value || pos.X > areaX.Value + areaW.Value) continue;
                    if (pos.Y < areaY.Value || pos.Y > areaY.Value + areaH.Value) continue;
                }
                filtered.Add(n2d);
            }

            var paged = filtered.Skip(offset).Take(limit).ToList();
            var items = new JsonArray();
            foreach (var n in paged) items.Add(BuildNodeInfo(n, camera, p2d));
            result[g] = new JsonObject { ["total"] = filtered.Count, ["returned"] = paged.Count, ["items"] = items };
        }
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    private static JsonNode BuildNodeInfo(Node2D node, Camera2D camera, Node2D player)
    {
        var o = new JsonObject
        {
            ["name"] = node.Name.ToString(),
            ["type"] = node.GetType().Name,
            ["path"] = node.GetPath().ToString(),
            ["world_pos"] = new JsonArray { Math.Round(node.GlobalPosition.X, 1), Math.Round(node.GlobalPosition.Y, 1) }
        };
        if (camera != null)
        {
            var sp = camera.GetScreenTransform() * node.GlobalPosition;
            o["screen_pos"] = new JsonArray { Math.Round(sp.X, 0), Math.Round(sp.Y, 0) };
        }
        if (player != null && node != player)
            o["dist"] = Math.Round(node.GlobalPosition.DistanceTo(player.GlobalPosition), 1);
        return o;
    }
}

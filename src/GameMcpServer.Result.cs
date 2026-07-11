#nullable disable
using System.Collections.Generic;
namespace GodotPlaytester;
using System.Text.Json;


/// <summary>
/// Type-safe MCP tool response builder. Eliminates hand-written JSON string interpolation
/// which is vulnerable to injection from node names containing quotes/special chars.
/// </summary>
public static class McpResult
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>Build a success response: {"ok":true,"name":"...",x,y}</summary>
    public static string Success(string name, double? x = null, double? y = null)
    {
        var dict = new Dictionary<string, object> { ["ok"] = true, ["name"] = name };
        if (x.HasValue) dict["x"] = (int)x.Value;
        if (y.HasValue) dict["y"] = (int)y.Value;
        return JsonSerializer.Serialize(dict, Opts);
    }

    /// <summary>Build an error response: {"error":"..."}</summary>
    public static string Error(string message)
    {
        return JsonSerializer.Serialize(new { error = message }, Opts);
    }

    /// <summary>Serialize any object as JSON response</summary>
    public static string FromObject(object obj)
    {
        return JsonSerializer.Serialize(obj, Opts);
    }
}

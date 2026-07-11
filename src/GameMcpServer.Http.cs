using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;


public partial class GameMcpServer
{
    // ═══════════════════════════════════════════════════════════════════════
    //  HTTP SERVER
    // ═══════════════════════════════════════════════════════════════════════

    private void StartServer()
    {
        _running = true;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _serverThread = new Thread(ServerLoop) { IsBackground = true };
        _serverThread.Start();
    }

    private void StopServer()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        _listener = null;
        foreach (var writer in _fileWriters.Values)
            try { writer.Dispose(); } catch { }
        _fileWriters.Clear();
    }

    private void ServerLoop()
    {
        while (_running)
        {
            try
            {
                var context = _listener.GetContext();
                HandleRequestAsync(context).GetAwaiter().GetResult();
            }
            catch (HttpListenerException) when (!_running) { }
            catch (Exception e) { if (_running) GD.PrintErr($"[GameMcp] {e.Message}"); }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;

        if (context.Request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            var body = Encoding.UTF8.GetBytes("{\"error\":\"Method not allowed\"}");
            response.ContentType = "application/json"; response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body); response.Close(); return;
        }

        string requestBody;
        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
            requestBody = await reader.ReadToEndAsync();
        Interlocked.Increment(ref _requestCount);

        string responseBody;
        try { responseBody = ProcessMcpMessage(requestBody); }
        catch (Exception e) { responseBody = $"{{\"error\":\"{e.Message.Replace("\"", "'")}\"}}"; }

        if (responseBody == null)
        {
            // Notification — no result expected. 202 with empty body per Streamable HTTP transport.
            response.StatusCode = 202;
            response.Close();
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(responseBody);
        response.StatusCode = 200; response.ContentType = "application/json"; response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes); response.Close();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MCP PROTOCOL
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] SupportedProtocolVersions = { "2024-11-05", "2025-03-26", "2025-06-18" };

    private string ProcessMcpMessage(string json)
    {
        var node = JsonNode.Parse(json).AsObject();
        var method = node["method"]?.GetValue<string>() ?? "";
        var id = node["id"];
        var @params = node["params"]?.AsObject();

        if (method.StartsWith("notifications/")) return null;

        string result = method switch
        {
            "initialize" => HandleInit(@params),
            "tools/list" => HandleToolsList(),
            "tools/call" => HandleToolsCall(@params),
            "ping" => "{}",
            _ => RpcError(id, -32601, $"Unknown method: {method}")
        };

        return RpcResult(id, result);
    }

    private string HandleInit(JsonObject p)
    {
        var name = p?["clientInfo"]?["name"]?.GetValue<string>() ?? "unknown";
        GD.Print($"[GameMcp] Client connected: {name}");
        var requested = p?["protocolVersion"]?.GetValue<string>();
        var negotiated = System.Linq.Enumerable.Contains(SupportedProtocolVersions, requested) ? requested : "2025-03-26";
        return JsonSerializer.Serialize(new JsonObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "2.0.0" }
        }, JsonOpts);
    }

    private string HandleToolsList()
    {
        var arr = new JsonArray();
        foreach (var t in _tools)
        {
            // MCP spec requires lowercase: name, description, inputSchema
            arr.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = JsonSerializer.SerializeToNode(t.InputSchema, JsonOpts),
            });
        }
        return JsonSerializer.Serialize(new JsonObject { ["tools"] = arr }, JsonOpts);
    }

    private string HandleToolsCall(JsonObject p)
    {
        var toolName = p?["name"]?.GetValue<string>() ?? "";
        var args = new Dictionary<string, JsonElement>();
        if (p?["arguments"] is JsonObject ao)
            foreach (var kv in ao) args[kv.Key] = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(kv.Value.ToJsonString());

        try
        {
            if (!_handlers.TryGetValue(toolName, out var handler))
                throw new Exception($"Unknown tool: {toolName}");

            var result = ExecuteOnMainThread(handler, args);

            if (result is McpImageResult img)
            {
                return JsonSerializer.Serialize(new JsonObject
                {
                    ["content"] = new JsonArray { new JsonObject { ["type"] = "image", ["data"] = img.Base64, ["mimeType"] = img.MimeType } }
                }, JsonOpts);
            }

            return JsonSerializer.Serialize(new JsonObject
            {
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result?.ToString() ?? "null" } }
            }, JsonOpts);
        }
        catch (Exception e)
        {
            return JsonSerializer.Serialize(new JsonObject
            {
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = $"Tool error: {e.Message}" } },
                ["isError"] = true
            }, JsonOpts);
        }
    }

    // ── Thread Safety ───────────────────────────────────────────────────

    private const int MainThreadTimeoutMs = 10000;

    private T ExecuteOnMainThread<T>(Func<Dictionary<string, JsonElement>, T> func, Dictionary<string, JsonElement> args)
    {
        T result = default; Exception ex = null;
        var done = new ManualResetEventSlim(false);
        _mainQueue.Enqueue(() => { try { result = func(args); } catch (Exception e) { ex = e; } finally { done.Set(); } });
        if (!done.Wait(MainThreadTimeoutMs))
            throw new TimeoutException($"Main-thread dispatch timed out ({MainThreadTimeoutMs / 1000}s) — game frozen or paused?");
        if (ex != null) throw ex;
        return result;
    }

    // ── JSON-RPC ────────────────────────────────────────────────────────

    private static string RpcResult(JsonNode id, string result) =>
        JsonSerializer.Serialize(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = JsonNode.Parse(result) }, JsonOpts);

    private static string RpcError(JsonNode id, int code, string msg) =>
        JsonSerializer.Serialize(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = msg } }, JsonOpts);
}

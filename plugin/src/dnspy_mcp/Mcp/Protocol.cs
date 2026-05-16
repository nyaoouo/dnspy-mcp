using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using DnspyMcp.Util;

namespace DnspyMcp.Mcp;

public sealed class Protocol
{
    private readonly ToolRegistry _registry;
    private readonly IDnSpyServices _services;

    public Protocol(ToolRegistry registry, IDnSpyServices services)
    {
        _registry = registry;
        _services = services;
    }

    public JsonObject Handle(JsonObject envelope)
    {
        var method = envelope["method"]?.GetValue<string>() ?? "";
        var id = envelope["id"]?.DeepClone();

        if (method.StartsWith("notifications/"))
            return new JsonObject(); // 202-style empty payload

        return method switch
        {
            "initialize" => Result(id, new JsonObject
            {
                ["protocolVersion"] = "2025-03-26",
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "dnspy-mcp-plugin",
                    ["version"] = "0.1.0",
                },
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject { ["listChanged"] = false },
                },
            }),
            "ping" => Result(id, new JsonObject()),
            "tools/list" => Result(id, BuildToolsList()),
            "tools/call" => HandleToolsCall(id, envelope),
            _ => Error(id, JsonRpcErrors.MethodNotFound, $"method not found: {method}"),
        };
    }

    private JsonObject BuildToolsList()
    {
        var tools = new JsonArray();
        foreach (var tool in _registry.Tools)
        {
            var schemaNode = JsonNode.Parse(JsonSerializer.Serialize(tool.GetInputSchema()))!.AsObject();
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = schemaNode,
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    private JsonObject HandleToolsCall(JsonNode? id, JsonObject envelope)
    {
        var paramsObj = envelope["params"]?.AsObject() ?? new JsonObject();
        var name = paramsObj["name"]?.GetValue<string>() ?? "";
        if (!_registry.TryGet(name, out var tool))
            return Error(id, JsonRpcErrors.MethodNotFound, $"unknown tool: {name}");

        var argsNode = paramsObj["arguments"]?.AsObject();
        var arguments = new Dictionary<string, object?>();
        if (argsNode is not null)
        {
            foreach (var kvp in argsNode)
            {
                arguments[kvp.Key] = JsonNodeToObject(kvp.Value);
            }
        }

        try
        {
            var result = tool.Invoke(arguments, _services);
            return Result(id, ResultToJson(result));
        }
        catch (System.Exception ex)
        {
            return Result(id, ResultToJson(CallToolResult.Text($"{tool.Name} threw: {ex.Message}", isError: true)));
        }
    }

    private static object? JsonNodeToObject(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<bool>(out var b)) return b;
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<double>(out var d)) return d;
            return v.ToJsonString();
        }
        if (node is JsonArray arr)
        {
            var list = new System.Collections.Generic.List<object?>(arr.Count);
            foreach (var item in arr) list.Add(JsonNodeToObject(item));
            return list;
        }
        if (node is JsonObject obj)
        {
            var dict = new System.Collections.Generic.Dictionary<string, object?>();
            foreach (var kvp in obj) dict[kvp.Key] = JsonNodeToObject(kvp.Value);
            return dict;
        }
        return null;
    }

    private static JsonObject ResultToJson(CallToolResult result)
    {
        var contentArr = new JsonArray();
        foreach (var c in result.Content)
            contentArr.Add(new JsonObject { ["type"] = c.Type, ["text"] = c.Text });
        var obj = new JsonObject { ["isError"] = result.IsError, ["content"] = contentArr };
        if (result.StructuredContent is not null)
            obj["structuredContent"] = JsonNode.Parse(JsonSerializer.Serialize(result.StructuredContent));
        return obj;
    }

    private static JsonObject Result(JsonNode? id, JsonObject payload) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = payload,
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}

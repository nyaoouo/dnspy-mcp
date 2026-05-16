using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class GetTypeInfoTool : IMcpTool
{
    public string Name => "get_type_info";
    public string Description => "Get detailed metadata about a specific type (by full name or token).";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Assembly name, full name, or path." },
            ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Type full name (e.g. 'System.String') or hex/decimal token." },
        },
        ["required"] = new[] { "assembly", "type" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm) || rawAsm is not string asm || string.IsNullOrEmpty(asm))
            return CallToolResult.Text("Missing 'assembly'.", isError: true);
        if (!args.TryGetValue("type", out var rawType) || rawType is not string typeName || string.IsNullOrEmpty(typeName))
            return CallToolResult.Text("Missing 'type'.", isError: true);

        try
        {
            var r = services.GetTypeInfo(asm, typeName);
            var payload = new JsonObject
            {
                ["namespace"] = r.Namespace,
                ["name"] = r.Name,
                ["full_name"] = r.FullName,
                ["token"] = r.Token,
                ["kind"] = r.Kind,
                ["base_type"] = r.BaseType,
                ["interfaces"] = new JsonArray(r.Interfaces.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()),
                ["field_count"] = r.FieldCount,
                ["method_count"] = r.MethodCount,
                ["property_count"] = r.PropertyCount,
                ["event_count"] = r.EventCount,
            };
            return CallToolResult.Text(payload.ToJsonString());
        }
        catch (Exception ex)
        {
            return CallToolResult.Text(ex.Message, isError: true);
        }
    }
}

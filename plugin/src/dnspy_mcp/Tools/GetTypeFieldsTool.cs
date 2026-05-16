using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class GetTypeFieldsTool : IMcpTool
{
    public string Name => "get_type_fields";
    public string Description => "Get fields of a type, optionally filtered by wildcard pattern (*), with pagination.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Assembly name, full name, or path." },
            ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Type full name or token." },
            ["pattern"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Wildcard pattern (default '*' = all)." },
            ["cursor"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Pagination cursor." },
            ["page_size"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Results per page (default 50)." },
        },
        ["required"] = new[] { "assembly", "type" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm) || rawAsm is not string asm || string.IsNullOrEmpty(asm))
            return CallToolResult.Text("Missing 'assembly'.", isError: true);
        if (!args.TryGetValue("type", out var rawType) || rawType is not string typeName || string.IsNullOrEmpty(typeName))
            return CallToolResult.Text("Missing 'type'.", isError: true);

        var pattern = args.TryGetValue("pattern", out var rPat) && rPat is string patStr && !string.IsNullOrEmpty(patStr)
            ? patStr : "*";
        var offset = ParseCursor(args);
        var limit = ParsePageSize(args);

        try
        {
            var r = services.GetTypeFields(asm, typeName, pattern, offset, limit);
            var payload = new JsonObject
            {
                ["fields"] = new JsonArray(r.Fields.Select(f => (JsonNode)new JsonObject
                {
                    ["name"] = f.Name,
                    ["full_name"] = f.FullName,
                    ["token"] = f.Token,
                    ["field_type"] = f.FieldType,
                    ["is_static"] = f.IsStatic,
                }).ToArray()),
                ["next_cursor"] = r.NextOffset < 0 ? null : (JsonNode)r.NextOffset.ToString(),
                ["total"] = r.Total,
            };
            return CallToolResult.Text(payload.ToJsonString());
        }
        catch (Exception ex)
        {
            return CallToolResult.Text(ex.Message, isError: true);
        }
    }

    private static int ParseCursor(IDictionary<string, object?> args)
    {
        if (args.TryGetValue("cursor", out var raw) && raw is string s && !string.IsNullOrEmpty(s))
            return int.TryParse(s, out var n) ? n : 0;
        return 0;
    }

    private static int ParsePageSize(IDictionary<string, object?> args, int def = 50)
    {
        if (args.TryGetValue("page_size", out var raw))
        {
            return raw switch { int i => i, long l => (int)l, string s => int.TryParse(s, out var n) ? n : def, _ => def };
        }
        return def;
    }
}

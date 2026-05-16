using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class ListTypesTool : IMcpTool
{
    public string Name => "list_types";
    public string Description => "List types in a loaded assembly with optional namespace filter and pagination.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Assembly name, full name, or path." },
            ["namespace"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Optional namespace filter (exact match)." },
            ["cursor"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Pagination cursor (opaque string returned by previous call)." },
            ["page_size"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Number of results per page (default 50)." },
        },
        ["required"] = new[] { "assembly" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm) || rawAsm is not string asm || string.IsNullOrEmpty(asm))
            return CallToolResult.Text("Missing 'assembly'.", isError: true);

        string? ns = args.TryGetValue("namespace", out var rNs) && rNs is string nsStr && !string.IsNullOrEmpty(nsStr) ? nsStr : null;
        var offset = ParseCursor(args);
        var limit = ParsePageSize(args);

        try
        {
            var r = services.ListTypes(asm, ns, offset, limit);
            var payload = new JsonObject
            {
                ["types"] = new JsonArray(r.Types.Select(t => (JsonNode)new JsonObject
                {
                    ["namespace"] = t.Namespace,
                    ["name"] = t.Name,
                    ["full_name"] = t.FullName,
                    ["token"] = t.Token,
                    ["kind"] = t.Kind,
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

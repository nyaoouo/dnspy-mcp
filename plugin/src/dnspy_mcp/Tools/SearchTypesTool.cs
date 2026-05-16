using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class SearchTypesTool : IMcpTool
{
    public string Name => "search_types";
    public string Description => "Search for types by wildcard pattern across all loaded assemblies (or a specific one).";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["pattern"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Wildcard pattern (use * for any characters)." },
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Optional assembly scope filter." },
            ["cursor"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Pagination cursor." },
            ["page_size"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Results per page (default 50)." },
        },
        ["required"] = new[] { "pattern" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("pattern", out var rawPat) || rawPat is not string pattern || string.IsNullOrEmpty(pattern))
            return CallToolResult.Text("Missing 'pattern'.", isError: true);

        string? scopeAsm = args.TryGetValue("assembly", out var rAsm) && rAsm is string asmStr && !string.IsNullOrEmpty(asmStr)
            ? asmStr : null;
        var offset = ParseCursor(args);
        var limit = ParsePageSize(args);

        try
        {
            var r = services.SearchTypes(pattern, scopeAsm, offset, limit);
            var payload = new JsonObject
            {
                ["results"] = new JsonArray(r.Results.Select(h => (JsonNode)new JsonObject
                {
                    ["assembly"] = h.Assembly,
                    ["namespace"] = h.Namespace,
                    ["name"] = h.Name,
                    ["full_name"] = h.FullName,
                    ["token"] = h.Token,
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

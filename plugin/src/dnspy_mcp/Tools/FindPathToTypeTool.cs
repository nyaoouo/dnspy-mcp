using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class FindPathToTypeTool : IMcpTool
{
    public string Name => "find_path_to_type";
    public string Description => "Find chains of properties/fields walking from from_type to to_type within the same module (BFS, up to max_depth hops, returns at most 10 paths).";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Assembly name, full name, or path." },
            ["from_type"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Fully qualified name (or token) of the starting type." },
            ["to_type"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Fully qualified name (or token) of the target type." },
            ["max_depth"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Maximum number of hops (default 5)." },
        },
        ["required"] = new[] { "assembly", "from_type", "to_type" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm) || rawAsm is not string asm || string.IsNullOrEmpty(asm))
            return CallToolResult.Text("Missing 'assembly'.", isError: true);
        if (!args.TryGetValue("from_type", out var rawFrom) || rawFrom is not string fromType || string.IsNullOrEmpty(fromType))
            return CallToolResult.Text("Missing 'from_type'.", isError: true);
        if (!args.TryGetValue("to_type", out var rawTo) || rawTo is not string toType || string.IsNullOrEmpty(toType))
            return CallToolResult.Text("Missing 'to_type'.", isError: true);

        int maxDepth = 5;
        if (args.TryGetValue("max_depth", out var rawDepth))
            maxDepth = rawDepth switch { int i => i, long l => (int)l, string s => int.TryParse(s, out var n) ? n : 5, _ => 5 };

        try
        {
            var r = services.FindPathToType(asm, fromType, toType, maxDepth);
            var payload = new JsonObject
            {
                ["paths"] = new JsonArray(r.Paths.Select(p => (JsonNode)new JsonObject
                {
                    ["steps"] = new JsonArray(p.Steps.Select(s => (JsonNode)new JsonObject
                    {
                        ["kind"] = s.Kind,
                        ["member_name"] = s.MemberName,
                        ["member_token"] = s.MemberToken,
                        ["declaring_type"] = s.DeclaringType,
                        ["target_type"] = s.TargetType,
                    }).ToArray()),
                }).ToArray()),
            };
            return CallToolResult.Text(payload.ToJsonString());
        }
        catch (Exception ex)
        {
            return CallToolResult.Text(ex.Message, isError: true);
        }
    }
}

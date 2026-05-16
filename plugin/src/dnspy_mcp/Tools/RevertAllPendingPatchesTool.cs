using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class RevertAllPendingPatchesTool : IMcpTool
{
    public string Name => "revert_all_pending_patches";
    public string Description =>
        "Revert every in-memory method patch made by patch_method_il. " +
        "Optional 'assembly' scopes the revert to one loaded assembly.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional assembly identifier; default = all assemblies.",
            },
        },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        string? scope = null;
        if (args.TryGetValue("assembly", out var raw) && raw is string s && !string.IsNullOrEmpty(s))
            scope = s;
        try
        {
            var r = services.RevertAllPendingPatches(scope);
            var payload = new JsonObject
            {
                ["reverted_count"] = r.Reverted.Count,
                ["reverted"] = new JsonArray(r.Reverted.Select(rm => (JsonNode)new JsonObject
                {
                    ["assembly"] = rm.Assembly,
                    ["method_token"] = rm.Token,
                    ["method"] = rm.MethodFullName,
                }).ToArray()),
            };
            return CallToolResult.Text(payload.ToJsonString());
        }
        catch (System.Exception ex)
        {
            return CallToolResult.Text(ex.Message, isError: true);
        }
    }
}

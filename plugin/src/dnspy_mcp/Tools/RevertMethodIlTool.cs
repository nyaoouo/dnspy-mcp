using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class RevertMethodIlTool : IMcpTool
{
    public string Name => "revert_method_il";
    public string Description => "Restore the IL body snapshot taken by patch_method_il, reverting all patches.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Assembly name, full name, or path." },
            ["method_token"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Metadata token of the MethodDef." },
        },
        ["required"] = new[] { "assembly", "method_token" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm) || rawAsm is not string asm || string.IsNullOrEmpty(asm))
            return CallToolResult.Text("Missing 'assembly'.", isError: true);
        if (!GetMethodIlTool.TryParseToken(args, "method_token", out var token))
            return CallToolResult.Text("Missing or invalid 'method_token'.", isError: true);

        try
        {
            var r = services.RevertMethodIl(asm, token);
            var payload = new JsonObject { ["reverted"] = r.Reverted };
            return CallToolResult.Text(payload.ToJsonString());
        }
        catch (Exception ex)
        {
            return CallToolResult.Text(ex.Message, isError: true);
        }
    }
}

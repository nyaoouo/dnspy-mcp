using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class GetMethodIlTool : IMcpTool
{
    public string Name => "get_method_il";
    public string Description => "Get the IL body of a method by its metadata token.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Assembly name, full name, or path." },
            ["method_token"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Metadata token (int or 0x-prefixed hex) of the MethodDef." },
        },
        ["required"] = new[] { "assembly", "method_token" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm) || rawAsm is not string asm || string.IsNullOrEmpty(asm))
            return CallToolResult.Text("Missing 'assembly'.", isError: true);
        if (!TryParseToken(args, "method_token", out var token))
            return CallToolResult.Text("Missing or invalid 'method_token'.", isError: true);

        try
        {
            var r = services.GetMethodIl(asm, token);
            var payload = new JsonObject
            {
                ["method"] = r.MethodFullName,
                ["max_stack"] = r.MaxStack,
                ["init_locals"] = r.InitLocals,
                ["locals"] = new JsonArray(r.Locals.Select(l => (JsonNode)new JsonObject
                {
                    ["index"] = l.Index,
                    ["type"] = l.TypeName,
                }).ToArray()),
                ["instructions"] = new JsonArray(r.Instructions.Select(i => (JsonNode)new JsonObject
                {
                    ["offset"] = i.Offset,
                    ["opcode"] = i.OpCode,
                    ["operand"] = i.Operand is not null ? (JsonNode)i.Operand : null,
                }).ToArray()),
                ["exception_handlers"] = new JsonArray(r.ExceptionHandlers.Select(eh => (JsonNode)new JsonObject
                {
                    ["try_start"] = eh.TryStart,
                    ["try_end"] = eh.TryEnd,
                    ["handler_start"] = eh.HandlerStart,
                    ["handler_end"] = eh.HandlerEnd,
                    ["kind"] = eh.Kind,
                    ["catch_type"] = eh.CatchType is not null ? (JsonNode)eh.CatchType : null,
                }).ToArray()),
            };
            return CallToolResult.Text(payload.ToJsonString());
        }
        catch (Exception ex)
        {
            return CallToolResult.Text(ex.Message, isError: true);
        }
    }

    internal static bool TryParseToken(IDictionary<string, object?> args, string key, out uint token)
    {
        token = 0;
        if (!args.TryGetValue(key, out var raw)) return false;
        switch (raw)
        {
            case int i: token = (uint)i; return true;
            case long l: token = (uint)l; return true;
            case uint u: token = u; return true;
            case string s when s.StartsWith("0x", StringComparison.OrdinalIgnoreCase):
                return uint.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out token);
            case string s:
                return uint.TryParse(s, out token);
            default: return false;
        }
    }
}

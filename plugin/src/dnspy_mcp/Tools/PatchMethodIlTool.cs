using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class PatchMethodIlTool : IMcpTool
{
    public string Name => "patch_method_il";
    public string Description => "Apply a list of edits to the in-memory IL body of a method. Snapshots the original on first call so revert_method_il can restore it.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Assembly name, full name, or path." },
            ["method_token"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Metadata token of the MethodDef." },
            ["edits"] = new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["description"] = "Ordered list of edits to apply.",
                ["items"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["op"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "insert|replace|remove|set_locals|set_init_locals" },
                        ["at_offset"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Instruction offset (required for insert/replace/remove)." },
                        ["instructions"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["description"] = "Instructions to insert/replace with.",
                            ["items"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object?>
                                {
                                    ["opcode"] = new Dictionary<string, object?> { ["type"] = "string" },
                                    ["operand"] = new Dictionary<string, object?> { ["type"] = "string" },
                                },
                                ["required"] = new[] { "opcode" },
                            },
                        },
                        ["count"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Number of instructions to remove/replace (default 1)." },
                        ["locals"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["description"] = "List of type names for set_locals.",
                            ["items"] = new Dictionary<string, object?> { ["type"] = "string" },
                        },
                        ["init_locals"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "Value for set_init_locals." },
                    },
                    ["required"] = new[] { "op" },
                },
            },
        },
        ["required"] = new[] { "assembly", "method_token", "edits" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm) || rawAsm is not string asm || string.IsNullOrEmpty(asm))
            return CallToolResult.Text("Missing 'assembly'.", isError: true);
        if (!GetMethodIlTool.TryParseToken(args, "method_token", out var token))
            return CallToolResult.Text("Missing or invalid 'method_token'.", isError: true);
        if (!args.TryGetValue("edits", out var rawEdits) || rawEdits is not List<object?> editsList)
            return CallToolResult.Text("Missing or invalid 'edits' (expected array).", isError: true);

        List<IlEdit> edits;
        try
        {
            edits = ParseEdits(editsList);
        }
        catch (Exception ex)
        {
            return CallToolResult.Text($"Invalid edits: {ex.Message}", isError: true);
        }

        try
        {
            var r = services.PatchMethodIl(asm, token, edits);
            var payload = new JsonObject
            {
                ["patched"] = r.Patched,
                ["new_instruction_count"] = r.NewInstructionCount,
                ["snapshot_taken"] = r.SnapshotTaken,
            };
            return CallToolResult.Text(payload.ToJsonString());
        }
        catch (Exception ex)
        {
            return CallToolResult.Text(ex.Message, isError: true);
        }
    }

    private static List<IlEdit> ParseEdits(List<object?> editsList)
    {
        var result = new List<IlEdit>(editsList.Count);
        foreach (var item in editsList)
        {
            if (item is not Dictionary<string, object?> d)
                throw new InvalidOperationException("Each edit must be an object.");
            var op = d.TryGetValue("op", out var opRaw) && opRaw is string opStr ? opStr
                : throw new InvalidOperationException("Edit missing 'op'.");
            int? atOffset = null;
            if (d.TryGetValue("at_offset", out var rawOffset) && rawOffset is not null)
                atOffset = rawOffset switch { int i => i, long l => (int)l, _ => (int?)null };
            int? count = null;
            if (d.TryGetValue("count", out var rawCount) && rawCount is not null)
                count = rawCount switch { int i => i, long l => (int)l, _ => (int?)null };
            bool? initLocals = null;
            if (d.TryGetValue("init_locals", out var rawInit) && rawInit is bool b)
                initLocals = b;

            List<IlInstructionSpec>? instructions = null;
            if (d.TryGetValue("instructions", out var rawInstr) && rawInstr is List<object?> instrList)
            {
                instructions = new List<IlInstructionSpec>(instrList.Count);
                foreach (var iraw in instrList)
                {
                    if (iraw is not Dictionary<string, object?> id)
                        throw new InvalidOperationException("Each instruction must be an object.");
                    var opc = id.TryGetValue("opcode", out var opcRaw) && opcRaw is string opcStr ? opcStr
                        : throw new InvalidOperationException("Instruction missing 'opcode'.");
                    string? operand = id.TryGetValue("operand", out var opndRaw) && opndRaw is string opndStr ? opndStr : null;
                    instructions.Add(new IlInstructionSpec(opc, operand));
                }
            }

            List<string>? locals = null;
            if (d.TryGetValue("locals", out var rawLocals) && rawLocals is List<object?> localsList)
            {
                locals = new List<string>(localsList.Count);
                foreach (var lraw in localsList)
                {
                    if (lraw is string ls) locals.Add(ls);
                    else throw new InvalidOperationException("Each local must be a string type name.");
                }
            }

            result.Add(new IlEdit(op, atOffset, instructions, count, locals, initLocals));
        }
        return result;
    }
}

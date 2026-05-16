using System;
using System.Collections.Generic;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class DecompileMethodTool : IMcpTool
{
    public string Name => "decompile_method";
    public string Description => "Decompile a method to C# given assembly identifier + method token.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["method_token"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Metadata token (decimal or hex string also accepted).",
            },
        },
        ["required"] = new[] { "assembly", "method_token" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm)
            || rawAsm is not string asm
            || string.IsNullOrEmpty(asm))
        {
            return CallToolResult.Text("Missing 'assembly'.", isError: true);
        }
        if (!args.TryGetValue("method_token", out var rawTok))
        {
            return CallToolResult.Text("Missing 'method_token'.", isError: true);
        }

        uint token;
        try
        {
            token = rawTok switch
            {
                int i => unchecked((uint)i),
                long l => unchecked((uint)l),
                string s when s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) =>
                    Convert.ToUInt32(s.Substring(2), 16),
                string s => uint.Parse(s),
                _ => throw new FormatException("method_token must be integer or hex string"),
            };
        }
        catch (Exception ex)
        {
            return CallToolResult.Text($"Bad method_token: {ex.Message}", isError: true);
        }

        try
        {
            var code = services.DecompileMethod(asm, token);
            return CallToolResult.Text(code);
        }
        catch (Exception ex)
        {
            return CallToolResult.Text($"Decompile failed: {ex.Message}", isError: true);
        }
    }
}

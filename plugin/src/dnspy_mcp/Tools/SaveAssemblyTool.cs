using System;
using System.Collections.Generic;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class SaveAssemblyTool : IMcpTool
{
    public string Name => "save_assembly";
    public string Description => "Save an assembly back to disk. Optional 'path' writes to a new location.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["path"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional destination path (default: original).",
            },
        },
        ["required"] = new[] { "assembly" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm)
            || rawAsm is not string asm
            || string.IsNullOrEmpty(asm))
        {
            return CallToolResult.Text("Missing 'assembly'.", isError: true);
        }

        string? dest = null;
        if (args.TryGetValue("path", out var rawPath)
            && rawPath is string s
            && !string.IsNullOrEmpty(s))
        {
            dest = s;
        }

        try
        {
            services.SaveAssembly(asm, dest);
            return CallToolResult.Text("{\"saved\":true}");
        }
        catch (Exception ex)
        {
            return CallToolResult.Text($"Save failed: {ex.Message}", isError: true);
        }
    }
}

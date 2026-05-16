using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class GetAssemblyInfoTool : IMcpTool
{
    public string Name => "get_assembly_info";
    public string Description => "Get metadata about a loaded assembly: name, full_name, path, runtime_version, module_count, type_count.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Assembly name, full name, or path." },
        },
        ["required"] = new[] { "assembly" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm) || rawAsm is not string asm || string.IsNullOrEmpty(asm))
            return CallToolResult.Text("Missing 'assembly'.", isError: true);

        try
        {
            var r = services.GetAssemblyInfo(asm);
            var payload = new JsonObject
            {
                ["name"] = r.Name,
                ["full_name"] = r.FullName,
                ["path"] = r.Path,
                ["runtime_version"] = r.RuntimeVersion,
                ["module_count"] = r.ModuleCount,
                ["type_count"] = r.TypeCount,
            };
            return CallToolResult.Text(payload.ToJsonString());
        }
        catch (Exception ex)
        {
            return CallToolResult.Text(ex.Message, isError: true);
        }
    }
}

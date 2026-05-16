using System.Collections.Generic;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class UnloadAssemblyTool : IMcpTool
{
    public string Name => "unload_assembly";
    public string Description => "Remove a loaded assembly from the document tree.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Path, full_name or short name.",
            },
        },
        ["required"] = new[] { "assembly" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var raw)
            || raw is not string id
            || string.IsNullOrEmpty(id))
        {
            return CallToolResult.Text("Missing 'assembly' argument.", isError: true);
        }
        var ok = services.CloseAssembly(id);
        return ok
            ? CallToolResult.Text("{\"closed\":true}")
            : CallToolResult.Text($"No matching assembly: {id}", isError: true);
    }
}

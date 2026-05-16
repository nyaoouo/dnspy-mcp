using System.Collections.Generic;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class ListAssembliesTool : IMcpTool
{
    public string Name => "list_assemblies";
    public string Description => "List loaded assemblies as {path, full_name, name} tuples.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>(),
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        var arr = new JsonArray();
        foreach (var asm in services.EnumerateAssemblies())
        {
            arr.Add(new JsonObject
            {
                ["path"] = asm.Path,
                ["full_name"] = asm.FullName,
                ["name"] = asm.Name,
            });
        }
        return CallToolResult.Text(arr.ToJsonString());
    }
}

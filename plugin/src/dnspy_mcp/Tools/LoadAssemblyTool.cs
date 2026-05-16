using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class LoadAssemblyTool : IMcpTool
{
    public string Name => "load_assembly";
    public string Description => "Open an assembly file in dnSpy.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["path"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Filesystem path to the assembly.",
            },
        },
        ["required"] = new[] { "path" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("path", out var raw)
            || raw is not string path
            || string.IsNullOrEmpty(path))
        {
            return CallToolResult.Text("Missing 'path' argument.", isError: true);
        }
        if (!File.Exists(path))
        {
            return CallToolResult.Text($"File not found: {path}", isError: true);
        }
        var handle = services.OpenAssembly(path);
        var obj = new JsonObject
        {
            ["path"] = handle.Path,
            ["full_name"] = handle.FullName,
            ["name"] = handle.Name,
        };
        return CallToolResult.Text(obj.ToJsonString());
    }
}

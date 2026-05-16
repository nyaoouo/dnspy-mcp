using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;

namespace DnspyMcp.SchemaDump;

public static class Program
{
    public static int Main(string[] args)
    {
        var output = "tools-schema.json";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
            {
                output = args[i + 1];
            }
        }

        var pluginAssembly = typeof(IMcpTool).Assembly;
        var registry = ToolRegistry.FromAssembly(pluginAssembly);
        var tools = new JsonArray();
        foreach (var tool in registry.Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(
                    JsonSerializer.Serialize(tool.GetInputSchema())),
            });
        }

        var pluginVersion = pluginAssembly.GetName().Version?.ToString() ?? "0.0.0";
        var root = new JsonObject
        {
            ["schema_version"] = 1,
            ["plugin_version"] = pluginVersion,
            ["tools"] = tools,
        };

        var fullPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
        File.WriteAllText(
            output,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote {tools.Count} tools to {output}");
        return 0;
    }
}

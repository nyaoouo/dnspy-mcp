using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Tools;

public sealed class GetTypePropertyTool : IMcpTool
{
    public string Name => "get_type_property";
    public string Description => "Get metadata for a single property of a type by name.";

    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["assembly"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Assembly name, full name, or path." },
            ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Type full name or token." },
            ["property"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Property name (exact)." },
        },
        ["required"] = new[] { "assembly", "type", "property" },
    };

    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices services)
    {
        if (!args.TryGetValue("assembly", out var rawAsm) || rawAsm is not string asm || string.IsNullOrEmpty(asm))
            return CallToolResult.Text("Missing 'assembly'.", isError: true);
        if (!args.TryGetValue("type", out var rawType) || rawType is not string typeName || string.IsNullOrEmpty(typeName))
            return CallToolResult.Text("Missing 'type'.", isError: true);
        if (!args.TryGetValue("property", out var rawProp) || rawProp is not string propName || string.IsNullOrEmpty(propName))
            return CallToolResult.Text("Missing 'property'.", isError: true);

        try
        {
            var r = services.GetTypeProperty(asm, typeName, propName);
            var payload = new JsonObject
            {
                ["name"] = r.Name,
                ["full_name"] = r.FullName,
                ["token"] = r.Token,
                ["property_type"] = r.PropertyType,
                ["has_getter"] = r.HasGetter,
                ["has_setter"] = r.HasSetter,
                ["getter_token"] = r.GetterToken,
                ["setter_token"] = r.SetterToken,
            };
            return CallToolResult.Text(payload.ToJsonString());
        }
        catch (Exception ex)
        {
            return CallToolResult.Text(ex.Message, isError: true);
        }
    }
}

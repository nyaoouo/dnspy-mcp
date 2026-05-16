using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DnspyMcp.Mcp;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IMcpTool> _tools = new(StringComparer.Ordinal);

    public IReadOnlyCollection<IMcpTool> Tools => _tools.Values;

    public bool TryGet(string name, out IMcpTool tool)
    {
        return _tools.TryGetValue(name, out tool!);
    }

    public ToolRegistry Register(IMcpTool tool)
    {
        if (_tools.ContainsKey(tool.Name))
            throw new InvalidOperationException($"Duplicate tool name: {tool.Name}");
        _tools.Add(tool.Name, tool);
        return this;
    }

    public static ToolRegistry FromAssembly(Assembly assembly)
    {
        var registry = new ToolRegistry();
        var toolType = typeof(IMcpTool);
        foreach (var t in assembly.GetTypes()
                     .Where(t => !t.IsAbstract && toolType.IsAssignableFrom(t)))
        {
            var instance = (IMcpTool)Activator.CreateInstance(t)!;
            registry.Register(instance);
        }
        return registry;
    }
}

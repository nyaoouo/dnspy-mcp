using System.Collections.Generic;
using DnspyMcp.Util;

namespace DnspyMcp.Mcp;

public interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    IDictionary<string, object?> GetInputSchema();
    CallToolResult Invoke(IDictionary<string, object?> arguments, IDnSpyServices services);
}

using System.Collections.Generic;
using System.Text.Json.Nodes;
using DnspyMcp.Mcp;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

internal sealed class StubServices : IDnSpyServices
{
    public System.Collections.Generic.IEnumerable<AssemblyHandle> EnumerateAssemblies() =>
        throw new System.NotImplementedException();
    public AssemblyHandle OpenAssembly(string path) =>
        throw new System.NotImplementedException();
    public bool CloseAssembly(string identifier) =>
        throw new System.NotImplementedException();
    public string DecompileMethod(string assemblyIdentifier, uint methodToken) =>
        throw new System.NotImplementedException();
    public void SaveAssembly(string identifier, string? destination) =>
        throw new System.NotImplementedException();
    public AssemblyInfoResult GetAssemblyInfo(string assemblyIdentifier) =>
        throw new System.NotImplementedException();
    public TypeListResult ListTypes(string assemblyIdentifier, string? @namespace, int offset, int limit) =>
        throw new System.NotImplementedException();
    public TypeInfoResult GetTypeInfo(string assemblyIdentifier, string typeIdentifier) =>
        throw new System.NotImplementedException();
    public FieldListResult GetTypeFields(string assemblyIdentifier, string typeIdentifier, string pattern, int offset, int limit) =>
        throw new System.NotImplementedException();
    public PropertyResult GetTypeProperty(string assemblyIdentifier, string typeIdentifier, string propertyName) =>
        throw new System.NotImplementedException();
    public MethodListResult ListMethods(string assemblyIdentifier, string typeIdentifier, int offset, int limit) =>
        throw new System.NotImplementedException();
    public TypeSearchResult SearchTypes(string pattern, string? scopeAssembly, int offset, int limit) =>
        throw new System.NotImplementedException();
    public PathSearchResult FindPathToType(string assemblyIdentifier, string fromType, string toType, int maxDepth) =>
        throw new System.NotImplementedException();
    public MethodIlResult GetMethodIl(string assemblyIdentifier, uint methodToken) =>
        throw new System.NotImplementedException();
    public PatchResult PatchMethodIl(string assemblyIdentifier, uint methodToken, System.Collections.Generic.IReadOnlyList<IlEdit> edits) =>
        throw new System.NotImplementedException();
    public RevertResult RevertMethodIl(string assemblyIdentifier, uint methodToken) =>
        throw new System.NotImplementedException();
    public RevertAllResult RevertAllPendingPatches(string? scopeAssembly) =>
        throw new System.NotImplementedException();
}

internal sealed class EchoTool : IMcpTool
{
    public string Name => "echo";
    public string Description => "echo back the text arg";
    public IDictionary<string, object?> GetInputSchema() => new Dictionary<string, object?>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["text"] = new Dictionary<string, object?> { ["type"] = "string" },
        },
    };
    public CallToolResult Invoke(IDictionary<string, object?> args, IDnSpyServices svc)
    {
        var text = args.TryGetValue("text", out var v) && v is string s ? s : "";
        return CallToolResult.Text(text);
    }
}

public class ProtocolTests
{
    private static Protocol MakeProtocol()
    {
        var registry = new ToolRegistry();
        registry.Register(new EchoTool());
        return new Protocol(registry, new StubServices());
    }

    [Fact]
    public void Initialize_ReturnsServerInfo()
    {
        var env = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""")!.AsObject();
        var resp = MakeProtocol().Handle(env);
        Assert.Equal("dnspy-mcp-plugin", resp["result"]!["serverInfo"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsList_IncludesRegisteredTool()
    {
        var env = JsonNode.Parse("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""")!.AsObject();
        var resp = MakeProtocol().Handle(env);
        var tools = resp["result"]!["tools"]!.AsArray();
        Assert.Single(tools);
        Assert.Equal("echo", tools[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_InvokesTool()
    {
        var env = JsonNode.Parse("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo","arguments":{"text":"hi"}}}""")!.AsObject();
        var resp = MakeProtocol().Handle(env);
        Assert.Equal("hi", resp["result"]!["content"]!.AsArray()[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void UnknownMethod_ReturnsMethodNotFound()
    {
        var env = JsonNode.Parse("""{"jsonrpc":"2.0","id":4,"method":"nope"}""")!.AsObject();
        var resp = MakeProtocol().Handle(env);
        Assert.Equal(JsonRpcErrors.MethodNotFound, resp["error"]!["code"]!.GetValue<int>());
    }
}

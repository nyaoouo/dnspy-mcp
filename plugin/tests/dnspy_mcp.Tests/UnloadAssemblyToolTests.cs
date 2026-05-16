using System.Collections.Generic;
using DnspyMcp.Tools;
using Xunit;

namespace DnspyMcp.Tests;

public class UnloadAssemblyToolTests
{
    [Fact]
    public void Unloads_RegisteredAssembly()
    {
        var svc = new FakeServices();
        svc.OpenAssembly("C:/a.dll");
        var r = new UnloadAssemblyTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "C:/a.dll" },
            svc);
        Assert.False(r.IsError);
        Assert.Empty(svc.Assemblies);
    }

    [Fact]
    public void ErrorsOnUnknown()
    {
        var svc = new FakeServices();
        var r = new UnloadAssemblyTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "ghost" },
            svc);
        Assert.True(r.IsError);
    }
}

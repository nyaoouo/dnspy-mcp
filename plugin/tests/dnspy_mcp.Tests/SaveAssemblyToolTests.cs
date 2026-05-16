using System.Collections.Generic;
using DnspyMcp.Tools;
using Xunit;

namespace DnspyMcp.Tests;

public class SaveAssemblyToolTests
{
    [Fact]
    public void HappyPath()
    {
        var svc = new FakeServices();
        svc.OpenAssembly("C:/a.dll");
        var r = new SaveAssemblyTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "C:/a.dll" },
            svc);
        Assert.False(r.IsError);
    }

    [Fact]
    public void MissingAssembly()
    {
        Assert.True(
            new SaveAssemblyTool()
                .Invoke(new Dictionary<string, object?>(), new FakeServices())
                .IsError);
    }
}

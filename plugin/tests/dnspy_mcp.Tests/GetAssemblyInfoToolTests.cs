using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class GetAssemblyInfoToolTests
{
    [Fact]
    public void HappyPath_ReturnsAssemblyInfo()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "MyClass", "NS.MyClass", 0x02000001, "class"));

        var r = new GetAssemblyInfoTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "MyAssembly" }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal("MyAssembly", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("type_count").GetInt32());
    }

    [Fact]
    public void MissingAssemblyArg_ReturnsError()
    {
        var r = new GetAssemblyInfoTool().Invoke(
            new Dictionary<string, object?>(), new FakeServices());
        Assert.True(r.IsError);
    }
}

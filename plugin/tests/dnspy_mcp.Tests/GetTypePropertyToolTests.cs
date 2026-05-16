using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class GetTypePropertyToolTests
{
    [Fact]
    public void HappyPath_ReturnsProperty()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "MyClass", "NS.MyClass", 0x02000001, "class"));
        svc.Properties.Add(new PropertyResult(
            "Name", "NS.MyClass::Name", 0x17000001, "System.String",
            true, false, 0x06000001, 0));

        var r = new GetTypePropertyTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["type"] = "NS.MyClass", ["property"] = "Name" }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal("Name", doc.RootElement.GetProperty("name").GetString());
        Assert.True(doc.RootElement.GetProperty("has_getter").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("has_setter").GetBoolean());
    }

    [Fact]
    public void MissingPropertyArg_ReturnsError()
    {
        var r = new GetTypePropertyTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["type"] = "NS.MyClass" }, new FakeServices());
        Assert.True(r.IsError);
    }

    [Fact]
    public void PropertyNotFound_ReturnsError()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "MyClass", "NS.MyClass", 0x02000001, "class"));
        var r = new GetTypePropertyTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["type"] = "NS.MyClass", ["property"] = "Missing" }, svc);
        Assert.True(r.IsError);
    }
}

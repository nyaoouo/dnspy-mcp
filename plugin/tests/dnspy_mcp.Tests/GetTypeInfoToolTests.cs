using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class GetTypeInfoToolTests
{
    [Fact]
    public void HappyPath_ReturnsTypeInfo()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "MyClass", "NS.MyClass", 0x02000001, "class"));

        var r = new GetTypeInfoTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["type"] = "NS.MyClass" }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal("NS.MyClass", doc.RootElement.GetProperty("full_name").GetString());
        Assert.Equal("class", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void MissingTypeArg_ReturnsError()
    {
        var r = new GetTypeInfoTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A" }, new FakeServices());
        Assert.True(r.IsError);
    }

    [Fact]
    public void TypeNotFound_ReturnsError()
    {
        var svc = new FakeServices();
        var r = new GetTypeInfoTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["type"] = "NS.Missing" }, svc);
        Assert.True(r.IsError);
    }
}

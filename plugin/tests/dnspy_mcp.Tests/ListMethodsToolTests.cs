using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class ListMethodsToolTests
{
    [Fact]
    public void HappyPath_ReturnsMethods()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "MyClass", "NS.MyClass", 0x02000001, "class"));
        svc.Methods.Add(new MethodSummary(
            "DoWork", "NS.MyClass::DoWork", 0x06000001, "System.Void",
            new List<ParameterSummary> { new("x", "System.Int32") }, false, false));

        var r = new ListMethodsTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["type"] = "NS.MyClass" }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal(1, doc.RootElement.GetProperty("total").GetInt32());
        var method = doc.RootElement.GetProperty("methods")[0];
        Assert.Equal("DoWork", method.GetProperty("name").GetString());
        Assert.Equal(1, method.GetProperty("parameters").GetArrayLength());
    }

    [Fact]
    public void MissingTypeArg_ReturnsError()
    {
        var r = new ListMethodsTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A" }, new FakeServices());
        Assert.True(r.IsError);
    }

    [Fact]
    public void Pagination_NextCursorSet()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "MyClass", "NS.MyClass", 0x02000001, "class"));
        for (int i = 0; i < 5; i++)
            svc.Methods.Add(new MethodSummary(
                $"M{i}", $"NS.MyClass::M{i}", (uint)(0x06000001 + i), "System.Void",
                new List<ParameterSummary>(), false, false));

        var r = new ListMethodsTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["type"] = "NS.MyClass", ["page_size"] = 3 }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal("3", doc.RootElement.GetProperty("next_cursor").GetString());
    }
}

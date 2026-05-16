using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class ListTypesToolTests
{
    [Fact]
    public void HappyPath_ReturnsTypes()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "ClassA", "NS.ClassA", 0x02000001, "class"));
        svc.Types.Add(new TypeSummary("NS", "ClassB", "NS.ClassB", 0x02000002, "class"));

        var r = new ListTypesTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "MyAssembly" }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal(2, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("types").GetArrayLength());
    }

    [Fact]
    public void MissingAssemblyArg_ReturnsError()
    {
        var r = new ListTypesTool().Invoke(
            new Dictionary<string, object?>(), new FakeServices());
        Assert.True(r.IsError);
    }

    [Fact]
    public void Pagination_NextCursorSet()
    {
        var svc = new FakeServices();
        for (int i = 0; i < 5; i++)
            svc.Types.Add(new TypeSummary("NS", $"T{i}", $"NS.T{i}", (uint)(0x02000001 + i), "class"));

        var r = new ListTypesTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["page_size"] = 3 }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal("3", doc.RootElement.GetProperty("next_cursor").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("types").GetArrayLength());
    }
}

using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class SearchTypesToolTests
{
    [Fact]
    public void HappyPath_ReturnsMatchingTypes()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "DocumentService", "NS.DocumentService", 0x02000001, "class"));
        svc.Types.Add(new TypeSummary("NS", "OtherClass", "NS.OtherClass", 0x02000002, "class"));

        var r = new SearchTypesTool().Invoke(
            new Dictionary<string, object?> { ["pattern"] = "*Document*" }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal(1, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("NS.DocumentService", doc.RootElement.GetProperty("results")[0].GetProperty("full_name").GetString());
    }

    [Fact]
    public void MissingPatternArg_ReturnsError()
    {
        var r = new SearchTypesTool().Invoke(
            new Dictionary<string, object?>(), new FakeServices());
        Assert.True(r.IsError);
    }

    [Fact]
    public void Pagination_NextCursorSet()
    {
        var svc = new FakeServices();
        for (int i = 0; i < 6; i++)
            svc.Types.Add(new TypeSummary("NS", $"Foo{i}", $"NS.Foo{i}", (uint)(0x02000001 + i), "class"));

        var r = new SearchTypesTool().Invoke(
            new Dictionary<string, object?> { ["pattern"] = "*Foo*", ["page_size"] = 4 }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal("4", doc.RootElement.GetProperty("next_cursor").GetString());
    }
}

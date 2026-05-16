using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class GetTypeFieldsToolTests
{
    [Fact]
    public void HappyPath_ReturnsFields()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "MyClass", "NS.MyClass", 0x02000001, "class"));
        svc.Fields.Add(new FieldSummary("_name", "NS.MyClass::_name", 0x04000001, "System.String", false));
        svc.Fields.Add(new FieldSummary("_count", "NS.MyClass::_count", 0x04000002, "System.Int32", true));

        var r = new GetTypeFieldsTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["type"] = "NS.MyClass" }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal(2, doc.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public void MissingTypeArg_ReturnsError()
    {
        var r = new GetTypeFieldsTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A" }, new FakeServices());
        Assert.True(r.IsError);
    }

    [Fact]
    public void Pagination_NextCursorSet()
    {
        var svc = new FakeServices();
        svc.Types.Add(new TypeSummary("NS", "MyClass", "NS.MyClass", 0x02000001, "class"));
        for (int i = 0; i < 5; i++)
            svc.Fields.Add(new FieldSummary($"_f{i}", $"NS.MyClass::_f{i}", (uint)(0x04000001 + i), "System.Int32", false));

        var r = new GetTypeFieldsTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["type"] = "NS.MyClass", ["page_size"] = 2 }, svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal("2", doc.RootElement.GetProperty("next_cursor").GetString());
    }
}

using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class FindPathToTypeToolTests
{
    [Fact]
    public void HappyPath_ReturnsPaths()
    {
        var svc = new FakeServices();
        svc.Paths.Add(new TypePath(new List<PathStep>
        {
            new PathStep("field", "_doc", 0x04000001u, "MyApp.Root", "MyApp.Doc"),
            new PathStep("property", "Name", 0x17000001u, "MyApp.Doc", "System.String"),
        }));

        var r = new FindPathToTypeTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["from_type"] = "MyApp.Root", ["to_type"] = "System.String" },
            svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal(1, doc.RootElement.GetProperty("paths").GetArrayLength());
        var steps = doc.RootElement.GetProperty("paths")[0].GetProperty("steps");
        Assert.Equal(2, steps.GetArrayLength());
        Assert.Equal("field", steps[0].GetProperty("kind").GetString());
        Assert.Equal("_doc", steps[0].GetProperty("member_name").GetString());
    }

    [Fact]
    public void MissingFromType_ReturnsError()
    {
        var r = new FindPathToTypeTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["to_type"] = "System.String" },
            new FakeServices());
        Assert.True(r.IsError);
    }
}

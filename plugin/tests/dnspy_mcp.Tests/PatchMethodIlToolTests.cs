using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class PatchMethodIlToolTests
{
    [Fact]
    public void EmptyEdits_SucceedsAndSnapshotTaken()
    {
        var svc = new FakeServices();
        // edits is an empty list -> PatchSucceeded=true, SnapshotTaken=true
        var editsList = new List<object?>();

        var r = new PatchMethodIlTool().Invoke(
            new Dictionary<string, object?>
            {
                ["assembly"] = "A",
                ["method_token"] = 0x06000001L,
                ["edits"] = editsList,
            },
            svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.True(doc.RootElement.GetProperty("patched").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("snapshot_taken").GetBoolean());
    }

    [Fact]
    public void MissingEdits_ReturnsError()
    {
        var r = new PatchMethodIlTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["method_token"] = 0x06000001L },
            new FakeServices());
        Assert.True(r.IsError);
    }
}

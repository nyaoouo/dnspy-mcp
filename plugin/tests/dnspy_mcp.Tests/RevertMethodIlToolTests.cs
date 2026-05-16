using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class RevertMethodIlToolTests
{
    [Fact]
    public void HappyPath_ReturnsReverted()
    {
        var svc = new FakeServices { RevertSucceeded = true };

        var r = new RevertMethodIlTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["method_token"] = 0x06000001L },
            svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.True(doc.RootElement.GetProperty("reverted").GetBoolean());
    }

    [Fact]
    public void MissingMethodToken_ReturnsError()
    {
        var r = new RevertMethodIlTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A" },
            new FakeServices());
        Assert.True(r.IsError);
    }
}

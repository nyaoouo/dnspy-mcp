using System.Collections.Generic;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class RevertAllPendingPatchesToolTests
{
    [Fact]
    public void HappyPath_NoScope()
    {
        var svc = new FakeServices();
        svc.RevertedAll.Add(new RevertedMethod("a.dll", 0x06000001u, "A.M()"));
        svc.RevertedAll.Add(new RevertedMethod("b.dll", 0x06000005u, "B.N()"));
        var r = new RevertAllPendingPatchesTool().Invoke(new Dictionary<string, object?>(), svc);
        Assert.False(r.IsError);
        Assert.Contains("\"reverted_count\":2", r.Content[0].Text);
        Assert.Contains("A.M()", r.Content[0].Text);
    }

    [Fact]
    public void HappyPath_ScopedAssembly()
    {
        var svc = new FakeServices();
        var r = new RevertAllPendingPatchesTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "a.dll" }, svc);
        Assert.False(r.IsError);
    }
}

using System.Collections.Generic;
using DnspyMcp.Tools;
using Xunit;

namespace DnspyMcp.Tests;

public class DecompileMethodToolTests
{
    [Fact]
    public void HappyPath()
    {
        var svc = new FakeServices();
        var r = new DecompileMethodTool().Invoke(
            new Dictionary<string, object?>
            {
                ["assembly"] = "a.dll",
                ["method_token"] = "0x06000001",
            },
            svc);
        Assert.False(r.IsError);
        Assert.Contains("decompiled", r.Content[0].Text);
    }

    [Fact]
    public void MissingArgs()
    {
        Assert.True(
            new DecompileMethodTool()
                .Invoke(new Dictionary<string, object?>(), new FakeServices())
                .IsError);
    }
}

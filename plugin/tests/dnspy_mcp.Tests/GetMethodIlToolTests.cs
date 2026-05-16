using System.Collections.Generic;
using System.Text.Json;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

public class GetMethodIlToolTests
{
    [Fact]
    public void HappyPath_ReturnsIlBody()
    {
        var svc = new FakeServices();
        svc.MethodIl = new MethodIlResult(
            "System.Void MyApp.Foo::Bar()",
            8,
            true,
            new List<IlLocal> { new IlLocal(0, "System.Int32") },
            new List<IlInstruction>
            {
                new IlInstruction(0, "nop", null),
                new IlInstruction(1, "ret", null),
            },
            new List<IlExceptionHandler>());

        var r = new GetMethodIlTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A", ["method_token"] = 0x06000001L },
            svc);

        Assert.False(r.IsError);
        var doc = JsonDocument.Parse(r.Content[0].Text);
        Assert.Equal("System.Void MyApp.Foo::Bar()", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("instructions").GetArrayLength());
        Assert.Equal("nop", doc.RootElement.GetProperty("instructions")[0].GetProperty("opcode").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("locals").GetArrayLength());
    }

    [Fact]
    public void MissingMethodToken_ReturnsError()
    {
        var r = new GetMethodIlTool().Invoke(
            new Dictionary<string, object?> { ["assembly"] = "A" },
            new FakeServices());
        Assert.True(r.IsError);
    }
}

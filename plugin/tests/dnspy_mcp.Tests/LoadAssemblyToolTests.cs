using System.Collections.Generic;
using System.IO;
using DnspyMcp.Tools;
using Xunit;

namespace DnspyMcp.Tests;

public class LoadAssemblyToolTests
{
    [Fact]
    public void Loads_ExistingFile()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var svc = new FakeServices();
            var r = new LoadAssemblyTool().Invoke(
                new Dictionary<string, object?> { ["path"] = tmp }, svc);
            Assert.False(r.IsError);
            Assert.Single(svc.Assemblies);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void RejectsMissingFile()
    {
        var svc = new FakeServices();
        var r = new LoadAssemblyTool().Invoke(
            new Dictionary<string, object?> { ["path"] = "Z:/nope.dll" },
            svc);
        Assert.True(r.IsError);
    }

    [Fact]
    public void RequiresPathArg()
    {
        var svc = new FakeServices();
        var r = new LoadAssemblyTool().Invoke(
            new Dictionary<string, object?>(), svc);
        Assert.True(r.IsError);
    }
}

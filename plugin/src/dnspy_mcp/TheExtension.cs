using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using dnSpy.Contracts.Extension;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp;

[ExportExtension]
public sealed class TheExtension : IExtension
{
    private readonly DnSpyServices _services;
    private HttpServer? _server;

    [ImportingConstructor]
    public TheExtension(DnSpyServices services)
    {
        _services = services;
    }

    public ExtensionInfo ExtensionInfo => new()
    {
        ShortDescription =
            "dnspy-mcp: MCP server plugin (supervised by dnspy-mcp Python "
            + "supervisor or standalone via settings).",
    };

    public IEnumerable<string> MergedResourceDictionaries =>
        Enumerable.Empty<string>();

    public void OnEvent(ExtensionEvent @event, object? obj)
    {
        switch (@event)
        {
            case ExtensionEvent.Loaded:
                Start();
                break;
            case ExtensionEvent.AppExit:
                _server?.Stop();
                break;
        }
    }

    private void Start()
    {
        var port = Environment.GetEnvironmentVariable("DNSPY_MCP_PORT");
        var token = Environment.GetEnvironmentVariable("DNSPY_MCP_TOKEN");
        if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            return; // Standalone mode: StandaloneRunner takes over.
        var bind = Environment.GetEnvironmentVariable("DNSPY_MCP_BIND") ?? "127.0.0.1";
        var registry = ToolRegistry.FromAssembly(Assembly.GetExecutingAssembly());
        var protocol = new Protocol(registry, _services);
        _server = new HttpServer(
            protocol,
            new HttpServerOptions
            {
                Port = port,
                Token = token,
                Bind = bind,
                Mode = "supervised",
            });
        _server.Start();
    }
}

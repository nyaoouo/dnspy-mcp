using System;
using System.ComponentModel.Composition;
using System.Reflection;
using DnspyMcp.Mcp;
using DnspyMcp.Util;

namespace DnspyMcp.Settings;

[Export]
public sealed class StandaloneRunner
{
    private readonly McpSettings _settings;
    private readonly IDnSpyServices _services;
    private HttpServer? _server;

    [ImportingConstructor]
    public StandaloneRunner(McpSettings settings, DnSpyServices services)
    {
        _settings = settings;
        _services = services;
        _settings.Applied += (_, _) => ApplyState();
        ApplyState();
    }

    private void ApplyState()
    {
        if (Environment.GetEnvironmentVariable("DNSPY_MCP_PORT") is { Length: > 0 })
            return; // supervised mode is in control
        _server?.Stop();
        _server = null;
        if (!_settings.Enabled) return;
        var registry = ToolRegistry.FromAssembly(Assembly.GetExecutingAssembly());
        _server = new HttpServer(
            new Protocol(registry, _services),
            new HttpServerOptions
            {
                Port = _settings.Port.ToString(),
                Token = string.IsNullOrEmpty(_settings.Token)
                    ? "anonymous"
                    : _settings.Token,
                Bind = _settings.Bind,
                Mode = "standalone",
            });
        _server.Start();
    }
}

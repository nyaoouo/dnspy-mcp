using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using DnspyMcp.Mcp;
using Xunit;

namespace DnspyMcp.Tests;

public class HttpServerTests
{
    private static (HttpServer server, int port) StartServer(string token)
    {
        var port = GetFreePort();
        var registry = new ToolRegistry();
        registry.Register(new EchoTool());
        var server = new HttpServer(
            new Protocol(registry, new StubServices()),
            new HttpServerOptions
            {
                Port = port.ToString(),
                Token = token,
                Bind = "127.0.0.1",
            });
        server.Start();
        return (server, port);
    }

    private static int GetFreePort()
    {
        var s = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, 0);
        s.Start();
        var p = ((System.Net.IPEndPoint)s.LocalEndpoint).Port;
        s.Stop();
        return p;
    }

    [Fact]
    public async Task BadToken_Returns401()
    {
        var (server, port) = StartServer("good");
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "bad");
            var resp = await http.GetAsync($"http://127.0.0.1:{port}/health");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Health_OkOnGoodToken()
    {
        var (server, port) = StartServer("good");
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "good");
            var resp = await http.GetAsync($"http://127.0.0.1:{port}/health");
            Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void NonLoopbackBind_Rejected()
    {
        var registry = new ToolRegistry();
        registry.Register(new EchoTool());
        Assert.Throws<System.InvalidOperationException>(() =>
            new HttpServer(
                new Protocol(registry, new StubServices()),
                new HttpServerOptions
                {
                    Port = "8000",
                    Token = "t",
                    Bind = "0.0.0.0",
                }));
    }

    [Fact]
    public void MissingPort_Rejected()
    {
        var registry = new ToolRegistry();
        registry.Register(new EchoTool());
        Assert.Throws<System.InvalidOperationException>(() =>
            new HttpServer(
                new Protocol(registry, new StubServices()),
                new HttpServerOptions { Port = null, Token = "t" }));
    }
}

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DnspyMcp.Mcp;

public sealed class HttpServerOptions
{
    public string? Port { get; init; }
    public string? Token { get; init; }
    public string Bind { get; init; } = "127.0.0.1";
    public string Mode { get; init; } = "supervised";
}

public sealed class HttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Protocol _protocol;
    private readonly HttpServerOptions _opts;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public HttpServer(Protocol protocol, HttpServerOptions opts)
    {
        if (string.IsNullOrEmpty(opts.Port))
            throw new InvalidOperationException("DNSPY_MCP_PORT not set");
        if (string.IsNullOrEmpty(opts.Token))
            throw new InvalidOperationException("DNSPY_MCP_TOKEN not set");
        if (!IsLoopback(opts.Bind))
            throw new InvalidOperationException(
                $"Bind must be loopback, got: {opts.Bind}");
        _protocol = protocol;
        _opts = opts;
        _listener.Prefixes.Add($"http://{opts.Bind}:{opts.Port}/");
    }

    private static bool IsLoopback(string host)
    {
        if (host == "localhost") return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    public void Start()
    {
        _listener.Start();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener.Stop();
            _loop?.Wait(2000);
        }
        catch
        {
            // ignore shutdown races
        }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            var auth = ctx.Request.Headers["Authorization"] ?? "";
            if (auth != $"Bearer {_opts.Token}")
            {
                ctx.Response.StatusCode = 401;
                ctx.Response.Close();
                return;
            }
            if (ctx.Request.Url!.AbsolutePath == "/health"
                && ctx.Request.HttpMethod == "GET")
            {
                WriteJson(ctx.Response, 200,
                    $"{{\"ok\":true,\"mode\":\"{_opts.Mode}\"}}");
                return;
            }
            if (ctx.Request.HttpMethod != "POST"
                || ctx.Request.Url!.AbsolutePath != "/")
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
            using var reader = new StreamReader(
                ctx.Request.InputStream, Encoding.UTF8);
            var body = reader.ReadToEnd();
            var envelope = JsonNode.Parse(body)?.AsObject() ?? new JsonObject();
            var response = _protocol.Handle(envelope);
            WriteJson(ctx.Response, 200, response.ToJsonString());
        }
        catch (Exception ex)
        {
            try
            {
                WriteJson(
                    ctx.Response,
                    200,
                    $"{{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{{\"code\":{JsonRpcErrors.InternalError},\"message\":{JsonEscape(ex.Message)}}}}}");
            }
            catch
            {
                // swallow
            }
        }
    }

    private static void WriteJson(HttpListenerResponse resp, int status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        resp.StatusCode = status;
        resp.ContentType = "application/json";
        resp.ContentLength64 = bytes.Length;
        resp.OutputStream.Write(bytes, 0, bytes.Length);
        resp.Close();
    }

    private static string JsonEscape(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

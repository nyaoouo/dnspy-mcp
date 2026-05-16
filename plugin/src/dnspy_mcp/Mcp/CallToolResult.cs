using System.Collections.Generic;

namespace DnspyMcp.Mcp;

public sealed class CallToolResult
{
    public bool IsError { get; init; }
    public IList<CallToolContent> Content { get; init; } = new List<CallToolContent>();
    public object? StructuredContent { get; init; }

    public static CallToolResult Text(string text, bool isError = false)
    {
        var r = new CallToolResult { IsError = isError };
        r.Content.Add(new CallToolContent { Type = "text", Text = text });
        return r;
    }
}

public sealed class CallToolContent
{
    public string Type { get; init; } = "text";
    public string Text { get; init; } = string.Empty;
}

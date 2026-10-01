// McpClientHelper.cs — small JSON-RPC client for the local MCP server.
// Used by MAF agents (maf-spec, future ones) to invoke tools that the
// dashboard owns (kanban, etc.) without us having to maintain a parallel
// HTTP wrapper.
//
// The MCP server is mounted on the same process at /mcp, so we point at
// 127.0.0.1:<PORT>/mcp (port comes from MAF_MCP_PORT env or defaults to
// the dev port 5014). Override with MAF_MCP_URL for prod/external.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MafMiniMaxAgent.Mcp;

internal static class McpClientHelper
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // Cache the session id once we initialize once.
    private static string? _sessionId;
    private static readonly Lock Lock = new();

    private static string McpBaseUrl =>
        Environment.GetEnvironmentVariable("MAF_MCP_URL")
        ?? $"http://127.0.0.1:{Environment.GetEnvironmentVariable("MAF_MCP_PORT") ?? "5014"}/mcp";

    /// <summary>
    /// Initializes a session if needed and invokes the named MCP tool with the
    /// given arguments object. Returns the assistant-visible text content as a
    /// plain string (or "[empty result]" if the server returned no text).
    /// </summary>
    public static async Task<string> CallToolAsync(string toolName, object arguments)
    {
        try
        {
            await EnsureSessionAsync();

            var payload = new
            {
                jsonrpc = "2.0",
                id = Interlocked.Increment(ref _idCounter),
                method = "tools/call",
                @params = new { name = toolName, arguments },
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, McpBaseUrl)
            {
                Content = JsonContent.Create(payload),
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (_sessionId is not null) req.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                return $"ERROR: MCP upstream HTTP {(int)resp.StatusCode}: {err.SafeSubstring(0, 300)}";
            }

            var body = await resp.Content.ReadAsStringAsync();
            return ExtractFirstTextContent(body);
        }
        catch (Exception ex)
        {
            return $"ERROR: MCP call failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Parse an MCP JSON-RPC response body (possibly wrapped in SSE
    /// "event: message\ndata: ...\n\n") and return the first text content
    /// item from result.content[]. Returns "[empty result]" if none.
    /// </summary>
    internal static string ExtractFirstTextContent(string body)
    {
        // SSE format: lines "event: <name>\ndata: <json>\n\n". Strip the
        // prefix and grab the first data line as the RPC payload.
        string? dataJson = null;
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                dataJson = line["data: ".Length..].Trim();
                break;
            }
        }
        dataJson ??= body;

        using var doc = JsonDocument.Parse(dataJson);
        if (doc.RootElement.TryGetProperty("error", out var errEl))
            return $"ERROR: MCP tool error: {errEl.GetRawText()}";

        if (!doc.RootElement.TryGetProperty("result", out var resultEl))
            return "ERROR: MCP response missing 'result'";

        if (resultEl.TryGetProperty("content", out var contentEl)
            && contentEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contentEl.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var t)
                    && string.Equals(t.GetString(), "text", StringComparison.Ordinal)
                    && item.TryGetProperty("text", out var txt))
                {
                    return txt.GetString() ?? "[empty text]";
                }
            }
        }
        return "[empty result]";
    }

    private static int _idCounter;

    private static async Task EnsureSessionAsync()
    {
        if (_sessionId is not null) return;
        lock (Lock)
        {
            if (_sessionId is not null) return;
        }

        var initPayload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "maf-spec-mcp-client", version = "0.1.0" },
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, McpBaseUrl)
        {
            Content = JsonContent.Create(initPayload),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"MCP initialize HTTP {(int)resp.StatusCode}: {err.SafeSubstring(0, 200)}");
        }

        if (resp.Headers.TryGetValues("Mcp-Session-Id", out var values))
        {
            lock (Lock) { _sessionId = values.First(); }
        }

        // Discard the response body — we only need the session id.
        _ = resp.Content.ReadAsStringAsync();
    }
}
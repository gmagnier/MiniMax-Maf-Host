// MafLeadDispatcher.cs — tool used by maf-lead to delegate work to other
// agents in the same MAF host. Implementation is a simple HTTP POST to the
// AG-UI endpoint of the target agent on this same host (which is exposed at
// 127.0.0.1:<PORT>/ag-ui/<name>).
//
// Why this design:
//   - No ModelContextProtocol.Client dependency (lighter, fewer moving parts).
//   - The AG-UI protocol is the same one the dashboard chat UI speaks, so
//     whatever maf-lead can call, a human could call directly via curl. That
//     symmetry makes the whole thing debuggable from a single tool.
//   - Returning the agent's final text keeps maf-lead in control of the
//     user-facing response (it can summarize, ask for clarifications, etc.).
//
// Caveats:
//   - The current implementation is **synchronous**: maf-lead waits for the
//     target agent to finish before responding. Streaming live to the browser
//     from a tool is not yet wired; that's a future improvement.
//   - The dispatched agent's session is identified by a synthetic thread id
//     derived from the caller's thread so the target agent can optionally
//     pull history via its own MCP tools if it wants.

using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SCDesc = System.ComponentModel.DescriptionAttribute;

namespace MafMiniMaxAgent.Tools;

public static class MafLeadDispatcher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    // Known dispatchable agents and their AG-UI paths on this host.
    // Keep this list in sync with Program.cs (MapAGUIServer for spec/coder).
    private static readonly Dictionary<string, string> AgentPaths = new(StringComparer.Ordinal)
    {
        ["maf-spec"] = "/ag-ui/spec",
        ["maf-coder"] = "/ag-ui/coder",
    };

    private static string BaseUrl
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("MAF_AGUI_BASE_URL");
            if (!string.IsNullOrWhiteSpace(env)) return env.TrimEnd('/');
            var port = Environment.GetEnvironmentVariable("MAF_MCP_PORT") ?? "5014";
            return $"http://127.0.0.1:{port}";
        }
    }

    /// <summary>
    /// Delegate a task to another agent in this host. maf-lead calls this
    /// when the user request clearly belongs to a specialist (specs/openspec
    /// → maf-spec, code implementation → maf-coder). Returns the target
    /// agent's final visible text as a plain string.
    /// </summary>
    [SCDesc("Delegate a task to another MAF agent on this host. Use this when the user request is clearly owned by a specialist (OpenSpec specs/changes -> maf-spec, code implementation -> maf-coder). Returns the target agent's final visible text. Do NOT call this for general conversation — answer directly.")]
    public static async Task<string> DelegateToAgentAsync(
        [SCDesc("Target agent name. One of: 'maf-spec' (OpenSpec specialist), 'maf-coder' (code implementation). Required.")] string agent,
        [SCDesc("The task to send to the target agent. Be specific: include the goal, any relevant context the agent does not have, and the expected deliverable. The agent sees this as its user message.")] string task,
        [SCDesc("Optional thread id. If provided, the dispatcher forwards it as the AG-UI threadId so the target agent can keep state across calls. If omitted, a fresh synthetic id is used (no persistence).")] string? threadId = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agent))
                return "ERROR: agent is required";
            if (string.IsNullOrWhiteSpace(task))
                return "ERROR: task is required";

            if (!AgentPaths.TryGetValue(agent, out var path))
                return $"ERROR: unknown agent '{agent}'. Valid options: {string.Join(", ", AgentPaths.Keys)}";

            // Wrap the user task in a system prefix so the target agent knows
            // it was dispatched by maf-lead rather than a human.
            var dispatchedTask =
                "[dispatched by maf-lead]\n\n" +
                "Task: " + task.Trim() + "\n\n" +
                "Do the work, then reply with a concise final summary that maf-lead can relay back to the user. " +
                "If you need more information, ask your question inside <clarify>...</clarify>.";

            var payload = new
            {
                threadId = threadId ?? $"dispatch-{Guid.NewGuid():N}",
                runId = $"run-{DateTime.Now:yyyyMMddHHmmssfff}",
                messages = new[]
                {
                    new { role = "user", content = dispatchedTask },
                },
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path)
            {
                Content = JsonContent.Create(payload),
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                return $"ERROR: upstream AG-UI HTTP {(int)resp.StatusCode} for {agent}: {err[..Math.Min(300, err.Length)]}";
            }

            var body = await resp.Content.ReadAsStringAsync();
            return ExtractFinalAssistantText(body);
        }
        catch (Exception ex)
        {
            return $"ERROR: dispatch failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Parse an AG-UI SSE response (stream of `data: {...}` JSON-RPC events)
    /// and concatenate the final assistant text content. Any tool calls are
    /// ignored — maf-lead does not need to relay them.
    /// </summary>
    private static string ExtractFinalAssistantText(string body)
    {
        var sb = new StringBuilder();
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var payload = line["data: ".Length..].Trim();
            if (string.IsNullOrEmpty(payload)) continue;
            try
            {
                var evt = JsonSerializer.Deserialize<JsonElement>(payload);
                if (evt.TryGetProperty("type", out var t)
                    && string.Equals(t.GetString(), "TEXT_MESSAGE_CONTENT", StringComparison.Ordinal)
                    && evt.TryGetProperty("delta", out var d)
                    && d.ValueKind == JsonValueKind.String)
                {
                    sb.Append(d.GetString());
                }
            }
            catch
            {
                // ignore malformed JSON lines (SSE comments, etc.)
            }
        }
        return sb.Length == 0 ? "[empty response from agent]" : sb.ToString();
    }
}

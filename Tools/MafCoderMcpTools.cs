// MafCoderMcpTools.cs — MCP-aware tools for maf-coder. Same pattern as
// MafSpecMcpTools but for the things maf-coder needs: openspec validation
// and PR listing. The MCP server (DashboardTools) already exposes the
// matching tools; these methods just forward JSON-RPC calls.

using System.ComponentModel;
using MafMiniMaxAgent.Mcp;
using SCDesc = System.ComponentModel.DescriptionAttribute;

namespace MafMiniMaxAgent.Tools;

public static class MafCoderMcpTools
{
    /// <summary>
    /// List OpenSpec changes in the project. Useful to see what's currently
    /// in flight before starting or extending one.
    /// </summary>
    [SCDesc("List OpenSpec changes in the project. Returns one line per change with its name, status, and which artifacts (proposal/design/tasks/specs) exist. Use this before opening or extending a change.")]
    public static async Task<string> McpListChangesAsync()
        => await McpClientHelper.CallToolAsync("list_changes", new { });

    /// <summary>
    /// Run `openspec validate --changes` and return stdout/stderr/exitcode.
    /// </summary>
    [SCDesc("Run 'openspec validate --changes' against the project. Returns exit code, stdout, stderr. Use this after editing change artifacts to make sure they are valid.")]
    public static async Task<string> McpValidateChangesAsync()
        => await McpClientHelper.CallToolAsync("validate_changes", new { });

    /// <summary>
    /// List GitHub pull requests. Defaults to GITHUB_DEFAULT_REPO if repo is omitted.
    /// </summary>
    [SCDesc("List GitHub pull requests for a repo. Defaults to GITHUB_DEFAULT_REPO env if repo is omitted. Useful to check open PRs before opening a new one.")]
    public static async Task<string> McpListPullRequestsAsync(
        [SCDesc("Optional 'owner/repo' slug. Defaults to the GITHUB_DEFAULT_REPO env var.")] string? repo = null,
        [SCDesc("Optional state filter: 'open' | 'closed' | 'all'. Defaults to 'open'.")] string state = "open",
        [SCDesc("Max number of PRs to return. Defaults to 30.")] int limit = 30)
    {
        var compact = new Dictionary<string, object?>
        {
            ["repo"] = repo,
            ["state"] = state,
            ["limit"] = limit,
        };
        return await McpClientHelper.CallToolAsync("list_pull_requests",
            compact.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
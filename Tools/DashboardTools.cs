// MCP tools that wrap the agent-dashboard HTTP API.
// Each tool does a HTTP request to localhost:3001 and returns the response as JSON.
//
// The dashboard runs on AGUI_BASE_URL (3001). All endpoints are described in
// agent-dashboard/server/api/.

using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
// MCP C# SDK exports its own [Description] attribute that shadows
// System.ComponentModel.DescriptionAttribute. The MCP one is what we want for
// tool-parameter descriptions; using the fully-qualified name everywhere below
// makes intent explicit and avoids CS1614 ambiguity.
// Alias the System.ComponentModel one to a shorter local name for clarity.
using SCDesc = System.ComponentModel.DescriptionAttribute;
// C# attribute usage allows either SCDesc(...) or SCDescAttribute(...). Use the
// short form everywhere below. The MCP SDK is in scope via ModelContextProtocol.Server
// and provides its own DescriptionAttribute that we shadow with this alias to
// avoid CS1614 ambiguity on every parameter.

namespace MafMiniMaxAgent.Mcp;

/// <summary>
/// Tools that let MAF agents read and write the kanban, run OpenSpec validation,
/// and search GitHub PRs by delegating to the agent-dashboard HTTP API.
/// Add this class to the MCP server via:
///   builder.Services
///     .AddMcpServer()
///     .WithHttpTransport()
///     .WithToolsFromAssembly();   // picks up [McpServerTool] methods on this type
/// </summary>
[McpServerToolType]
public class DashboardTools
{
    private readonly HttpClient _http;
    private readonly ILogger<DashboardTools> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public DashboardTools(IHttpClientFactory factory, ILogger<DashboardTools> log)
    {
        _http = factory.CreateClient("dashboard");
        // The dashboard runs locally; HttpClient base address is set in DI.
        _log = log;
    }

    // ----- Kanban --------------------------------------------------------

    [SCDesc("List all kanban boards in the dashboard.")]
    [McpServerTool(Name = "list_boards")]
    public async Task<string> ListBoards()
    {
        var boards = await _http.GetFromJsonAsync<List<BoardSummary>>("/api/boards", JsonOpts);
        return JsonSerializer.Serialize(boards, JsonOpts);
    }

    [SCDesc("List tickets of a board. Optionally filter by columnId (UUID).")]
    [McpServerTool(Name = "list_tickets")]
    public async Task<string> ListTickets(
        [SCDesc("Board UUID (required)")] string boardId,
        [SCDesc("Optional column UUID; if omitted, returns all columns with their tickets")] string? columnId = null)
    {
        var board = await _http.GetFromJsonAsync<BoardDetail>(
            $"/api/boards/{boardId}", JsonOpts);
        if (columnId is not null)
        {
            if (board is null) return $"No board with id {boardId}";
            var col = board.Columns?.FirstOrDefault(c => string.Equals(c.Id, columnId, StringComparison.Ordinal));
            return col is null
                ? $"No column with id {columnId} in board {boardId}"
                : JsonSerializer.Serialize(col, JsonOpts);
        }
        return JsonSerializer.Serialize(board, JsonOpts);
    }

    [SCDesc("Create a new ticket in a column. Returns the created ticket.")]
    [McpServerTool(Name = "create_ticket")]
    public async Task<string> CreateTicket(
        [SCDesc("Column UUID where the ticket will be created")] string columnId,
        [SCDesc("Ticket title (1-200 chars)")] string title,
        [SCDesc("Optional description")] string? description = null,
        [SCDesc("Priority: low | medium | high | urgent (default medium)")] string? priority = null,
        [SCDesc("Optional due date in YYYY-MM-DD format")] string? dueDate = null,
        [SCDesc("Optional GitHub issue URL")] string? githubIssueUrl = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
        };
        if (description is not null) body["description"] = description;
        if (priority is not null) body["priority"] = priority;
        if (dueDate is not null) body["due_date"] = dueDate;
        if (githubIssueUrl is not null) body["github_issue_url"] = githubIssueUrl;

        var resp = await _http.PostAsJsonAsync($"/api/columns/{columnId}/tickets", body, JsonOpts);
        resp.EnsureSuccessStatusCode();
        var ticket = await resp.Content.ReadFromJsonAsync<Ticket>(JsonOpts);
        return JsonSerializer.Serialize(ticket, JsonOpts);
    }

    [SCDesc("Move a ticket to another column at a specific position. Useful to mark tickets as Done after a merge.")]
    [McpServerTool(Name = "move_ticket")]
    public async Task<string> MoveTicket(
        [SCDesc("Ticket UUID to move")] string ticketId,
        [SCDesc("Destination column UUID")] string columnId,
        [SCDesc("Position in the destination column (0 = top)")] int position = 0)
    {
        var body = new { column_id = columnId, position };
        var resp = await _http.PatchAsJsonAsync($"/api/tickets/{ticketId}", body, JsonOpts);
        resp.EnsureSuccessStatusCode();
        var ticket = await resp.Content.ReadFromJsonAsync<Ticket>(JsonOpts);
        return JsonSerializer.Serialize(ticket, JsonOpts);
    }

    // ----- OpenSpec ------------------------------------------------------

    [SCDesc("Run 'openspec validate --changes' against the configured OpenSpec repo and return stdout/stderr/exit code.")]
    [McpServerTool(Name = "validate_changes")]
    public async Task<string> ValidateChanges()
    {
        var resp = await _http.PostAsync("/api/specs/validate", content: null);
        var body = await resp.Content.ReadFromJsonAsync<ValidateResult>(JsonOpts);
        return JsonSerializer.Serialize(body, JsonOpts);
    }

    [SCDesc("List OpenSpec changes (artifacts in openspec/changes/) and their artifacts (proposal/tasks/specs).")]
    [McpServerTool(Name = "list_changes")]
    public async Task<string> ListChanges()
    {
        var summary = await _http.GetFromJsonAsync<OpenSpecSummary>("/api/specs", JsonOpts);
        return JsonSerializer.Serialize(summary, JsonOpts);
    }

    // ----- GitHub PRs ---------------------------------------------------

    [SCDesc("List GitHub PRs for a repo. Defaults to GITHUB_DEFAULT_REPO from the dashboard env.")]
    [McpServerTool(Name = "list_pull_requests")]
    public async Task<string> ListPullRequests(
        [SCDesc("owner/name. If omitted, uses the dashboard's default repo")] string? repo = null,
        [SCDesc("open | closed | merged | all")] string state = "open",
        [SCDesc("Max number of PRs (1-100)")] int limit = 20)
    {
        var qs = $"?state={state}&limit={limit}";
        if (repo is not null) qs += $"&repo={Uri.EscapeDataString(repo)}";
        var prs = await _http.GetFromJsonAsync<List<PullRequest>>($"/api/prs{qs}", JsonOpts);
        return JsonSerializer.Serialize(prs, JsonOpts);
    }

    // ----- DTOs ---------------------------------------------------------

    public record BoardSummary(string Id, string Name, string? Repo);

    public record BoardDetail(string Id, string Name, string? Repo, List<ColumnDetail> Columns);

    public record ColumnDetail(string Id, string Name, int Position, string? Color, List<Ticket> Tickets);

    public record Ticket(
        string Id,
        string ColumnId,
        string Title,
        string? Description,
        int Position,
        string Priority,
        string? DueDate,
        string? GithubIssueUrl,
        string? GithubPrUrl);

    public record ValidateResult(string Stdout, string Stderr, int ExitCode);

    public record OpenSpecSummary(string Root, List<ChangeSummary> Changes, List<object> Specs);

    public record ChangeSummary(
        string Name,
        string Status,
        bool HasProposal,
        bool HasDesign,
        bool HasTasks,
        bool HasSpecs,
        bool? SkipSpecs,
        string? Goal);

    public record PullRequest(
        int Number,
        string Title,
        string State,
        string Url,
        string HeadRefName,
        string BaseRefName,
        Author Author,
        bool IsDraft);

    public record Author(string Login);
}
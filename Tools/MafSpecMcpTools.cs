// MafSpecMcpTools.cs — MAF-native tools that delegate to the dashboard via
// the local MCP server. These are exposed to maf-spec only.
//
// Rationale: file operations stay MAF-native (open_spec, write_change_file,
// read_change_file in Program.cs) because they need zero round-trip. But
// anything that touches shared state in the dashboard (kanban tickets,
// PRs, validation status) goes through MCP so maf-spec and the UI see the
// same data without a parallel HTTP wrapper.

using System.ComponentModel;
using System.Text.Json;
using MafMiniMaxAgent.Mcp;
using SCDesc = System.ComponentModel.DescriptionAttribute;

namespace MafMiniMaxAgent.Tools;

public static class MafSpecMcpTools
{
    // --- list_boards ---------------------------------------------------------
    /// <summary>
    /// List kanban boards in the dashboard. Use to discover the board id
    /// before creating tickets or moving them.
    /// </summary>
    [SCDesc("List kanban boards in the dashboard. Returns one line per board with its id and name. Call this BEFORE create_ticket so you know the target board and column ids.")]
    public static async Task<string> McpListBoardsAsync()
        => await McpClientHelper.CallToolAsync("list_boards", new { });

    // --- list_tickets --------------------------------------------------------
    /// <summary>
    /// List tickets in a board (optionally a single column).
    /// </summary>
    [SCDesc("List tickets of a kanban board. Optionally filter by columnId to narrow to one column. Returns one line per ticket with its id, column id, order, title, priority.")]
    public static async Task<string> McpListTicketsAsync(
        [SCDesc("UUID of the board to inspect.")] string boardId,
        [SCDesc("Optional UUID of a column to filter to. Omit or pass null to list all columns.")] string? columnId = null)
        => await McpClientHelper.CallToolAsync("list_tickets", new { boardId, columnId });

    // --- create_ticket -------------------------------------------------------
    /// <summary>
    /// Create a new ticket in a column.
    /// </summary>
    [SCDesc("Create a new ticket in a kanban column. Returns the created ticket (with its id and current position).")]
    public static async Task<string> McpCreateTicketAsync(
        [SCDesc("UUID of the column where the new ticket will be placed. Get it from list_boards + list_tickets.")] string columnId,
        [SCDesc("Short imperative title for the ticket, max 200 chars.")] string title,
        [SCDesc("Optional markdown description of the work to do.")] string? description = null,
        [SCDesc("Optional priority: 'low' | 'medium' | 'high' | 'urgent'. Defaults to 'medium'.")] string? priority = null)
    {
        var args = new Dictionary<string, object?>
        {
            ["columnId"] = columnId,
            ["title"] = title,
            ["description"] = description,
            ["priority"] = priority,
        };
        // Strip nulls so the optional fields are not sent as null (the server
        // schema treats null as 'set the default', which is fine, but we
        // prefer not sending them at all).
        var compact = args.Where(kv => kv.Value is not null)
                         .ToDictionary(kv => kv.Key, kv => kv.Value);
        return await McpClientHelper.CallToolAsync("create_ticket", compact);
    }

    // --- move_ticket ---------------------------------------------------------
    /// <summary>
    /// Move an existing ticket to a different column (and optional position).
    /// </summary>
    [SCDesc("Move a ticket to another column. Useful after a code task finishes to push the work forward in the kanban.")]
    public static async Task<string> McpMoveTicketAsync(
        [SCDesc("UUID of the ticket to move.")] string ticketId,
        [SCDesc("UUID of the destination column.")] string destinationColumnId,
        [SCDesc("Optional 0-based position inside the destination column. Omit to append at the end.")] int? position = null)
    {
        var compact = new Dictionary<string, object?>
        {
            ["ticketId"] = ticketId,
            ["destinationColumnId"] = destinationColumnId,
            ["position"] = position,
        };
        return await McpClientHelper.CallToolAsync("move_ticket",
            compact.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
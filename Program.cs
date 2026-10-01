using System.ClientModel;
using System.ComponentModel;
using System.IO;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using OpenAI;

// ---------------------------------------------------------------------------
// Load .env BEFORE building configuration
// ---------------------------------------------------------------------------
Env.Load();

// ---------------------------------------------------------------------------
// MafMiniMaxAgent — POC multi-agent
//
// Two agents in this iteration:
//   - maf-lead   : orchestrator. Plain chat. Receives user request.
//   - maf-spec   : OpenSpec specialist. Wraps the `openspec` CLI as a tool.
// ---------------------------------------------------------------------------

const string LeadName = "maf-lead";
const string SpecName = "maf-spec";

const string LeadInstructions =
    "You are maf-lead, the orchestrator of a multi-agent dev team. " +
    "In this POC you do not yet dispatch to other agents — answer the user directly, " +
    "in their language, concisely. If they ask about OpenSpec specs/changes, " +
    "suggest they address maf-spec explicitly (it is wired separately).";

const string SpecInstructions =
    "You are maf-spec, the OpenSpec specialist. " +
    "Use the open_spec tool to inspect and scaffold the project's openspec/ folder. " +
    "Rules: " +
    "  - List current state before proposing changes. " +
    "  - Use kebab-case for change names. " +
    "  - Specs MUST use the SHALL/SHOULD/MAY normative vocabulary. " +
    "  - Capability spec files under openspec/specs/<capability>/spec.md are source of truth: prefer the tool, but you may also hand-edit them if needed. " +
    "  - Change metadata files (.openspec.yaml, README.md) and proposals (proposal.md, design.md, tasks.md) may be hand-edited when the openspec CLI doesn't expose the needed action (e.g. setting skip_specs: true in .openspec.yaml). Always run 'validate --changes' after such edits. " +
    "  - Match the user's language. " +
    "  - Keep responses under 30 lines unless quoting.";

var builder = WebApplication.CreateBuilder(args);

var apiKey = builder.Configuration["MiniMax:ApiKey"]
    ?? throw new InvalidOperationException("MiniMax:ApiKey missing");
var endpoint = builder.Configuration["MiniMax:Endpoint"] ?? "https://api.minimaxi.chat/v1";
var modelId = builder.Configuration["MiniMax:ModelId"] ?? "MiniMax-M3";

builder.Services.AddChatClient(_ =>
    new OpenAIClient(new ApiKeyCredential(apiKey),
                     new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
        .GetChatClient(modelId)
        .AsIChatClient());

// HttpClientFactory for tools that call the dashboard API (and for MCP tools).
builder.Services.AddHttpClient("dashboard", (sp, client) =>
{
    // The dashboard URL is the same as the AG-UI host (different path).
    var cfg = sp.GetRequiredService<IConfiguration>();
    var aguiBase = cfg["MiniMax:Endpoint"]; // unused, just to ensure config is loaded
    var dashBase = Environment.GetEnvironmentVariable("DASHBOARD_BASE_URL") ?? "http://127.0.0.1:3001";
    client.BaseAddress = new Uri(dashBase);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// --- Tools -----------------------------------------------------------------
// open_spec : wraps the openspec CLI (read-only by default for the agent)
static string OpenSpecCli([Description("Args passed to the openspec CLI. " +
    "Examples: 'list', 'list --specs', 'view', 'change show <name>', 'validate'.")] string args)
{
    try
    {
        var openspecBin = "/home/gmagnier/.hermes/cache/scratch/bin/openspec";
        if (!System.IO.File.Exists(openspecBin))
            return "ERROR: openspec CLI not found at " + openspecBin;

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = openspecBin,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        if (!proc.WaitForExit(15_000)) { proc.Kill(); return "ERROR: openspec CLI timed out after 15s"; }
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        return proc.ExitCode == 0
            ? (string.IsNullOrWhiteSpace(stdout) ? "(no output)" : stdout.Trim())
            : $"EXIT {proc.ExitCode}\nSTDOUT: {stdout}\nSTDERR: {stderr}";
    }
    catch (Exception ex) { return "ERROR: " + ex.Message; }
}

// write_change_file : hand-edit a file inside openspec/<relative>/.
// SCOPED: refuses any path that escapes openspec/ (path-traversal guard).
static string WriteChangeFile(
    [Description("Relative path under openspec/, e.g. 'changes/my-change/.openspec.yaml' or 'changes/my-change/proposal.md'. MUST start with 'changes/' or 'specs/'. Must NOT start with '/' or contain '..'.")] string relativePath,
    [Description("New file contents (overwrites the file).")] string content)
{
    try
    {
        // Resolve project root = CWD (the host is launched from the repo root)
        var projectRoot = Directory.GetCurrentDirectory();
        var openspecRoot = Path.GetFullPath(Path.Combine(projectRoot, "openspec"));
        var target = Path.GetFullPath(Path.Combine(openspecRoot, relativePath));

        // Path-traversal guard
        if (!target.StartsWith(openspecRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(target, openspecRoot, StringComparison.Ordinal))
        {
            return $"ERROR: path '{relativePath}' escapes openspec/ — refused";
        }

        // Refuse to write into openspec/specs/<cap>/spec.md (capability specs are source of truth,
        // managed only via openspec CLI or human curation, not by this tool)
        var relToOpenspec = Path.GetRelativePath(openspecRoot, target);
        var parts = relToOpenspec.Split(Path.DirectorySeparatorChar);
        if (parts.Length >= 2 && string.Equals(parts[0], "specs", StringComparison.Ordinal))
            return $"ERROR: writing to openspec/specs/{parts[1]}/spec.md is not allowed via this tool. Capability specs must be edited directly or via the openspec CLI.";

        // Audit log
        var auditLog = "/home/gmagnier/.hermes/cache/scratch/maf-logs/spec-edits.log";
        Directory.CreateDirectory(Path.GetDirectoryName(auditLog)!);
        File.AppendAllText(auditLog,
            $"[{DateTime.UtcNow:O}] maf-spec wrote {relToOpenspec} ({content.Length} bytes)\n");

        // Make sure parent dir exists
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);

        File.WriteAllText(target, content);
        return $"OK: wrote {relToOpenspec} ({content.Length} bytes)";
    }
    catch (Exception ex) { return "ERROR: " + ex.Message; }
}

// read_change_file : read any file under openspec/ (for context)
static string ReadChangeFile(
    [Description("Relative path under openspec/, e.g. 'specs/leads/spec.md' or 'changes/init-aspnet-core-rest-api-base/proposal.md'.")] string relativePath)
{
    try
    {
        var projectRoot = Directory.GetCurrentDirectory();
        var openspecRoot = Path.GetFullPath(Path.Combine(projectRoot, "openspec"));
        var target = Path.GetFullPath(Path.Combine(openspecRoot, relativePath));

        if (!target.StartsWith(openspecRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(target, openspecRoot, StringComparison.Ordinal))
        {
            return $"ERROR: path '{relativePath}' escapes openspec/ — refused";
        }

        if (!File.Exists(target)) return $"ERROR: file not found: {relativePath}";
        return File.ReadAllText(target);
    }
    catch (Exception ex) { return "ERROR: " + ex.Message; }
}

// --- Register agents -------------------------------------------------------
builder.AddAIAgent(LeadName, LeadInstructions);

builder.AddAIAgent(SpecName, SpecInstructions)
       .WithAITool(AIFunctionFactory.Create(OpenSpecCli, name: "open_spec"))
       .WithAITool(AIFunctionFactory.Create(WriteChangeFile, name: "write_change_file"))
       .WithAITool(AIFunctionFactory.Create(ReadChangeFile, name: "read_change_file"));

// --- DevUI (development only) ---------------------------------------------
if (builder.Environment.IsDevelopment())
{
    builder.AddOpenAIResponses();
    builder.AddOpenAIConversations();
    builder.AddDevUI(o => o.AllowRemoteAccess = true);
}

builder.Services.AddAGUIServer();

// MCP server: streamable HTTP transport at /mcp, picking up DashboardTools.
// Other tools (open_spec, write_change_file, read_change_file) stay MAF-native
// because they operate on local files; the MCP layer delegates to the dashboard
// over HTTP for the things only the dashboard owns (kanban state).
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "agent-dashboard-mcp", Version = "0.1.0" };
    })
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenAIResponses();
    app.MapOpenAIConversations();
    app.MapDevUI();
}

// maf-lead exposed via AG-UI for interactive testing
var leadAgent = app.Services.GetRequiredKeyedService<AIAgent>(LeadName);
app.MapAGUIServer("/ag-ui", leadAgent);

// MCP server at /mcp — picked up from MapMcp() (provided by WithHttpTransport).
app.MapMcp("/mcp");

app.MapGet("/", () => Results.Json(new
{
    name = "MafMiniMaxAgent (POC multi-agent)",
    model = modelId,
    agents = new[] { LeadName, SpecName },
    endpoints = new
    {
        devui = app.Environment.IsDevelopment() ? "/devui" : null,
        agui = "/ag-ui",
    },
}));

// Top-level statements can't await directly; suppress CS1998 by storing the task.
_ = app.RunAsync();

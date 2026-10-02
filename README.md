# MafMiniMaxAgent

A .NET 10 host that runs a small **team of [Microsoft Agent Framework (MAF)](https://learn.microsoft.com/en-us/agent-framework/) agents** powered by the **MiniMax-M3** model. Agents are exposed through three surfaces and one Model Context Protocol server.

| Surface        | When            | URL prefix  | Purpose                                                                          |
| -------------- | --------------- | ------------------- | -------------------------------------------------------------------------------- |
| **DevUI**      | `Development`   | `/devui`            | In-browser chat playground + OpenAI Responses/Conversations API                  |
| **AG-UI**      | always          | `/ag-ui`            | maf-lead (the orchestrator)                                                      |
| **AG-UI**      | always          | `/ag-ui/spec`       | maf-spec (specs / kanban)                                                        |
| **AG-UI**      | always          | `/ag-ui/coder`      | maf-coder (code implementation, autonomous PR review)                            |
| **MCP**        | always          | `/mcp`              | Model Context Protocol server (10 tools; consumed by the agents and by clients) |

The MiniMax API is OpenAI-compatible, so we wire it up with the standard `OpenAIClient` pointed at `https://api.minimaxi.chat/v1`.

---

## The agent team

| Agent         | Role                                        | Has these tools                                                                                                       |
| ------------- | ------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `maf-lead`    | Orchestrator. Routes work to specialists.   | `delegate_to_agent` (HTTP POST to `/ag-ui/spec` or `/ag-ui/coder`)                                                     |
| `maf-spec`    | OpenSpec specialist + kanban.               | `open_spec`, `write_change_file`, `read_change_file`, `mcp_list_boards`, `mcp_list_tickets`, `mcp_create_ticket`, `mcp_move_ticket`, `mcp_chat_*` |
| `maf-coder`   | Code implementation + autonomous PR review.| `read_file`, `write_file`, `apply_patch`, `shell_command` (allowlist), `mcp_list_changes`, `mcp_validate_changes`, `mcp_list_pull_requests` |

`maf-coder` operates on the local filesystem with a strict scope:
- Allowed roots: `openspec/`, `Tools/`.
- Allowed top-level files: `README.md`, `AGENTS.md`.
- `shell_command` is allowlisted (`dotnet`, `git`, `gh`, `node`, `npx`); `git push` is permitted **only** to non-protected branches (anything other than `main`/`master`/`develop`/`release`/`trunk`).
- `git reset`, `git checkout`, `git clean`, `git branch`, `git rebase`, `git merge` are always blocked.

---

## MCP server

Streamable-HTTP transport at `/mcp`. Tools exposed by `Tools/DashboardTools.cs`:

| Tool                       | What                                                                            |
| -------------------------- | ------------------------------------------------------------------------------- |
| `list_boards`              | List kanban boards in the dashboard.                                            |
| `list_tickets`             | Tickets in a board (optionally a single column).                               |
| `create_ticket`            | Create a ticket in a column.                                                    |
| `move_ticket`              | Move a ticket to another column.                                                |
| `list_changes`             | List OpenSpec changes on the local project.                                    |
| `validate_changes`         | Run `openspec validate --changes`.                                              |
| `list_pull_requests`       | List GitHub PRs for a repo.                                                     |
| `chat_list_threads`        | List chat threads for an agent.                                                 |
| `chat_save_message`        | Save a message to a chat thread.                                                |
| `chat_create_thread`       | Create a new chat thread.                                                       |

All tools delegate to the agent-dashboard Nuxt API at `http://127.0.0.1:3001`.

---

## Prerequisites

- **.NET 10 SDK** (`dotnet --version`)
- A **MiniMax API key** (request one from your MiniMax account dashboard)
- *(optional)* Node.js ≥ 18 if you want to point the agent-dashboard UI at the server

---

## Setup

```bash
cd MiniMax-Maf-Host
cp .env.example .env
# edit .env and set MiniMax:ApiKey=<your-key>
```

`appsettings.json` carries the non-secret defaults (`Endpoint`, `ModelId`). The key is **never** read from source-controlled files — `.env` is loaded at process start, and `.env` is gitignored.

---

## Run

```bash
# Dev (port 5014)
dotnet run --urls http://0.0.0.0:5014

# Prod (port 5016) — same binary, different port
dotnet run --urls http://0.0.0.0:5016
```

| URL                                      | What you get                                                                  |
| ---------------------------------------- | ----------------------------------------------------------------------------- |
| `http://localhost:5014/`                 | JSON landing page listing the available endpoints                             |
| `http://localhost:5014/devui`            | DevUI dashboard — chat with any agent in your browser                        |
| `http://localhost:5014/ag-ui`            | maf-lead over AG-UI                                                            |
| `http://localhost:5014/ag-ui/spec`       | maf-spec over AG-UI                                                            |
| `http://localhost:5014/ag-ui/coder`      | maf-coder over AG-UI                                                          |
| `http://localhost:5014/mcp`              | MCP streamable-HTTP server (POST JSON-RPC, SSE out)                          |

### Talk to an AG-UI endpoint with curl

```bash
curl -N http://localhost:5014/ag-ui/spec \
  -H "Content-Type: application/json" \
  -d '{
    "threadId": "demo-thread",
    "runId": "run-1",
    "messages": [
      { "role": "user", "content": "Liste les boards du kanban." }
    ]
  }'
```

---

## Configuration reference

| Key                       | Default                          | Notes                                                  |
| ------------------------- | -------------------------------- | ------------------------------------------------------ |
| `MiniMax:ApiKey`          | *(required)*                     | from `.env` / shell env / user-secrets                 |
| `MiniMax:Endpoint`        | `https://api.minimaxi.chat/v1`   | any OpenAI-compatible base URL                         |
| `MiniMax:ModelId`         | `MiniMax-M3`                     | `MiniMax-M3`, `MiniMax-M2`, etc.                      |
| `MAF_AGUI_BASE_URL`       | `http://127.0.0.1:5014`          | where `maf-lead.delegate_to_agent` forwards HTTP       |
| `MAF_MCP_URL`             | `http://127.0.0.1:5014/mcp`      | where MAF agents read MCP tools                         |
| `MAF_MCP_PORT`            | `5014`                           | alternate: base port when MAF_MCP_URL is not set       |
| `DASHBOARD_INTERNAL_URL`  | `http://127.0.0.1:3001`          | read by the webhook handler to talk to the dashboard    |

---

## Project layout

```
MiniMax-Maf-Host/
├── MafMiniMaxAgent.csproj           # net10.0, MAF + analyzers (Directory.Packages.props)
├── Program.cs                        # 3 agents + AG-UI + MCP wiring
├── Tools/
│   ├── DashboardTools.cs             # 10 MCP tools → dashboard API
│   ├── MafSpecMcpTools.cs            # MAF-native wrappers that call MCP
│   ├── MafCoderTools.cs              # file ops + shell allowlist (scoped)
│   ├── MafCoderMcpTools.cs           # MAF-native wrappers for coder MCP
│   ├── MafLeadDispatcher.cs          # delegate_to_agent (synchronous HTTP POST)
│   ├── McpClientHelper.cs            # tiny JSON-RPC HTTP client for MCP
│   └── StringExtensions.cs
├── Directory.Packages.props          # Central Package Management for app packages
├── Directory.Build.props / .editorconfig
├── .env.example
├── appsettings.json
├── start-maf.sh                      # starts dev (5014) + prod (5016) in nohup
└── openspec/                         # OpenSpec changes + specs
```

---

## Packages

Centralized in `Directory.Packages.props`; analyzers in the `.csproj` (CPM does not cover `GlobalPackageReference`):

- `Microsoft.Agents.AI.*` 1.22.0 (preview for Hosting / DevUI / AGUI.AspNetCore)
- `ModelContextProtocol` 0.4.0-preview.1
- `DotNetEnv` for `.env` loading
- `better-sqlite3` not used here (the dashboard owns its own SQLite for persistence)

### Roslyn analyzers (build-time)

In `MafMiniMaxAgent.csproj` as `GlobalPackageReference`:

| Analyzer                          | Latest       | Purpose                                       |
| --------------------------------- | ------------ | --------------------------------------------- |
| Nullable.Extended.Analyzer        | 1.16.6891    | Deeper nullable reference analysis              |
| SonarAnalyzer.CSharp              | 10.35.0.4138 | Bugs, smells, security hotspots               |
| Meziantou.Analyzer                | 3.0.290      | .NET conventions, IDisposable, formatting     |
| Microsoft.CodeAnalysis.BannedApi  | 5.6.0        | Bans APIs declared in `.editorconfig`         |
| Roslynator.Analyzers               | 5.0.0        | RCS/IDE refactorings                            |
| SmartanAlyzers.ExceptionAnalyzer  | 1.0.10       | Exception advice                               |

Build is `TreatWarningsAsErrors=true`. The `.editorconfig` ships with ~30 per-rule suppressions for patterns that have a low signal-to-noise ratio on top-level statements and MCP DTOs.

---

## Going to production

- `start-maf.sh start both` runs **two** instances on 5014 (dev profile) and 5016 (prod profile, no DevUI) from two separate clones (`MiniMax-Maf-Host` and `MiniMax-Maf-Host-prod`). Logs go to `~/.hermes/cache/scratch/maf-logs/{dev,prod}.log`.
- The DevUI dashboard is **only** mapped under `IsDevelopment()`.
- Inject `MiniMax:ApiKey` from your platform's secret store; never bake it into the image.
- The MCP server and AG-UI endpoints stay exposed; gate them with your ingress / auth layer.
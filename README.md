# MafMiniMaxAgent

A minimal C# / .NET 8 host that runs a [Microsoft Agent Framework (MAF)](https://learn.microsoft.com/en-us/agent-framework/) agent powered by the **MiniMax-M3** model, exposed through two surfaces:

| Surface        | When            | URL prefix | Purpose                                                                 |
| -------------- | --------------- | ---------- | ----------------------------------------------------------------------- |
| **DevUI**      | `Development`   | `/devui`   | In-browser chat playground + OpenAI Responses/Conversations API         |
| **AG-UI**      | always          | `/ag-ui`   | [AG-UI protocol](https://docs.ag-ui.com/) endpoint (HTTP POST + SSE)    |

The MiniMax API is OpenAI-compatible, so we wire it up with the standard `OpenAIClient` pointed at `https://api.minimaxi.chat/v1`.

---

## Prerequisites

- **.NET 8 SDK** or later (`dotnet --version`)
- A **MiniMax API key** (request one from your MiniMax account dashboard)
- *(optional)* Node.js ≥ 18 if you want to point a JS AG-UI client at the server

---

## Setup

```bash
cd MafMiniMaxAgent

# Store the secret OUTSIDE the repo (encrypted, per-user)
dotnet user-secrets set "MiniMax:ApiKey"    "<your-minimax-api-key>"
dotnet user-secrets set "MiniMax:Endpoint"  "https://api.minimaxi.chat/v1"
dotnet user-secrets set "MiniMax:ModelId"   "MiniMax-M3"

# Verify
dotnet user-secrets list
```

`appsettings.json` carries the non-secret defaults (`Endpoint`, `ModelId`). The key is **never** read from source-controlled files.

---

## Run

```bash
dotnet run
```

By default the launch profile binds to `http://localhost:5014`. Then:

| URL                                  | What you get                                                |
| ------------------------------------ | ----------------------------------------------------------- |
| `http://localhost:5014/`             | JSON landing page listing the available endpoints           |
| `http://localhost:5014/devui`        | DevUI dashboard — chat with the agent in your browser       |
| `http://localhost:5014/v1/responses` | OpenAI-compatible Responses endpoint (used by DevUI)        |
| `http://localhost:5014/ag-ui`        | AG-UI protocol endpoint — accepts `RunAgentInput`, SSE out |

### Talk to the AG-UI endpoint with curl

```bash
curl -N http://localhost:5014/ag-ui \
  -H "Content-Type: application/json" \
  -d '{
    "threadId": "demo-thread",
    "runId": "run-1",
    "messages": [
      { "role": "user", "content": "Bonjour, qui es-tu ?" }
    ]
  }'
```

---

## Configuration reference

All keys live under the `MiniMax` section (env-var form: `MiniMax__ApiKey`, etc.):

| Key               | Default                          | Notes                                       |
| ----------------- | -------------------------------- | ------------------------------------------- |
| `MiniMax:ApiKey`  | *(required)*                     | from user-secrets / env var                 |
| `MiniMax:Endpoint`| `https://api.minimaxi.chat/v1`   | any OpenAI-compatible base URL              |
| `MiniMax:ModelId` | `MiniMax-M3`                     | e.g. `MiniMax-M3`, `MiniMax-M2`, etc.       |

---

## Project layout

```
MafMiniMaxAgent/
├── MafMiniMaxAgent.csproj          # target net8.0, all MAF packages
├── Program.cs                       # chat-client + agent + DevUI + AG-UI wiring
├── appsettings.json                 # public defaults (no secrets)
├── appsettings.Development.json     # dev logging
└── .gitignore
```

---

## How the wiring works

`Program.cs` does four things:

1. **Registers an OpenAI-compatible chat client** that points at MiniMax:
   ```csharp
   builder.Services.AddChatClient(_ =>
       new OpenAIClient(new ApiKeyCredential(apiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
           .GetChatClient(modelId)
           .AsIChatClient());
   ```
2. **Registers a MAF agent** with a name and system instructions:
   ```csharp
   builder.AddAIAgent("maf-agent", "You are MafMiniMaxAgent, ...");
   ```
3. **Dev mode only** — adds the OpenAI Responses / Conversations services and maps the DevUI dashboard at `/devui`.
4. **Always** — maps the AG-UI server endpoint at `/ag-ui`, resolving the same keyed agent from DI.

---

## Packages

| Package                                                | Version                   | Why                                        |
| ------------------------------------------------------ | ------------------------- | ------------------------------------------ |
| `Microsoft.Agents.AI`                                  | `1.22.0`                  | Core MAF abstractions (`AIAgent`, …)       |
| `Microsoft.Agents.AI.OpenAI`                           | `1.22.0`                  | OpenAI chat-client bridge                  |
| `Microsoft.Agents.AI.Hosting`                          | `1.22.0-preview.260918.1` | `AddAIAgent` / `AddDevUI` extensions       |
| `Microsoft.Agents.AI.DevUI`                            | `1.22.0-preview.260918.1` | DevUI dashboard + Responses / Convos APIs  |
| `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`          | `1.22.0-preview.260918.1` | AG-UI server endpoint                      |

Preview versions are required because DevUI, Hosting, and AGUI.AspNetCore have not shipped a stable 1.x release yet.

---

## Going to production

When you `dotnet run -c Release` or deploy:

- The DevUI dashboard and its OpenAI Responses/Conversations endpoints are **not** mapped (gated by `IsDevelopment()`).
- Only the `/ag-ui` endpoint is exposed, so AG-UI-compatible frontends (e.g. the CopilotKit AG-UI client, custom Blazor clients, etc.) can talk to your agent over a single SSE-based protocol.
- Inject `MiniMax:ApiKey` from your platform's secret store (Azure App Service app-settings, Kubernetes secrets, etc.) — never bake it into the image.

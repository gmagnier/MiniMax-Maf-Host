using System.ClientModel;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using OpenAI;

// ---------------------------------------------------------------------------
// Load .env BEFORE building configuration so ASP.NET Core's default
// EnvironmentVariablesConfigurationProvider can pick up MiniMax__* values.
// Existing real process env vars always win over .env entries.
// Safe to call when no .env file is present (Env.Load is a no-op then).
Env.Load();

// ---------------------------------------------------------------------------
// MafMiniMaxAgent
// ASP.NET Core host that exposes a Microsoft Agent Framework (MAF) agent
// powered by the MiniMax-M3 model (OpenAI-compatible endpoint).
//
//   Dev  -> DevUI dashboard        : GET  /devui   (also /v1/* behind the scenes)
//   Prod -> AG-UI protocol server  : POST /ag-ui   (RunAgentInput -> SSE events)
//
// All sensitive configuration is read from .NET user-secrets / env vars,
// never from appsettings.json committed to the repo.
// ---------------------------------------------------------------------------

const string AgentName = "maf-agent";
const string AgentInstructions =
    "You are MafMiniMaxAgent, a concise and helpful assistant powered by MiniMax-M3. " +
    "Answer in the user's language. If you are unsure, say so.";

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration --------------------------------------------------------
var apiKey = builder.Configuration["MiniMax:ApiKey"]
    ?? throw new InvalidOperationException(
        "MiniMax:ApiKey is not configured. " +
        "Set it with: dotnet user-secrets set \"MiniMax:ApiKey\" \"<your-key>\"");

var endpoint = builder.Configuration["MiniMax:Endpoint"] ?? "https://api.minimaxi.chat/v1";
var modelId = builder.Configuration["MiniMax:ModelId"] ?? "MiniMax-M3";

// ---- Chat client (OpenAI-compatible, pointing at MiniMax) -----------------
builder.Services.AddChatClient(_ =>
{
    var openAIClient = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

    return openAIClient.GetChatClient(modelId).AsIChatClient();
});

// ---- Agent registration ---------------------------------------------------
// AddAIAgent(name, instructions) registers a keyed AIAgent in DI
// and also wires it up to the chat client registered above.
builder.AddAIAgent(AgentName, AgentInstructions);

// ---- DevUI (development only) --------------------------------------------
if (builder.Environment.IsDevelopment())
{
    // DevUI talks to the agent through these OpenAI-compatible endpoints.
    builder.AddOpenAIResponses();
    builder.AddOpenAIConversations();

    // Serves the in-browser DevUI dashboard at /devui.
    builder.AddDevUI();
}

// ---- AG-UI server (always available, used by frontends in prod) -----------
builder.Services.AddAGUIServer();

var app = builder.Build();

// ---- Map endpoints --------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.MapOpenAIResponses();
    app.MapOpenAIConversations();
    app.MapDevUI();   // /devui
}

// Resolve the agent registered by AddAIAgent(AgentName, ...) for the AG-UI route.
var aguiAgent = app.Services.GetRequiredKeyedService<AIAgent>(AgentName);
app.MapAGUIServer("/ag-ui", aguiAgent);

// Friendly landing page so `dotnet run` shows something useful.
app.MapGet("/", () => Results.Json(new
{
    name = "MafMiniMaxAgent",
    model = modelId,
    endpoints = new
    {
        devui = app.Environment.IsDevelopment() ? "/devui" : null,
        agui  = "/ag-ui",
    },
}));

app.Run();

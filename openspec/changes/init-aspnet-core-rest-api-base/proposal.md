## Why

The repository currently hosts an ASP.NET Core host (`Program.cs`, a `Microsoft.Agents.AI.*`-based agent wiring) but exposes no surface that matches the stated goal — a modern .NET 10 REST API ready to receive business features. New endpoints and middleware can't be added cleanly because there is no shared base: no controllers, no JSON configuration, no health check, no OpenAPI, no test project, no CI. Contributors wanting to add a first business endpoint must invent the conventions themselves, and the conventions drift change by change.

This change is pure scaffolding/tooling: it introduces the conventional REST API base (project layout, hosting, observability endpoints, contract publishing, test project) so that subsequent feature changes only have to declare behavior, not reinvent infrastructure.

## What Changes

- **Hosting**: keep `Microsoft.NET.Sdk.Web`, target `net10.0`, keep `Program.cs` minimal-API entry point with `WebApplication.CreateBuilder(args)`.
- **Routing**: enable controllers (`AddControllers`, `MapControllers`) so feature changes can drop in `[ApiController]` classes without re-plumbing.
- **JSON**: install `Microsoft.AspNetCore.Mvc.NewtonsoftJson` (or equivalent `System.Text.Json` defaults in .NET 10) and configure camelCase + ignore cycles, locked in by a small test.
- **Health**: add `MapGet("/health")` returning `200 OK` with a tiny JSON body, and `AddHealthChecks()` registered (the `/health` route reuses the default health pipeline).
- **OpenAPI**: register `Microsoft.AspNetCore.OpenApi` and `Swashbuckle.AspNetCore` (or the .NET 10 built-in OpenAPI generator if stable), expose `/swagger` in Development.
- **Error handling**: install `Hellang.Middleware.ProblemDetails` and wire it globally so all unhandled exceptions become RFC 7807 responses.
- **Test project**: add `MafMiniMaxAgent.Tests` (xUnit + `Microsoft.AspNetCore.Mvc.Testing`) with one smoke test hitting `/health`.
- **CI**: add a GitHub Actions workflow `dotnet.yml` running `restore`, `build`, `test` on push/PR.
- **Docs**: extend `README.md` with a short "Adding a feature endpoint" section pointing at this base.

## Capabilities

### New Capabilities
<!-- None — this change introduces no runtime behavior contract.
     Subsequent feature changes (auth, persistence, business endpoints) will add specs
     under their own capabilities. -->

### Modified Capabilities
<!-- None — no existing spec exists yet. -->

## Impact

- **Code**: additions only to `Program.cs` (new `Add…`/`Map…` calls), no breaking edits to existing lines.
- **Dependencies**: new NuGet refs — `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore`, `Hellang.Middleware.ProblemDetails`, plus the test project's `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`. None of these conflict with the existing `Microsoft.Agents.AI.*` 1.22.0 / preview set (verified during init commit).
- **New projects**: `MafMiniMaxAgent.Tests` added to the solution.
- **Repo files**: `.github/workflows/dotnet.yml`, updated `README.md`, updated `.gitignore` entries for typical .NET artifacts (none required — already covered).
- **Out of scope**: authentication, persistence, business endpoints, agent runtime wiring, production deployment manifests.

## Notes on `skip_specs`

This change is pure infrastructure/scaffolding: it introduces no observable runtime contract beyond `/health` returning 200, which is covered by the test project rather than a long-lived capability spec. Capability specs (e.g. `config`, `routing`, `error-handling`) will be introduced by the first feature change that exercises them with real behavior, not by this scaffolding step. Hence `skip_specs: true` is set deliberately.
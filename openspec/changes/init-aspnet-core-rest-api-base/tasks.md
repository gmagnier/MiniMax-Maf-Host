## 1. Project configuration

- [ ] 1.1 Bump `<TargetFramework>` to `net10.0` in `MafMiniMaxAgent.csproj` (keep `Microsoft.NET.Sdk.Web`) and verify `dotnet restore` succeeds
- [ ] 1.2 Add NuGet refs `Microsoft.AspNetCore.OpenApi` (matching .NET 10 preview) and `Swashbuckle.AspNetCore` 6.x; verify `dotnet build` succeeds with no transitive conflict against `Microsoft.Agents.AI.*` 1.22.0 / preview
- [ ] 1.3 Add NuGet ref `Hellang.Middleware.ProblemDetails` for RFC 7807 responses

## 2. `Program.cs` wiring

- [ ] 2.1 Add `builder.Services.AddControllers()` to `Program.cs` and `app.MapControllers()` after `app.Build()` so future feature changes can drop in `[ApiController]` classes without re-plumbing
- [ ] 2.2 Add `builder.Services.AddProblemDetails()` and `app.UseExceptionHandler()` / `app.UseStatusCodePages()` so unhandled exceptions become RFC 7807 responses
- [ ] 2.3 Add `builder.Services.AddEndpointsApiExplorer()`, `builder.Services.AddSwaggerGen()`, and `app.UseSwagger()` + `app.MapGet("/swagger", ...)` (or `app.UseSwaggerUI()`) guarded behind `IsDevelopment`
- [ ] 2.4 Add `builder.Services.AddHealthChecks()` and `app.MapHealthChecks("/health")` returning 200 OK with a small JSON payload
- [ ] 2.5 Configure JSON: camelCase property naming and `JsonSerializerOptions.DefaultIgnoreCondition = WhenWritingNull` (or the .NET 10 default equivalent), wired through `AddControllers().AddJsonOptions(...)` or the minimal-API equivalent

## 3. Test project

- [ ] 3.1 Create `MafMiniMaxAgent.Tests` project (xUnit, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `Microsoft.AspNetCore.Mvc.Testing`), add it to the solution file
- [ ] 3.2 Add a smoke test that uses `WebApplicationFactory<Program>` and asserts `GET /health` returns `200 OK`
- [ ] 3.3 Add a smoke test that asserts `GET /swagger` returns `200 OK` (Development profile only) and `GET /unknown` returns a `ProblemDetails` JSON payload with status 404

## 4. CI

- [ ] 4.1 Create `.github/workflows/dotnet.yml` running `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release` on `ubuntu-latest` for push and `pull_request` events
- [ ] 4.2 Cache NuGet packages (`actions/setup-dotnet@v4` built-in cache) to keep CI under 60 s

## 5. Docs

- [ ] 5.1 Add a "Adding a feature endpoint" section to `README.md`: open `Controllers/XController.cs`, add `[ApiController][Route("api/[controller]")]`, the route is auto-mapped, error responses follow RFC 7807

## 6. Verification

- [ ] 6.1 `dotnet run` then `curl -i http://localhost:5000/health` → `200 OK` with JSON body
- [ ] 6.2 `dotnet run` then `curl -i http://localhost:5000/unknown` → `404 Not Found` with a `ProblemDetails` JSON body (RFC 7807)
- [ ] 6.3 `dotnet test` → all tests green
- [ ] 6.4 Existing agent smoke (`/` landing page returns 200, `/devui` accepts a chat round-trip in Development) still passes — regression guard for the agent wiring that lived here before this change
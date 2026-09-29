## Context

The .NET 8 `WebApplication.CreateBuilder(args)` already wires up an `EnvironmentVariablesConfigurationProvider`. That provider reads process environment variables using the standard `Section__Key` → `Section:Key` mapping. So the only missing piece is "populate process env vars from a `.env` file at the very top of `Program.cs`". See `proposal.md` for the motivation behind moving away from a `dotnet user-secrets`-only workflow.

The project is a small ASP.NET Core 8 host with no existing capability specs. This change introduces the first one — `config`.

## Goals / Non-Goals

**Goals:**
- `.env` is the canonical local-dev path; a new contributor can run `cp .env.example .env && dotnet run`.
- Production deployments that inject env vars (Azure App Service, Kubernetes, shell `export`) keep working unchanged.
- Zero behavior change for contributors who stick with `dotnet user-secrets` — it remains a fallback.

**Non-Goals:**
- Replacing `dotnet user-secrets` outright.
- Migrating every future secret to `.env` in one go — only `MiniMax:ApiKey` is in scope here.
- Production-grade secret management (Vault, Key Vault, etc.) — that lives in the platform layer, not in the app.
- Any change to the agent's runtime behavior, endpoints, or wiring.

## Decisions

### Decision: Use the `DotNetEnv` NuGet package

**Why:** `.env` parsing has non-trivial edge cases (quoted values, `export` prefixes, comment lines, escaped newlines). Re-rolling that parser invites bugs. `DotNetEnv` is the de-facto .NET `.env` loader (~7M downloads, MIT, single small DLL, no native deps, no transitive conflicts with `Microsoft.Agents.AI.*` 1.22.0 / preview).

**Alternatives considered:**
- **Custom 20-line parser** reading `.env` line-by-line into `Environment.SetEnvironmentVariable`. Rejected: easy to get edge cases wrong, and the parsing question is solved well by an existing package.
- **Wait for .NET 9+ built-in `.env` support.** Not available in net8.0 (the project's `<TargetFramework>`).

### Decision: Call `DotNetEnv.Env.Load()` as the first statement in `Program.cs`

**Why:** It must run before `WebApplication.CreateBuilder(args)` so the env vars are visible to the default `EnvironmentVariablesConfigurationProvider`. `Env.Load()`'s default behavior is "do not overwrite existing process env vars", which is exactly the precedence the spec requires (real env vars > `.env` > appsettings).

**Alternatives considered:**
- **ASP.NET Core `IConfigurationBuilder.AddDotNetEnv()` extension** (a separate `DotNetEnv.Configuration` package) — would feed `.env` straight into `IConfiguration` without touching process env. Rejected: skips the precedence guarantee in the spec (a real `MiniMax__ApiKey` set in the shell would be shadowed by `.env` if the order of providers is wrong). The simpler "load into process env, let the default provider pick it up" model is harder to misuse.

### Decision: Env-var naming uses `Section__Key` (double underscore)

**Why:** This is the convention ASP.NET Core's `EnvironmentVariablesConfigurationProvider` already expects (`MiniMax:ApiKey` → `MiniMax__ApiKey`). Using it means zero mapping code — `builder.Configuration["MiniMax:ApiKey"]` keeps working as-is.

**Alternatives considered:**
- **`.env` keys like `MiniMax.ApiKey` with a custom mapper.** Rejected: adds code, breaks muscle memory for ASP.NET developers, and `.env` files conventionally use flat names anyway.

### Decision: `.env.example` ships with empty secret values, real defaults for non-secrets

**Why:** Secrets should never appear in a committed file (even example ones). Non-secret defaults (`Endpoint`, `ModelId`) are already public and belong in the template so contributors can see what to override.

## Risks / Trade-offs

- **Risk:** A contributor commits `.env` by accident. → **Mitigation:** `.env` is already in `.gitignore` (lines 6–7). `.env.example` is committed and uses safe placeholders only. CI could add a `git diff --exit-code -- .env` check later (out of scope here).
- **Risk:** `DotNetEnv` is abandoned or unmaintained. → **Mitigation:** it's a small, stable, MIT-licensed one-file parser; if it ever breaks, replacing it with a hand-rolled parser is a single-file change (`Program.cs`). The spec doesn't depend on the library choice.
- **Risk:** Two contributors on the same machine both load `.env` and one shadows the other. → **Mitigation:** `Env.Load()` only fills gaps; whichever contributor's shell session sets `MiniMax__ApiKey` explicitly always wins. This matches the spec's precedence requirement.

## Migration Plan

No migration needed. The change is additive:

1. Pull the change.
2. `cp .env.example .env`, fill in `MiniMax__ApiKey`.
3. `dotnet run` — should work identically to the old `dotnet user-secrets set … && dotnet run` flow.

Existing `dotnet user-secrets` users can ignore `.env` entirely; both paths continue to resolve `MiniMax:ApiKey` from their respective sources.

**Rollback:** revert the one `Program.cs` line and remove the `.env.example` file. No state to undo.

## Open Questions

None — all design-level decisions are resolved. The library choice can be flipped without touching the spec (see Risks).

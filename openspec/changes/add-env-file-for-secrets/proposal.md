## Why

The project already keeps secrets out of source control (the README documents `dotnet user-secrets`), but the developer experience is uneven: `MiniMax__ApiKey` must be set with a `dotnet user-secrets set …` command, which is unfamiliar to contributors coming from Node/Python stacks where a `.env` file is the norm. There is no committed template that tells a new contributor which environment variables exist, so onboarding has to read the source to find the keys. And future secrets (third-party API keys, webhook tokens, etc.) will need the same treatment — a single, conventional `.env` mechanism scales better than one-off `user-secrets` entries.

## What Changes

- `Program.cs` loads a `.env` file (if present) before the ASP.NET Core configuration is built.
- A committed `.env.example` template ships in the repo, listing every secret variable the app reads (starting with `MiniMax__ApiKey`).
- `.env` is already in `.gitignore`; no change there.
- `README.md` Setup section is updated to document `.env` as the preferred local-dev path; `dotnet user-secrets` is kept as a fallback (no breakage for existing setups).
- The existing `MiniMax:ApiKey` config key is unchanged, so production deployments that inject env vars (Azure App Service, Kubernetes, etc.) continue to work.

## Capabilities

### New Capabilities
- `config`: Externalized application configuration loaded from `.env` files at dev time, with platform env vars always winning. Covers the contract for `.env` loading precedence, the `.env.example` template, and the rule that secrets are never read from committed JSON files.

### Modified Capabilities
<!-- None — no existing spec exists yet. -->

## Impact

- **Code**: one new line at the top of `Program.cs` (`DotNetEnv.Env.Load();`).
- **Dependencies**: new NuGet package `DotNetEnv` (3.x, MIT, no transitive conflicts with the existing `Microsoft.Agents.AI.*` 1.22.0 / preview set).
- **Docs**: `README.md` Setup section rewritten to lead with `.env`; one short paragraph noting `dotnet user-secrets` still works.
- **Repo files**: new `.env.example` (committed). `.env` already ignored.
- **Out of scope**: replacing `dotnet user-secrets` outright; production secret management; migrating every existing secret to `.env` (only `MiniMax:ApiKey` is in scope here).

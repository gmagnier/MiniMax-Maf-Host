## 1. Dependencies

- [x] 1.1 Add `<PackageReference Include="DotNetEnv" Version="3.1.1" />` to `MafMiniMaxAgent.csproj` and verify `dotnet restore` succeeds with no transitive conflict against the existing `Microsoft.Agents.AI.*` 1.22.0 / preview packages

## 2. Code

- [x] 2.1 Insert `DotNetEnv.Env.Load();` as the first executable statement in `Program.cs`, before `WebApplication.CreateBuilder(args)`, and verify the file still compiles (`dotnet build` succeeds)

## 3. Repo files

- [x] 3.1 Create `.env.example` at the repo root with `MiniMax__ApiKey=` (empty), `MiniMax__Endpoint=https://api.minimaxi.chat/v1`, `MiniMax__ModelId=MiniMax-M3` and verify `git check-ignore -v .env` reports `.env` is ignored while `git check-ignore -v .env.example` reports it is NOT ignored

## 4. Docs

- [x] 4.1 Update `README.md` Setup section to lead with the `.env` flow (`cp .env.example .env`, edit, `dotnet run`) and add a one-sentence note that `dotnet user-secrets` still works as a fallback

## 5. Verification

- [x] 5.1 With no `.env` and no `MiniMax__ApiKey` env var set, run `dotnet run` and verify the existing `InvalidOperationException` about the missing API key still fires (regression guard)
- [x] 5.2 Create a `.env` containing a valid `MiniMax__ApiKey`, run `dotnet run`, and verify the host starts, the `/` landing page returns 200, and `/devui` (development profile) accepts a chat round-trip
- [x] 5.3 With both a `.env` key AND a real shell `export MiniMax__ApiKey=…`, verify the shell value wins by pointing the shell var at a deliberately wrong endpoint and observing the agent fail with that endpoint in the error

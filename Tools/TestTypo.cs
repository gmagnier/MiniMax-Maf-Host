// TestTypo.cs — minimal helper used by the PR-review workflow demo.
// Exists so reviewers can confirm the Tools/ scope accepts new files and
// the build pipeline surfaces style/analyzer errors via `dotnet build`.

namespace MafMiniMaxAgent.Tools;

public static class TestTypo
{
    public static void Do()
    {
        // Intentionally empty: this is a smoke-test helper, not production code.
    }
}
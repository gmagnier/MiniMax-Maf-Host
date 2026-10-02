// MafCoderWorkdir.cs — singleton accessor that lets the maf-coder tools
// resolve paths against a workspace-specific directory rather than the
// MAF host's process cwd.
//
// Pattern:
//   * When a tool is invoked WITHOUT a workdir override, it falls back to
//     Directory.GetCurrentDirectory() (the MAF host's repo).
//   * When the dispatcher wants maf-coder to operate on a different repo
//     (cloned into /tmp/maf-workspace/<id>/ by the webhook handler), it
//     calls SetWorkdir() before invoking /ag-ui/coder. The tool methods
//     pick that up via Current().
//
// We use AsyncLocal so concurrent AG-UI requests can each have their own
// workspace without cross-contamination.

namespace MafMiniMaxAgent.Tools;

public static class MafCoderWorkdir
{
    private static readonly AsyncLocal<string?> Override = new();

    /// <summary>
    /// Returns the current workspace root that maf-coder should treat as
    /// its repo root. Defaults to Directory.GetCurrentDirectory() when no
    /// override is set.
    /// </summary>
    public static string Current()
    {
        var overrideValue = Override.Value;
        if (!string.IsNullOrEmpty(overrideValue) && Directory.Exists(overrideValue))
        {
            return overrideValue;
        }
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Set the workspace override for this async flow. Used by the
    /// dispatcher when invoking maf-coder on a cloned target repo.
    /// </summary>
    public static IDisposable Set(string workdir)
    {
        if (string.IsNullOrWhiteSpace(workdir))
            throw new ArgumentException("Workdir cannot be empty", nameof(workdir));
        if (!Path.IsPathRooted(workdir))
            throw new ArgumentException($"Workdir '{workdir}' must be absolute", nameof(workdir));
        if (!Directory.Exists(workdir))
            throw new DirectoryNotFoundException($"Workdir '{workdir}' does not exist");

        var previous = Override.Value;
        Override.Value = workdir;
        return new Restorer(previous);
    }

    private sealed class Restorer : IDisposable
    {
        private readonly string? _previous;
        public Restorer(string? previous) => _previous = previous;
        public void Dispose() => Override.Value = _previous;
    }
}
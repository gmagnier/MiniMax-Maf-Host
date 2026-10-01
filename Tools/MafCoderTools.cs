// MafCoderTools.cs — file-local tools used by maf-coder.
//
// Scope rules (hardcoded below):
//   - Allowed roots: openspec/, Tools/ (NOT Program.cs, NOT the .csproj, NOT
//     bin/obj/, NOT dotnet config).
//   - Allowed top-level files: README.md, AGENTS.md (notes for the agent).
//   - Refused with ERROR: ... otherwise. Path-traversal guard with
//     Path.GetFullPath + StartsWith + StringComparison.Ordinal, same as the
//     existing open_spec family.
//
// Each tool returns a plain string (success or ERROR: ...) so MAF can pass
// the result back to the model without JSON wrapping.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using SCDesc = System.ComponentModel.DescriptionAttribute;

namespace MafMiniMaxAgent.Tools;

public static class MafCoderTools
{
    // ----- Paths & scopes ----------------------------------------------------

    private static readonly string[] AllowedDirs =
    [
        "openspec", // hand-edit change files and specs (the source of truth)
        "Tools", // add or extend MAF-native tools
    ];

    private static readonly string[] AllowedTopLevelFiles =
    [
        "README.md",
        "AGENTS.md",
    ];

    private static (bool ok, string? resolved, string? error) ResolveAndCheck(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return (false, null, "ERROR: relativePath is empty");

        // Reject absolute paths and obvious traversal up-front.
        if (Path.IsPathRooted(relativePath))
            return (false, null, $"ERROR: '{relativePath}' is absolute — must be repo-relative");
        if (relativePath.Contains("..", StringComparison.Ordinal))
            return (false, null, $"ERROR: '{relativePath}' contains '..' — refused");

        var cwd = Directory.GetCurrentDirectory();
        var target = Path.GetFullPath(Path.Combine(cwd, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        // Top-level allowlist
        var firstSegment = relativePath.Replace('\\', '/').Split('/', 2)[0];
        var isAllowedTopFile = AllowedTopLevelFiles.Contains(firstSegment, StringComparer.Ordinal)
            && !relativePath.Contains('/');
        var isAllowedDir = AllowedDirs.Contains(firstSegment, StringComparer.Ordinal);

        if (!isAllowedTopFile && !isAllowedDir)
            return (false, null, $"ERROR: '{relativePath}' is outside the allowed scope (openspec/, Tools/, README.md, AGENTS.md). Refused.");

        // Belt and braces: ensure the resolved path actually sits inside cwd.
        if (!target.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(target, cwd, StringComparison.Ordinal))
        {
            return (false, null, $"ERROR: '{relativePath}' resolves outside the project root");
        }

        return (true, target, null);
    }

    // ----- read_file ---------------------------------------------------------

    /// <summary>
    /// Read a file from the project, scoped to openspec/, Tools/, README.md, AGENTS.md.
    /// </summary>
    [SCDesc("Read a file from the project (relative to the repo root). Scoped to openspec/, Tools/, README.md, and AGENTS.md. Refuses paths outside that set, including Program.cs and the .csproj.")]
    public static string ReadFile(
        [SCDesc("Repo-relative path, e.g. 'openspec/changes/init-foo/proposal.md' or 'Tools/MafCoderTools.cs'. Must not be absolute or contain '..'.")] string relativePath)
    {
        try
        {
            var (ok, target, error) = ResolveAndCheck(relativePath);
            if (!ok) return error!;
            if (!File.Exists(target))
                return $"ERROR: file not found: {relativePath}";
            return File.ReadAllText(target);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ----- write_file --------------------------------------------------------

    /// <summary>
    /// Overwrite a file in the allowed scope. Always overwrites (use apply_patch
    /// for in-place edits to existing code).
    /// </summary>
    [SCDesc("Overwrite a file in the project. Scoped to openspec/, Tools/, README.md, AGENTS.md. Use this for new files or full rewrites; for targeted in-place edits, prefer apply_patch.")]
    public static string WriteFile(
        [SCDesc("Repo-relative path (same scope rules as read_file).")] string relativePath,
        [SCDesc("New file contents (overwrites the file).")] string content)
    {
        try
        {
            var (ok, target, error) = ResolveAndCheck(relativePath);
            if (!ok) return error!;

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(target!, content);
            return $"OK: wrote {relativePath} ({content.Length} bytes)";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    // ----- apply_patch -------------------------------------------------------

    /// <summary>
    /// Apply a single find/replace patch to a file. Refuses if find() is not
    /// unique in the file (so the agent can't accidentally mass-rewrite).
    /// </summary>
    [SCDesc("Apply a single find/replace patch to a file. Refuses if the 'find' text occurs zero or more than once in the file — the agent must disambiguate first.")]
    public static string ApplyPatch(
        [SCDesc("Repo-relative path (same scope rules as read_file).")] string relativePath,
        [SCDesc("Exact text to find. Must appear exactly once in the file.")] string find,
        [SCDesc("Replacement text.")] string replace)
    {
        try
        {
            var (ok, target, error) = ResolveAndCheck(relativePath);
            if (!ok) return error!;
            if (!File.Exists(target))
                return $"ERROR: file not found: {relativePath}";

            var content = File.ReadAllText(target);
            var occurrences = CountOccurrences(content, find);
            if (occurrences == 0)
            {
                return $"ERROR: 'find' text not found in {relativePath}. Read the file first to copy the exact text.";
            }
            if (occurrences > 1)
            {
                return $"ERROR: 'find' text appears {occurrences} times in {relativePath}. Add more surrounding context to make it unique.";
            }

            var updated = content.Replace(find, replace, StringComparison.Ordinal);
            File.WriteAllText(target, updated);
            return $"OK: patched {relativePath} (replaced {find.Length} chars with {replace.Length} chars)";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    // ----- shell_command -----------------------------------------------------

    // Hardcoded allowlist of commands. Anything else is rejected with
    // ERROR: command not allowed. We keep this short on purpose: the LLM
    // does not need a general-purpose shell, only a few build/test commands.
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "dotnet",          // build, test, run, format, restore
        "git",            // diff, status, log, add, commit (push is routed through gh)
        "gh",             // pr create, pr list, pr view
        "node",           // node script.js (for ad-hoc probes)
        "npx",            // npx tsc --noEmit on dashboard if needed
    };

    private static readonly HashSet<string> BlockedGitSubcommands = new(StringComparer.Ordinal)
    {
        // Destructive git ops are NOT allowed via this tool.
        "reset", "checkout", "clean", "push",
        "branch",
        "rebase", "merge",
    };

    /// <summary>
    /// Run a shell command from a small allowlist (dotnet, git, gh, node, npx).
    /// Anything else is refused. Use this to verify a build, run tests, or
    /// inspect git state — not as a free shell.
    /// </summary>
    [SCDesc("Run a shell command from a small allowlist (dotnet, git, gh, node, npx). Anything else is refused. Use to verify a build, run tests, or inspect git state. Destructive git ops are blocked.")]
    public static async Task<string> ShellCommandAsync(
        [SCDesc("The full command line, e.g. 'dotnet build' or 'git status'. First token must be an allowlisted command.")] string command,
        [SCDesc("Optional working directory (repo-relative). Defaults to the project root.")] string? cwdRelative = null,
        [SCDesc("Optional timeout in seconds (default 60, max 600).")] int timeoutSeconds = 60)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(command))
                return "ERROR: command is empty";

            timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 600);

            var checkError = CheckCommandAllowed(command);
            if (checkError is not null) return checkError;

            var workdir = ResolveWorkdir(cwdRelative);
            if (workdir.StartsWith("ERROR:", StringComparison.Ordinal))
                return workdir;

            return await Task.Run(() => RunAndCapture(command, workdir, timeoutSeconds));
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string? CheckCommandAllowed(string command)
    {
        var firstSpace = command.IndexOf(' ');
        var firstToken = firstSpace < 0 ? command : command[..firstSpace];
        var exeName = Path.GetFileName(firstToken);
        if (!AllowedCommands.Contains(exeName))
            return $"ERROR: '{exeName}' is not in the allowlist ({string.Join(", ", AllowedCommands)})";

        if (string.Equals(exeName, "git", StringComparison.Ordinal) && firstSpace >= 0)
        {
            var restTrimmed = command[(firstSpace + 1)..].TrimStart();
            var gitSubcommand = restTrimmed.Split(' ', 2)[0];
            if (BlockedGitSubcommands.Contains(gitSubcommand))
                return $"ERROR: git {gitSubcommand} is blocked. Use gh pr create for pushing, or run destructive ops manually.";
        }
        return null;
    }

    private static string ResolveWorkdir(string? cwdRelative)
    {
        if (string.IsNullOrWhiteSpace(cwdRelative))
            return Directory.GetCurrentDirectory();

        var (ok, target, error) = ResolveAndCheck(cwdRelative);
        if (!ok) return error!;
        return Directory.Exists(target) ? target : Directory.GetCurrentDirectory();
    }

    private static string RunAndCapture(string command, string workdir, int timeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList = { "-lc", command },
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        if (!proc.WaitForExit(timeoutSeconds * 1000))
        {
            try { proc.Kill(); } catch { /* ignore */ }
            return $"ERROR: command timed out after {timeoutSeconds}s";
        }
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        var exitMarker = proc.ExitCode == 0 ? "OK" : $"EXIT {proc.ExitCode}";
        var sb = new StringBuilder();
        sb.Append(exitMarker).Append('\n');
        if (!string.IsNullOrWhiteSpace(stdout)) sb.Append("STDOUT: ").Append(stdout);
        if (!string.IsNullOrWhiteSpace(stderr)) sb.Append("STDERR: ").Append(stderr);
        return sb.ToString();
    }
}
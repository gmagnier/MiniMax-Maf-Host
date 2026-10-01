// HelloLog.cs — minimal leveled console logger for ad-hoc agent output.
// Use this for quick "hello world" style traces that don't need the full
// Microsoft.Extensions.Logging pipeline (no DI, no structured sinks).
//
// Format: "[LEVEL] message" on stderr for warn/error, stdout for info.

namespace MafMiniMaxAgent.Mcp;

public static class HelloLog
{
    public static void WriteInfo(string message)
        => Console.Out.WriteLine($"[INFO] {message}");

    public static void WriteWarn(string message)
        => Console.Error.WriteLine($"[WARN] {message}");

    public static void WriteError(string message)
        => Console.Error.WriteLine($"[ERROR] {message}");
}

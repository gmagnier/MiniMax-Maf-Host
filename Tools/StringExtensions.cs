// StringExtensions.cs — small safe-substring helper for trimming log/error
// output to a bounded size.

namespace MafMiniMaxAgent.Mcp;

internal static class StringExtensions
{
    public static string SafeSubstring(this string s, int start, int length)
        => s.Length <= start ? string.Empty
            : s.Length - start <= length ? s[start..]
            : s.Substring(start, length);
}
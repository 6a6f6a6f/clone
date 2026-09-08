using System.Text.RegularExpressions;

namespace Clone.Git;

public static partial class DiagnosticText
{
    public static string Sanitize(string value)
    {
        // Discard terminal control sequences before exposing untrusted server diagnostics.
        var clean = ControlSequence().Replace(value, "");
        clean = new string(clean.Where(c => !char.IsControl(c) && c is not '\u202a' and not '\u202b' and not '\u202c' and not '\u202d' and not '\u202e' and not '\u2066' and not '\u2067' and not '\u2068' and not '\u2069').ToArray());
        clean = Credentials().Replace(clean, "$1[redacted]@");
        return QuerySecret().Replace(clean, "$1[redacted]");
    }

    [GeneratedRegex(@"\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)?|\x1b\[[0-?]*[ -/]*[@-~]|\x1b.", RegexOptions.CultureInvariant)]
    private static partial Regex ControlSequence();
    [GeneratedRegex(@"(https?://)[^/\s@]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Credentials();
    [GeneratedRegex(@"((?:token|password|access_token|authorization|secret)=)[^\s&]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecret();
}

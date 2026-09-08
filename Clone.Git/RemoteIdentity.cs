using System.Globalization;
using System.Text.RegularExpressions;

namespace Clone.Git;

public sealed partial record RemoteIdentity
{
    public string Remote { get; }
    public string Host { get; }
    public string HostDirectory { get; }
    public IReadOnlyList<string> Namespace { get; }
    public string Repository { get; }
    public string Identity => $"{HostDirectory}/{string.Join('/', Namespace)}/{Repository}";

    private RemoteIdentity(string remote, string host, string hostDirectory, string[] segments)
    {
        Remote = remote;
        Host = host;
        HostDirectory = hostDirectory;
        Namespace = Array.AsReadOnly(segments[..^1]);
        Repository = segments[^1];
    }

    public static RemoteIdentity Parse(string remote)
    {
        if (string.IsNullOrWhiteSpace(remote) || remote.Length > 4096 || remote.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || remote.Contains('\\') || remote.Contains('?') || remote.Contains('#'))
            throw Invalid();

        string authority;
        string path;
        var ssh = false;
        var port = "";
        if (remote.StartsWith("https://", StringComparison.Ordinal) || remote.StartsWith("ssh://", StringComparison.Ordinal))
        {
            ssh = remote.StartsWith("ssh://", StringComparison.Ordinal);
            var rest = remote[(ssh ? 6 : 8)..];
            var slash = rest.IndexOf('/');
            if (slash <= 0) throw Invalid();
            authority = rest[..slash];
            path = rest[(slash + 1)..];
            if (!ssh && authority.Contains('@')) throw Invalid();
            var colon = authority.LastIndexOf(':');
            if (colon >= 0)
            {
                var value = authority[(colon + 1)..];
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 1 or > 65535) throw Invalid();
                port = number.ToString(CultureInfo.InvariantCulture);
                authority = authority[..colon];
                if ((ssh && port == "22") || (!ssh && port == "443")) port = "";
            }
        }
        else
        {
            ssh = true;
            var colon = remote.IndexOf(':');
            if (colon <= 0) throw Invalid();
            authority = remote[..colon];
            path = remote[(colon + 1)..];
        }

        var at = authority.IndexOf('@');
        if (at >= 0)
        {
            if (!ssh || !UserPattern().IsMatch(authority[..at])) throw Invalid();
            authority = authority[(at + 1)..];
        }
        if (!HostPattern().IsMatch(authority) || authority.Split('.').Any(x => x.Length == 0 || x.StartsWith('-') || x.EndsWith('-'))) throw Invalid();
        var host = authority.ToLowerInvariant();
        var segments = path.Split('/');
        if (segments.Length is < 2 or > 17) throw Invalid();
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = Uri.UnescapeDataString(segments[i]);
            if (i == segments.Length - 1 && segment.EndsWith(".git", StringComparison.Ordinal)) segment = segment[..^4];
            segment = segment.Normalize(NormalizationForm.FormC);
            ValidateSegment(segment);
            segments[i] = segment;
        }
        // A colon cannot occur in a DNS host, making the explicit port key unambiguous.
        return new RemoteIdentity(remote, host, host + (port.Length == 0 ? "" : ":" + port), segments);
    }

    internal static void ValidateSegment(string segment)
    {
        if (Encoding.UTF8.GetByteCount(segment) > 100 || !SegmentPattern().IsMatch(segment) || segment.EndsWith('.') || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)) throw Invalid();
    }

    private static ArgumentException Invalid() => new("Invalid remote. Use HTTPS, ssh://, or host:namespace/repository SSH syntax without credentials, query strings, or unsafe path segments.");

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,252}\z", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();
    [GeneratedRegex(@"\A[A-Za-z0-9_][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex UserPattern();
    [GeneratedRegex(@"\A[\p{L}\p{N}][\p{L}\p{N}\p{M}._-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentPattern();
}

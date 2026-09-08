namespace Clone.Git;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<CloneLayout>))]
public enum CloneLayout { Host, Legacy }

public sealed record CloneRequest(string Remote, string Root, CloneLayout Layout = CloneLayout.Host,
    string? GitPath = null, bool Interactive = true, TimeSpan? Timeout = null);

public sealed class GitFailedException(int exitCode) : IOException($"Git failed (exit {exitCode}). Check authentication, the remote, and network access.")
{
    public int ExitCode { get; } = exitCode;
}

public static class CloneService
{
    public static string Preview(CloneRequest request)
    {
        var remote = RemoteIdentity.Parse(request.Remote);
        if (!Enum.IsDefined(request.Layout)) throw new ArgumentException("Unknown layout.");
        ConfigurationStore.Validate(new CloneConfiguration(request.Root, request.Layout, request.GitPath));
        return Path.Combine([Path.GetFullPath(request.Root), .. Segments(remote, request.Layout)]);
    }

    public static async Task<string> CloneAsync(CloneRequest request, Action<string, bool>? output = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This release supports macOS only.");
        if (MacDirectory.GetUser() == 0) throw new IOException("Run clone as your normal user, without sudo.");
        var remote = RemoteIdentity.Parse(request.Remote);
        _ = Preview(request);
        var git = Runner.ResolveExecutable("git", request.GitPath);
        cancellationToken.ThrowIfCancellationRequested();
        using var root = MacDirectory.OpenRoot(request.Root, true);
        var segments = Segments(remote, request.Layout);
        var parents = new List<MacDirectory>();
        var parent = root;
        try
        {
            foreach (var segment in segments[..^1])
            {
                parent = parent.Child(segment, true);
                parents.Add(parent);
                parent.CheckOwnership();
            }
            var destination = Path.Combine(parent.Path, remote.Repository);
            if (parent.Contains(remote.Repository))
            {
                var detail = "Existing data was preserved.";
                try
                {
                    using var existing = parent.Child(remote.Repository);
                    using var metadata = existing.Child(".git");
                    var origin = await Runner.RunAsync(git, ["config", "--file", Path.Combine(metadata.Path, "config"), "--no-includes", "--get", "remote.origin.url"], root.Path,
                        environment: GitEnvironment(false), timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (origin.ExitCode == 0 && origin.Diagnostics.Count == 1)
                        detail = RemoteIdentity.Parse(origin.Diagnostics[0]).Identity == remote.Identity ? "Its origin matches this remote." : "Its origin differs from this remote.";
                }
                catch (Exception error) when (error is IOException or ArgumentException or TimeoutException) { }
                throw new IOException($"Destination already exists: {destination}. {detail} Choose another root or inspect it manually.");
            }
            var stagingName = ".clone-staging-" + Guid.NewGuid().ToString("N");
            using var staging = root.CreateExclusive(stagingName);
            var stagingPath = staging.Path;
            var parentPath = parent.Path;
            try
            {
                var arguments = new List<string> { "--no-pager", "-c", "protocol.allow=never", "-c", "protocol.https.allow=always", "-c", "protocol.ssh.allow=always", "-c", "core.hooksPath=/dev/null", "clone", "--template=" };
                if (request.Interactive) arguments.Add("--progress");
                arguments.AddRange(["--", remote.Remote, "repository"]);
                var result = await Runner.RunAsync(git, arguments, stagingPath, output, GitEnvironment(request.Interactive), request.Timeout, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0) throw new GitFailedException(result.ExitCode);
                cancellationToken.ThrowIfCancellationRequested();
                using var checkout = staging.Child("repository");
                using var metadata = checkout.Child(".git");
                if (!metadata.Contains("HEAD") || !metadata.Contains("config")) throw new IOException("Git did not produce the expected repository; nothing was published.");
                if (staging.Path != stagingPath || parent.Path != parentPath || !parentPath.StartsWith(root.Path + "/", StringComparison.Ordinal))
                    throw new IOException("The destination changed during cloning; nothing was published.");
                staging.Publish("repository", parent, remote.Repository);
                return destination;
            }
            finally
            {
                try { root.RemoveOwnedTree(stagingName); }
                catch (IOException) { output?.Invoke("Could not remove an owned staging directory. Inspect the project root for .clone-staging-* before removing it manually.", true); }
            }
        }
        finally { foreach (var directory in parents) directory.Dispose(); }
    }

    private static string[] Segments(RemoteIdentity remote, CloneLayout layout) => layout == CloneLayout.Host
        ? [remote.HostDirectory, .. remote.Namespace, remote.Repository]
        : [.. remote.Namespace, remote.Repository];

    internal static Dictionary<string, string?> GitEnvironment(bool interactive)
    {
        var environment = new Dictionary<string, string?>();
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>())
        {
            if (name.StartsWith("GIT_CONFIG_KEY_", StringComparison.Ordinal) || name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.Ordinal)) environment[name] = null;
        }
        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT", "GIT_TEMPLATE_DIR" }) environment[name] = null;
        environment["GIT_ALLOW_PROTOCOL"] = "https:ssh";
        environment["GIT_PROTOCOL_FROM_USER"] = "0";
        if (!interactive)
        {
            environment["GIT_TERMINAL_PROMPT"] = "0";
            environment["GCM_INTERACTIVE"] = "never";
            environment["GIT_SSH_COMMAND"] = (Environment.GetEnvironmentVariable("GIT_SSH_COMMAND") ?? "ssh") + " -oBatchMode=yes";
        }
        return environment;
    }
}

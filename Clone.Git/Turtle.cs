namespace Clone.Git;

public static class Turtle
{
    public static void Clone(string remote,
        string destination,
        Action<string>? output = default,
        CancellationToken ct = default)
    {
        var binary = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "git.exe" : "git";
        var workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workingDirectory);

        Runner.Create(binary)
            .WithOutputDataReceivedCallback(line => output?.Invoke(line))
            .WithErrorDataReceivedCallback(line => output?.Invoke(line))
            .WithWorkingDirectory(workingDirectory)
            .WithCancellationToken(ct)
            .WithArguments("--no-pager", "clone", remote, destination)
            .RunAsync()
            .Wait(ct);

        Directory.Delete(workingDirectory, true);
    }

    public static bool TryParseRemote(string remote,
        [NotNullWhen(true)] out string? user,
        [NotNullWhen(true)] out string? repository)
    {
        try
        {
            user = remote[..remote.LastIndexOf('/')];
            user = remote.Contains('@')
                ? user[(user.LastIndexOf(':') + 1)..]
                : user[(user.LastIndexOf('/') + 1)..];
        }
        catch (ArgumentOutOfRangeException)
        {
            user = default;
            repository = default;

            return false;
        }

        try
        {
            repository = remote[(remote.LastIndexOf('/') + 1)..];
            if (repository.EndsWith(".git")) repository = repository[..^4];
        }
        catch (ArgumentOutOfRangeException)
        {
            user = default;
            repository = default;

            return false;
        }

        return true;
    }
}

using System.ComponentModel;

namespace Clone.Git;

public sealed record ProcessResult(int ExitCode, IReadOnlyList<string> Diagnostics);

public sealed class Runner
{
    public static string ResolveExecutable(string name, string? explicitPath = null, string? searchPath = null)
    {
        if (explicitPath is not null)
        {
            if (!Path.IsPathFullyQualified(explicitPath) || !IsExecutable(explicitPath)) throw new IOException("The Git override must name an executable absolute path.");
            return Path.GetFullPath(explicitPath);
        }
        if (name != Path.GetFileName(name)) throw new ArgumentException("Executable lookup requires a filename.");
        foreach (var directory in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.Combine(directory, name);
            if (IsExecutable(candidate)) return Path.GetFullPath(candidate);
        }
        throw new IOException("Git was not found. Install Git using Homebrew or Apple's Command Line Tools, then retry or use --git /absolute/path/to/git.");
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path)) return false;
        return OperatingSystem.IsWindows() || (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory,
        Action<string, bool>? output = null, IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("Process execution requires an absolute executable path.");
        if (timeout is { } duration && duration <= TimeSpan.Zero) throw new ArgumentException("Timeout must be positive.");
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null) start.Environment.Remove(key);
                else start.Environment[key] = value;
            }
        }
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new IOException("Could not start Git.");
        }
        catch (Win32Exception)
        {
            throw new IOException("Could not start Git. Check its executable path and permissions.");
        }
        var diagnostics = new Queue<string>();
        var outputLock = new object();
        void Receive(string line, bool error)
        {
            var safe = DiagnosticText.Sanitize(line);
            lock (outputLock)
            {
                if (diagnostics.Count == 16) diagnostics.Dequeue();
                diagnostics.Enqueue(safe);
                output?.Invoke(safe, error);
            }
        }
        using var reading = new CancellationTokenSource();
        var outputFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ReadOutputAsync(StreamReader reader, bool error)
        {
            try { await ReadLinesAsync(reader, line => Receive(line, error), reading.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { outputFailure.TrySetResult(); throw; }
        }
        var streams = Task.WhenAll(ReadOutputAsync(process.StandardOutput, false), ReadOutputAsync(process.StandardError, true));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } limit) lifetime.CancelAfter(limit);
        var canceled = false;
        try
        {
            var exited = process.WaitForExitAsync(lifetime.Token);
            var first = await Task.WhenAny(exited, outputFailure.Task).ConfigureAwait(false);
            if (first == outputFailure.Task)
            {
                Kill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw new IOException("Could not consume Git output; the operation was stopped.");
            }
            await exited.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            // A child retaining a pipe must not hang the caller after Git exits.
            reading.CancelAfter(TimeSpan.FromSeconds(2));
            try { await streams.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception) when (streams.IsFaulted) { }
        }
        if (streams.IsFaulted && !canceled) throw new IOException("Could not consume Git output; the operation was stopped.");
        if (canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Git exceeded the configured timeout; the incomplete clone was not published.");
        }
        return new ProcessResult(process.ExitCode, diagnostics.ToArray());
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> receive, CancellationToken token)
    {
        var buffer = new char[1024];
        var line = new StringBuilder();
        var oversized = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < count; i++)
            {
                var character = buffer[i];
                if (character is '\n' or '\r')
                {
                    if (oversized) receive("[oversized Git output omitted]");
                    else if (line.Length > 0) receive(line.ToString());
                    line.Clear();
                    oversized = false;
                }
                else if (!oversized)
                {
                    if (line.Length == 8192) { line.Clear(); oversized = true; }
                    else line.Append(character);
                }
            }
        }
        if (oversized) receive("[oversized Git output omitted]");
        else if (line.Length > 0) receive(line.ToString());
    }
}

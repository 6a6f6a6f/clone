using Clone.Git;

try
{
    if (args.Length != 1) throw new ArgumentException("Usage: clone <remote>");
    var root = Environment.GetEnvironmentVariable("CLONE_PROJECT_FOLDER") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var request = new CloneRequest(args[0], root, Environment.GetEnvironmentVariable("CLONE_PROJECT_FOLDER") is null ? CloneLayout.Host : CloneLayout.Legacy, Interactive: !Console.IsInputRedirected);
    var destination = await CloneService.CloneAsync(request, (line, _) => Console.Error.WriteLine(line), cancellation.Token);
    Console.WriteLine(destination);
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Canceled; no incomplete clone was published."); return 130; }
catch (TimeoutException error) { Console.Error.WriteLine(error.Message); return 124; }
catch (ArgumentException error) { Console.Error.WriteLine(DiagnosticText.Sanitize(error.Message)); return 2; }
catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
{
    Console.Error.WriteLine(DiagnosticText.Sanitize(error.Message));
    return 1;
}

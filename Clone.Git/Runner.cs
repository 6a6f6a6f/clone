namespace Clone.Git;

/// <summary>
/// Provides a fluent API to configure and run external processes.
/// </summary>
public class Runner
{
    private readonly string _executable;
    private string _workingDirectory = Directory.GetCurrentDirectory();

    private readonly List<string> _arguments = [];

    private Action<string>? _outputDataReceivedCallback;
    private Action<string>? _errorDataReceivedCallback;

    private TimeSpan _timeout = TimeSpan.FromSeconds(60);
    private CancellationToken _cancellationToken = CancellationToken.None;

    /// <summary>
    /// Creates a new instance of the <see cref="Runner"/> class with the specified executable.
    /// </summary>
    /// <param name="executable">The name or path of the executable to run.</param>
    /// <returns>A new <see cref="Runner"/> instance configured to use the specified executable.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the specified executable is not found in the file system or the PATH environment variable.
    /// </exception>
    /// <exception cref="AmbiguousExecutableNameMatchException">
    /// Thrown when multiple executables with the same name are found in the PATH environment variable.
    /// </exception>
    /// <remarks>
    /// The method first tries to resolve the full path of the executable.
    /// If it's not found, it looks for the executable in directories specified by the PATH environment variable.
    /// If still not found, or if multiple matches are found in the PATH, an exception is thrown.
    /// </remarks>
    public static Runner Create(string executable)
    {
        if (!File.Exists(executable))
        {
            var paths = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
                .Select(path => Path.Combine(path, executable))
                .Where(File.Exists)
                .Distinct()
                .Select(Path.GetFullPath)
                .ToArray();

            switch (paths.Length)
            {
                case 0:
                    throw new FileNotFoundException();
                case > 1:
                    throw new AmbiguousExecutableNameMatchException(executable, paths);
            }

            executable = paths[0];
        }

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException();
        }

        return new Runner(executable);
    }

    /// <summary>
    /// Sets the working directory for the process.
    /// </summary>
    /// <param name="workingDirectory">The working directory path.</param>
    /// <returns>The current <see cref="Runner"/> instance.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the specified directory does not exist.</exception>
    public Runner WithWorkingDirectory(string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException();
        }

        _workingDirectory = workingDirectory;
        return this;
    }

    /// <summary>
    /// Sets the callback for processing standard output data.
    /// </summary>
    /// <param name="callback">The callback action.</param>
    /// <returns>The current <see cref="Runner"/> instance.</returns>
    public Runner WithOutputDataReceivedCallback(Action<string> callback)
    {
        _outputDataReceivedCallback = callback;
        return this;
    }

    /// <summary>
    /// Sets the callback for processing standard error data.
    /// </summary>
    /// <param name="callback">The callback action.</param>
    /// <returns>The current <see cref="Runner"/> instance.</returns>
    public Runner WithErrorDataReceivedCallback(Action<string> callback)
    {
        _errorDataReceivedCallback = callback;
        return this;
    }

    /// <summary>
    /// Sets the maximum time to wait for the process to complete.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>The current <see cref="Runner"/> instance.</returns>
    public Runner WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the cancellation token.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current <see cref="Runner"/> instance.</returns>
    public Runner WithCancellationToken(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        return this;
    }

    /// <summary>
    /// Adds arguments to pass to the process.
    /// </summary>
    /// <param name="arguments">The arguments to add.</param>
    /// <returns>The current <see cref="Runner"/> instance.</returns>
    public Runner WithArguments(params string[] arguments)
    {
        _arguments.AddRange(arguments);
        return this;
    }

    /// <summary>
    /// Asynchronously runs the configured external process.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the process cannot be started.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when the process does not complete within the configured timeout.
    /// </exception>
    /// <remarks>
    /// This method configures and starts an external process based on the properties set
    /// in the <see cref="Runner"/> instance. It sets up callbacks for standard output and
    /// standard error if they have been configured. The method also respects the configured
    /// timeout and cancellation token.
    /// 
    /// Note that the process is killed if it doesn't complete within the configured timeout
    /// or if a cancellation is triggered.
    /// </remarks>
    public async Task RunAsync()
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = _executable,
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = _outputDataReceivedCallback is not null,
            RedirectStandardError = _errorDataReceivedCallback is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = string.Join(' ', _arguments)
        };

        var process = new Process();
        process.StartInfo = processStartInfo;
        process.EnableRaisingEvents = true;

        if (_outputDataReceivedCallback is not null)
        {
            process.OutputDataReceived += (_, args) =>
            {
                var content = args.Data;
                if (string.IsNullOrEmpty(content)) return;

                _outputDataReceivedCallback(content);
            };
        }

        if (_errorDataReceivedCallback is not null)
        {
            process.ErrorDataReceived += (_, args) =>
            {
                var content = args.Data;
                if (string.IsNullOrEmpty(content)) return;

                _errorDataReceivedCallback(content);
            };
        }

        if (!process.Start())
        {
            throw new InvalidOperationException();
        }

        if (_outputDataReceivedCallback is not null)
        {
            process.BeginOutputReadLine();
        }

        if (_errorDataReceivedCallback is not null)
        {
            process.BeginErrorReadLine();
        }

        await using (_cancellationToken.Register(() =>
                     {
                         if (!process.HasExited) process.Kill();
                     }))
        {
            var delayTask = Task.Delay(_timeout, _cancellationToken);
            var processTask = Task.Run(() => process.WaitForExit(), _cancellationToken);

            var completedTask = await Task.WhenAny(processTask, delayTask);

            if (completedTask == delayTask)
            {
                process.Kill();
                throw new TimeoutException();
            }
        }
    }

    /// <summary>
    /// Private constructor for specifying executable.
    /// </summary>
    /// <param name="executable">The path to the executable.</param>
    private Runner(string executable)
    {
        _executable = executable;
    }
}

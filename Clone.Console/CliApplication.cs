using System.Globalization;
using System.Reflection;
using Clone.Git;

namespace Clone.CommandLine;

public static class CliApplication
{
    public static string Version => typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, bool interactive,
        CancellationToken cancellationToken = default, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h") { await stdout.WriteLineAsync(Help); return 0; }
            if (args.Length == 1 && args[0] == "--version") { await stdout.WriteLineAsync("clone " + Version); return 0; }
            var options = Options.Parse(args);
            var configuration = options.Operands.SequenceEqual(["config", "reset"]) ? new CloneConfiguration() : ConfigurationStore.Load(options.ConfigurationDirectory);
            if (options.Operands.FirstOrDefault() == "config")
                return await ConfigureAsync(options, configuration, stdout).ConfigureAwait(false);

            var legacyRoot = environment("CLONE_PROJECT_FOLDER");
            if (string.IsNullOrWhiteSpace(legacyRoot)) legacyRoot = null;
            var root = options.Root ?? legacyRoot ?? configuration.Root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects");
            root = ExpandPath(root);
            var source = options.Root is not null ? "command line" : legacyRoot is not null ? "CLONE_PROJECT_FOLDER" : configuration.Root is not null ? "configuration" : "default";
            var layout = options.Layout ?? configuration.Layout ?? (legacyRoot is not null ? CloneLayout.Legacy : CloneLayout.Host);
            var git = options.GitPath ?? configuration.GitPath;
            var seconds = options.TimeoutSeconds ?? configuration.TimeoutSeconds;
            var timeout = seconds is null ? (TimeSpan?)null : TimeSpan.FromSeconds(seconds.Value);
            var request = new CloneRequest(options.Operands.Single(), root, layout, git, interactive && !options.NonInteractive, timeout);
            if (request.Remote == "doctor")
                return await DoctorAsync(request, source, options.ConfigurationDirectory, stdout, stderr, cancellationToken).ConfigureAwait(false);

            var preview = CloneService.Preview(request);
            if (options.DryRun) { await stdout.WriteLineAsync(preview); return 0; }
            if (!options.Quiet) await stderr.WriteLineAsync("Cloning into " + DiagnosticText.Sanitize(preview));
            var destination = await CloneService.CloneAsync(request,
                (line, _) => { if (!options.Quiet) stderr.WriteLine(line); }, cancellationToken).ConfigureAwait(false);
            await stdout.WriteLineAsync(destination);
            return 0;
        }
        catch (OperationCanceledException) { await stderr.WriteLineAsync("Canceled. No incomplete clone was published; retry when ready."); return 130; }
        catch (TimeoutException error) { await stderr.WriteLineAsync(DiagnosticText.Sanitize(error.Message)); return 124; }
        catch (ArgumentException error) { await stderr.WriteLineAsync(DiagnosticText.Sanitize(error.Message) + " Run clone --help for usage."); return 2; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            await stderr.WriteLineAsync(DiagnosticText.Sanitize(error.Message));
            return 1;
        }
    }

    private static async Task<int> ConfigureAsync(Options options, CloneConfiguration configuration, TextWriter output)
    {
        if (options.Root is not null || options.Layout is not null || options.GitPath is not null || options.TimeoutSeconds is not null || options.DryRun || options.NonInteractive || options.Quiet)
            throw new ArgumentException("Use config subcommands with --config-dir only.");
        var operands = options.Operands;
        var command = operands.Count >= 2 ? operands[1] : "show";
        if (command == "show")
        {
            if (operands.Count > 2) throw new ArgumentException("Usage: clone config show");
            await output.WriteLineAsync("File: " + (options.ConfigurationDirectory ?? ConfigurationStore.DirectoryPath) + "/config.json");
            await output.WriteLineAsync("Root: " + (configuration.Root ?? "(default or environment)"));
            await output.WriteLineAsync("Layout: " + (configuration.Layout?.ToString().ToLowerInvariant() ?? "(automatic)"));
            await output.WriteLineAsync("Git: " + (configuration.GitPath ?? "(PATH)"));
            await output.WriteLineAsync("Timeout: " + (configuration.TimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? "none"));
            return 0;
        }
        if (command == "reset")
        {
            if (operands.Count != 2) throw new ArgumentException("Usage: clone config reset");
            configuration = new CloneConfiguration();
        }
        else
        {
            if (operands.Count != 3) throw new ArgumentException("A config set command requires one value.");
            configuration = command switch
            {
                "set-root" => configuration with { Root = ExpandPath(operands[2]) },
                "set-layout" => configuration with { Layout = ParseLayout(operands[2]) },
                "set-git" => configuration with { GitPath = Runner.ResolveExecutable("git", ExpandPath(operands[2])) },
                "set-timeout" => configuration with { TimeoutSeconds = operands[2] == "none" ? null : ParseTimeout(operands[2]) },
                _ => throw new ArgumentException("Unknown config command.")
            };
        }
        ConfigurationStore.Save(configuration, options.ConfigurationDirectory);
        await output.WriteLineAsync("Configuration saved. Existing clones were preserved.");
        return 0;
    }

    private static async Task<int> DoctorAsync(CloneRequest request, string source, string? configDirectory, TextWriter output, TextWriter error, CancellationToken token)
    {
        await output.WriteLineAsync("clone " + Version);
        await output.WriteLineAsync("Platform: " + System.Runtime.InteropServices.RuntimeInformation.OSDescription + " / " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        await output.WriteLineAsync("Configuration: " + (configDirectory ?? ConfigurationStore.DirectoryPath));
        await output.WriteLineAsync("Root: " + request.Root + " (" + source + ")");
        await output.WriteLineAsync("Layout: " + request.Layout.ToString().ToLowerInvariant());
        var git = Runner.ResolveExecutable("git", request.GitPath);
        await output.WriteLineAsync("Git path: " + git);
        var result = await Runner.RunAsync(git, ["--version"], Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), timeout: TimeSpan.FromSeconds(5), cancellationToken: token).ConfigureAwait(false);
        if (result.ExitCode != 0) { await error.WriteLineAsync("Git could not run. Check Command Line Tools or your Git installation."); return 1; }
        foreach (var line in result.Diagnostics) await output.WriteLineAsync(line);
        if (Directory.Exists(request.Root) || File.Exists(request.Root) || new DirectoryInfo(request.Root).LinkTarget is not null) ConfigurationStore.InspectRoot(request.Root);
        else await output.WriteLineAsync("Root does not exist; the first clone will create it under the ownership policy.");
        await output.WriteLineAsync("No credentials or network access were tested. No files were changed.");
        return 0;
    }

    private static string ExpandPath(string path)
    {
        if (path.Length == 0 || path.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format)) throw new ArgumentException("Paths must not be empty or contain control characters.");
        if (path == "~") path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        else if (path.StartsWith("~/", StringComparison.Ordinal)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return Path.GetFullPath(path);
    }
    private static CloneLayout ParseLayout(string value) => value switch { "host" => CloneLayout.Host, "legacy" => CloneLayout.Legacy, _ => throw new ArgumentException("Layout must be host or legacy.") };
    private static int ParseTimeout(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 86400 ? number : throw new ArgumentException("Timeout must be 1–86400 seconds, or use config set-timeout none.");

    private sealed class Options
    {
        public string? Root { get; private set; }
        public string? GitPath { get; private set; }
        public string? ConfigurationDirectory { get; private set; }
        public CloneLayout? Layout { get; private set; }
        public int? TimeoutSeconds { get; private set; }
        public bool Quiet { get; private set; }
        public bool NonInteractive { get; private set; }
        public bool DryRun { get; private set; }
        public List<string> Operands { get; } = [];

        public static Options Parse(string[] args)
        {
            if (args.Length is 0 or > 64 || args.Any(x => x.Length > 4096)) throw new ArgumentException("Usage: clone [options] <remote>");
            var options = new Options();
            var seen = new HashSet<string>();
            var literal = false;
            for (var i = 0; i < args.Length; i++)
            {
                var value = args[i];
                if (!literal && value == "--") { literal = true; continue; }
                if (literal || !value.StartsWith('-')) { options.Operands.Add(value); continue; }
                if (!seen.Add(value)) throw new ArgumentException("Duplicate option.");
                string TakeValue() => ++i < args.Length ? args[i] : throw new ArgumentException("An option is missing its value.");
                switch (value)
                {
                    case "--root": options.Root = ExpandPath(TakeValue()); break;
                    case "--git": options.GitPath = ExpandPath(TakeValue()); break;
                    case "--config-dir": options.ConfigurationDirectory = ExpandPath(TakeValue()); break;
                    case "--layout": options.Layout = ParseLayout(TakeValue()); break;
                    case "--timeout": options.TimeoutSeconds = ParseTimeout(TakeValue()); break;
                    case "--quiet": options.Quiet = true; break;
                    case "--non-interactive": options.NonInteractive = true; break;
                    case "--dry-run": options.DryRun = true; break;
                    default: throw new ArgumentException("Unknown option.");
                }
            }
            if (options.Operands.Count == 0 || (options.Operands[0] != "config" && options.Operands.Count != 1)) throw new ArgumentException("Supply exactly one remote or command.");
            if (options.Operands[0] == "doctor" && (options.DryRun || options.Quiet || options.NonInteractive)) throw new ArgumentException("doctor does not accept clone execution flags.");
            return options;
        }
    }

    private const string Help = """
        clone — organize Git repositories safely

        Usage:
          clone [options] <remote>
          clone doctor [--root <path>] [--git <path>]
          clone config [show | reset]
          clone config set-root <path>
          clone config set-layout <host|legacy>
          clone config set-git <absolute executable>
          clone config set-timeout <seconds|none>

        Options:
          --root <path>          Project root (default: ~/Projects)
          --layout <host|legacy> Include host or preserve the legacy namespace layout
          --git <path>           Explicit Git executable
          --timeout <seconds>    Stop after 1–86400 seconds (default: no deadline)
          --dry-run             Print the destination without writing or connecting
          --quiet               Suppress progress; retain errors and the destination
          --non-interactive     Disable authentication prompts (use with --timeout)
          --config-dir <path>   Override the configuration directory
          --help, -h            Show this help without Git or configuration
          --version             Show the version
          --                    End option parsing

        Examples:
          clone https://github.com/owner/repository.git
          clone git@github.com:owner/repository.git
          clone --root "$HOME/Work Projects" ssh://git@example.com/team/sub/repo.git
          clone --dry-run --layout host https://github.com/owner/repository

        Root precedence: command line > CLONE_PROJECT_FOLDER > configuration > default.
        CLONE_PROJECT_FOLDER retains legacy layout unless a layout is explicitly set.
        Output: destination on stdout; progress/errors on stderr. No ANSI decoration.
        Exit codes: 0 success, 1 Git/setup/storage error, 2 usage, 124 timeout, 130 canceled.
        """;
}

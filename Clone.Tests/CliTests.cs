using System.Runtime.Versioning;
using Clone.CommandLine;
using Clone.Git;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clone.Tests;

[TestClass]
[SupportedOSPlatform("macos")]
public sealed class CliTests
{
    private static async Task<(int Code, string Output, string Error)> Run(string[] args, string config, Func<string, string?>? environment = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApplication.RunAsync([.. args, "--config-dir", config], output, error, false, environment: environment ?? (_ => null));
        return (code, output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task HelpAndVersionDoNotNeedConfigurationOrGit()
    {
        foreach (var option in new[] { "--help", "-h", "--version" })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await CliApplication.RunAsync([option], output, error, false, environment: _ => throw new InvalidOperationException());
            Assert.AreEqual(0, code);
            Assert.IsTrue(output.ToString().Contains("clone"));
            Assert.AreEqual("", error.ToString());
        }
    }

    [TestMethod]
    [DataRow("--unknown")]
    [DataRow("--root")]
    [DataRow("--timeout", "0", "https://example.com/team/repo")]
    [DataRow("--layout", "bad", "https://example.com/team/repo")]
    [DataRow("--quiet", "--quiet", "https://example.com/team/repo")]
    [DataRow("https://example.com/team/repo", "extra")]
    public async Task InvalidUsageHasStableExitCode(params string[] args)
    {
        using var fixture = new Fixture();
        var result = await Run(args, Path.Combine(fixture.Root, "config"));
        Assert.AreEqual(2, result.Code);
        Assert.AreEqual("", result.Output);
        Assert.IsTrue(result.Error.Contains("--help"));
    }

    [TestMethod]
    public async Task RootPrecedenceAndLegacyCompatibilityAreExplicit()
    {
        using var fixture = new Fixture();
        var config = fixture.Subdirectory("config");
        ConfigurationStore.Save(new(Path.Combine(fixture.Root, "configured")), config);
        var environmentRoot = Path.Combine(fixture.Root, "environment");
        var cliRoot = Path.Combine(fixture.Root, "command line");
        var args = new[] { "--dry-run", "https://example.com/team/repo" };
        var configured = await Run(args, config);
        Assert.AreEqual(Path.Combine(fixture.Root, "configured/example.com/team/repo") + Environment.NewLine, configured.Output);
        var legacy = await Run(args, config, key => key == "CLONE_PROJECT_FOLDER" ? environmentRoot : null);
        Assert.AreEqual(environmentRoot + "/team/repo" + Environment.NewLine, legacy.Output);
        var explicitRoot = await Run([.. args, "--root", cliRoot, "--layout", "host"], config, key => key == "CLONE_PROJECT_FOLDER" ? environmentRoot : null);
        Assert.AreEqual(cliRoot + "/example.com/team/repo" + Environment.NewLine, explicitRoot.Output);
        Assert.IsFalse(Directory.Exists(cliRoot));
        Assert.AreEqual("", explicitRoot.Error);
    }

    [TestMethod]
    public async Task ConfigurationResetRecoversMalformedData()
    {
        using var fixture = new Fixture();
        var config = fixture.Subdirectory("config");
        File.WriteAllText(Path.Combine(config, "config.json"), "{broken");
        var failed = await Run(["config", "show"], config);
        Assert.AreEqual(1, failed.Code);
        var reset = await Run(["config", "reset"], config);
        Assert.AreEqual(0, reset.Code);
        Assert.AreEqual(new CloneConfiguration(), ConfigurationStore.Load(config));
    }

    [TestMethod]
    public async Task FirstUseClonesWithoutPersistentConfiguration()
    {
        using var fixture = new Fixture();
        var git = await fixture.LocalGit();
        var config = Path.Combine(fixture.Root, "absent config");
        var root = Path.Combine(fixture.Root, "new projects");
        var result = await Run(["--root", root, "--git", git, "--quiet", "https://example.com/team/repo.git"], config);
        Assert.AreEqual(0, result.Code, result.Error);
        Assert.AreEqual(root + "/example.com/team/repo" + Environment.NewLine, result.Output);
        Assert.AreEqual("", result.Error);
        Assert.IsFalse(Directory.Exists(config));
    }

    [TestMethod]
    public async Task FailureNeverEmitsDestinationOrFalseSuccess()
    {
        using var fixture = new Fixture();
        var git = fixture.Script("echo 'fatal: synthetic failure' >&2\nexit 128\n");
        var result = await Run(["--root", fixture.Root, "--git", git, "https://example.com/team/repo"], Path.Combine(fixture.Root, "config"));
        Assert.AreEqual(1, result.Code);
        Assert.AreEqual("", result.Output);
        Assert.IsTrue(result.Error.Contains("exit 128"));
        Assert.IsFalse(result.Error.Contains('\u001b'));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BlockedStderrDoesNotPreventCleanupOrExit(bool cancel)
    {
        using var fixture = new Fixture();
        using var sink = new BlockingWriter();
        using var stdout = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        var root = fixture.Subdirectory("projects");
        var script = fixture.Script("mkdir repository\necho $$ > " + Fixture.Quote(Path.Combine(fixture.Root, "git.pid")) +
            "\necho diagnostic >&2\n/bin/sleep 30\n");
        var operation = Task.Run(() => CliApplication.RunAsync(["--root", root, "--git", script, "--timeout", "2",
            "--config-dir", Path.Combine(fixture.Root, "config"), "https://example.com/team/repo"],
            stdout, sink, false, cancellation.Token, _ => null));
        try
        {
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The preamble can block before Git starts; wait for staging and process evidence.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!File.Exists(Path.Combine(fixture.Root, "git.pid")) && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "git.pid")));
            if (cancel) cancellation.Cancel();
            Assert.AreEqual(cancel ? 130 : 124, await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(0, Directory.GetDirectories(root, ".clone-staging-*").Length);
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "example.com/team/repo")));
            Assert.IsFalse(Fixture.IsRunning(int.Parse(File.ReadAllText(Path.Combine(fixture.Root, "git.pid")))));
            Assert.IsFalse(sink.Released);
        }
        finally
        {
            sink.Release();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            await sink.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task BlockedSuccessStdoutDoesNotPreventExit()
    {
        using var fixture = new Fixture();
        using var sink = new BlockingWriter();
        using var stderr = new StringWriter();
        var script = fixture.Script("mkdir -p repository/.git\ntouch repository/.git/HEAD repository/.git/config\n");
        var operation = Task.Run(() => CliApplication.RunAsync(["--root", fixture.Root, "--git", script,
            "--config-dir", Path.Combine(fixture.Root, "config"), "https://example.com/team/repo"], sink, stderr, false, environment: _ => null));
        try
        {
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(0, Directory.GetDirectories(fixture.Root, ".clone-staging-*").Length);
        }
        finally
        {
            sink.Release();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            await sink.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task DoctorIsReadOnly()
    {
        using var fixture = new Fixture();
        var root = Path.Combine(fixture.Root, "absent");
        var result = await Run(["doctor", "--root", root], Path.Combine(fixture.Root, "config"));
        Assert.AreEqual(0, result.Code, result.Error);
        Assert.IsTrue(result.Output.Contains("Git path:"));
        Assert.IsTrue(result.Output.Contains("No files were changed"));
        Assert.IsFalse(Directory.Exists(root));
    }
}

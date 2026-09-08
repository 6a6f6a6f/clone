using System.Diagnostics;
using System.Runtime.Versioning;
using Clone.Git;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace Clone.Tests;

[TestClass]
public sealed class RemoteTests
{
    [TestMethod]
    [DataRow("https://github.com/team/repo.git", "github.com/team/repo")]
    [DataRow("git@github.com:team/repo.git", "github.com/team/repo")]
    [DataRow("ssh://git@github.com/team/repo.git", "github.com/team/repo")]
    [DataRow("ssh://git@example.com:2222/team/sub/repo.git", "example.com:2222/team/sub/repo")]
    [DataRow("https://example.com/team/caf%C3%A9.git", "example.com/team/café")]
    [DataRow("work-alias:team/repo", "work-alias/team/repo")]
    public void ParsesSupportedForms(string input, string expected) => Assert.AreEqual(expected, RemoteIdentity.Parse(input).Identity);

    [TestMethod]
    [DataRow("https://github.com/../sample.git")]
    [DataRow("git@example.com:/tmp/owner/sample.git")]
    [DataRow("https://github.com/team/")]
    [DataRow("https://github.com/team/repo.git --quiet")]
    [DataRow("https://token@example.com/team/repo")]
    [DataRow("https://example.com/team/%2e%2e")]
    [DataRow("https://example.com/team/a%2fb")]
    [DataRow("https://example.com/team/a%5cb")]
    [DataRow("https://example.com/team/%252e%252e")]
    [DataRow("https://example.com/team/repo?token=secret")]
    [DataRow("https://example.com/team/repo#fragment")]
    [DataRow("https://example.com/team/.git")]
    [DataRow("https://example.com/team/repo%00")]
    [DataRow("https://example.com/team/repo%1b")]
    [DataRow("ssh://user:password@example.com/team/repo")]
    [DataRow("ext::sh -c example")]
    [DataRow("file:///tmp/team/repo")]
    [DataRow("http://example.com/team/repo")]
    [DataRow("git://example.com/team/repo")]
    [DataRow("/tmp/team/repo")]
    [DataRow("--upload-pack=example")]
    [DataRow("https://example.com:0/team/repo")]
    public void RejectsUnsafeForms(string input) => Assert.ThrowsExactly<ArgumentException>(() => RemoteIdentity.Parse(input));

    [TestMethod]
    public void BoundsRemoteAndSegments()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RemoteIdentity.Parse("https://example.com/team/" + new string('a', 101)));
        Assert.ThrowsExactly<ArgumentException>(() => RemoteIdentity.Parse(new string('a', 4097)));
    }

    [TestMethod]
    public void PreviewIsReadOnlyAndPreservesNamespace()
    {
        var root = "/private/tmp/not-created-" + Guid.NewGuid().ToString("N");
        Assert.AreEqual(root + "/example.com/team/sub/repo", CloneService.Preview(new("https://example.com/team/sub/repo", root)));
        Assert.IsFalse(Directory.Exists(root));
        Assert.AreEqual(root + "/team/sub/repo", CloneService.Preview(new("https://example.com/team/sub/repo", root, CloneLayout.Legacy)));
    }

    [TestMethod]
    public void SanitizesTerminalAndCredentials()
    {
        var result = DiagnosticText.Sanitize("\u001b[31mhttps://synthetic:password@example.com/repo?token=fake\u001b[0m\u001b]52;c;data\a");
        Assert.IsFalse(result.Contains("synthetic"));
        Assert.IsFalse(result.Contains("password"));
        Assert.IsFalse(result.Contains("fake"));
        Assert.IsFalse(result.Contains('\u001b'));
        Assert.IsFalse(result.Contains("52;c;data"));
    }
}

[TestClass]
[SupportedOSPlatform("macos")]
public sealed class ProcessTests
{
    [TestMethod]
    public async Task PreservesArgumentsAndReportsExit()
    {
        using var fixture = new Fixture();
        var script = fixture.Script("printf '%s\\n' \"$@\"\nexit 128\n");
        var result = await Runner.RunAsync(script, ["space separated", "quoted\"value", "--quiet", "café", "$(not-executed)"], fixture.Root);
        Assert.AreEqual(128, result.ExitCode);
        CollectionAssert.AreEqual(new[] { "space separated", "quoted\"value", "--quiet", "café", "$(not-executed)" }, result.Diagnostics.ToArray());
    }

    [TestMethod]
    public void ResolvesPathInOrderAndSkipsRelativeEntries()
    {
        using var fixture = new Fixture();
        var first = fixture.Subdirectory("first");
        var second = fixture.Subdirectory("second");
        fixture.Script("exit 0\n", Path.Combine(first, "git"));
        fixture.Script("exit 0\n", Path.Combine(second, "git"));
        Assert.AreEqual(Path.Combine(first, "git"), Runner.ResolveExecutable("git", searchPath: $":.:relative:{first}:{second}"));
        Assert.ThrowsExactly<IOException>(() => Runner.ResolveExecutable("git", searchPath: ":.:relative"));
        Assert.ThrowsExactly<IOException>(() => Runner.ResolveExecutable("git", explicitPath: "git"));
    }

    [TestMethod]
    public async Task BoundsOutputAndPreservesLastDiagnostics()
    {
        using var fixture = new Fixture();
        var script = fixture.Script("/usr/bin/awk 'BEGIN {for(i=0;i<20000;i++) printf \"x\"; print \"\"; for(i=0;i<100;i++) print i}'\n");
        var result = await Runner.RunAsync(script, [], fixture.Root);
        Assert.AreEqual(16, result.Diagnostics.Count);
        Assert.AreEqual("99", result.Diagnostics[^1]);
    }

    [TestMethod]
    public async Task TimeoutTerminatesProcessTree()
    {
        using var fixture = new Fixture();
        var script = fixture.Script("/bin/sleep 30 &\necho $! > child.pid\nwait\n");
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => Runner.RunAsync(script, [], fixture.Root, timeout: TimeSpan.FromSeconds(5)));
        var pid = int.Parse(File.ReadAllText(Path.Combine(fixture.Root, "child.pid")));
        await Task.Delay(100);
        Assert.IsFalse(Fixture.IsRunning(pid));
    }

    [TestMethod]
    public async Task FailingOutputConsumerStopsTheProcess()
    {
        using var fixture = new Fixture();
        var script = fixture.Script("echo output\n/bin/sleep 30\n");
        await Assert.ThrowsExactlyAsync<IOException>(() => Runner.RunAsync(script, [], fixture.Root,
            output: (_, _) => throw new InvalidOperationException("synthetic callback failure"), timeout: TimeSpan.FromSeconds(3)));
    }

    [TestMethod]
    public async Task CancellationIsNotTimeout()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource(200);
        var script = fixture.Script("/bin/sleep 30\n");
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Runner.RunAsync(script, [], fixture.Root, cancellationToken: cancellation.Token));
    }
}

[TestClass]
[SupportedOSPlatform("macos")]
public sealed class StorageTests
{
    [TestMethod]
    public async Task FailedGitDoesNotPublishAndCleansOwnedFiles()
    {
        using var fixture = new Fixture();
        var root = fixture.Subdirectory("projects with spaces");
        var outside = fixture.Subdirectory("outside");
        File.WriteAllText(Path.Combine(outside, "sentinel"), "keep");
        var script = fixture.Script("mkdir repository\nln -s " + Fixture.Quote(outside) + " repository/link\nprintf 'fatal: synthetic failure\\n' >&2\nexit 128\n");
        await Assert.ThrowsExactlyAsync<GitFailedException>(() => CloneService.CloneAsync(new("https://example.com/team/repo", root, GitPath: script)));
        Assert.IsTrue(File.Exists(Path.Combine(outside, "sentinel")));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "example.com/team/repo")));
        Assert.AreEqual(0, Directory.GetDirectories(root, ".clone-staging-*").Length);
    }

    [TestMethod]
    public async Task RejectsSymlinkNamespaceWithoutTouchingOutside()
    {
        using var fixture = new Fixture();
        var root = fixture.Subdirectory("projects");
        var outside = fixture.Subdirectory("outside");
        Directory.CreateSymbolicLink(Path.Combine(root, "example.com"), outside);
        var script = fixture.Script("touch SHOULD_NOT_RUN\n");
        await Assert.ThrowsExactlyAsync<IOException>(() => CloneService.CloneAsync(new("https://example.com/team/repo", root, GitPath: script)));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(outside).Length);
    }

    [TestMethod]
    public async Task RejectsSharedWritableRoot()
    {
        using var fixture = new Fixture();
        var root = fixture.Subdirectory("shared");
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);
        await Assert.ThrowsExactlyAsync<IOException>(() => CloneService.CloneAsync(new("https://example.com/team/repo", root, GitPath: fixture.Script("exit 0\n"))));
    }

    [TestMethod]
    public async Task RejectsDanglingDestinationAndPreservesIt()
    {
        using var fixture = new Fixture();
        var root = fixture.Subdirectory("projects");
        var parent = Path.Combine(root, "example.com/team");
        Directory.CreateDirectory(parent);
        File.CreateSymbolicLink(Path.Combine(parent, "repo"), "/nonexistent/clone-test");
        await Assert.ThrowsExactlyAsync<IOException>(() => CloneService.CloneAsync(new("https://example.com/team/repo", root, GitPath: fixture.Script("exit 0\n"))));
        Assert.IsNotNull(new FileInfo(Path.Combine(parent, "repo")).LinkTarget);
    }

    [TestMethod]
    public async Task RequiresExpectedRepositoryAfterExitZero()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsExactlyAsync<IOException>(() => CloneService.CloneAsync(new("https://example.com/team/repo", fixture.Root, GitPath: fixture.Script("exit 0\n"))));
        Assert.AreEqual(0, Directory.GetDirectories(fixture.Root, ".clone-staging-*").Length);
    }

    [TestMethod]
    public async Task ClonesWithRealGitAndDoesNotReplaceAnExistingClone()
    {
        using var fixture = new Fixture();
        var wrapper = await fixture.LocalGit();
        var root = fixture.Subdirectory("projects with spaces");
        var request = new CloneRequest("https://example.com/team/repo.git", root, GitPath: wrapper, Interactive: false);
        var path = await CloneService.CloneAsync(request);
        Assert.AreEqual("fixture", File.ReadAllText(Path.Combine(path, "README")));
        File.WriteAllText(Path.Combine(path, "sentinel"), "keep");
        await Assert.ThrowsExactlyAsync<IOException>(() => CloneService.CloneAsync(request));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(path, "sentinel")));
        Assert.AreEqual(0, Directory.GetDirectories(root, ".clone-staging-*").Length);
    }

    [TestMethod]
    public async Task ConcurrentClonesHaveOneWinner()
    {
        using var fixture = new Fixture();
        var wrapper = await fixture.LocalGit();
        var root = fixture.Subdirectory("projects");
        async Task<bool> TryClone()
        {
            try { await CloneService.CloneAsync(new("https://example.com/team/repo.git", root, GitPath: wrapper)); return true; }
            catch (IOException) { return false; }
        }
        var outcomes = await Task.WhenAll(TryClone(), TryClone());
        Assert.AreEqual(1, outcomes.Count(x => x));
        Assert.AreEqual("fixture", File.ReadAllText(Path.Combine(root, "example.com/team/repo/README")));
        Assert.AreEqual(0, Directory.GetDirectories(root, ".clone-staging-*").Length);
    }

    [TestMethod]
    public async Task ProductionPolicyRejectsFileTransportRewrite()
    {
        using var fixture = new Fixture();
        var wrapper = await fixture.LocalGit(allowLocal: false);
        await Assert.ThrowsExactlyAsync<GitFailedException>(() => CloneService.CloneAsync(new("https://example.com/team/repo.git", fixture.Subdirectory("projects"), GitPath: wrapper)));
    }

    [TestMethod]
    public void ConfigurationIsAtomicAndRejectsSymlinks()
    {
        using var fixture = new Fixture();
        var config = new CloneConfiguration(fixture.Subdirectory("projects"), CloneLayout.Legacy, TimeoutSeconds: 20);
        ConfigurationStore.Save(config, fixture.Subdirectory("config"));
        Assert.AreEqual(config, ConfigurationStore.Load(Path.Combine(fixture.Root, "config")));
        var configPath = Path.Combine(fixture.Root, "config/config.json");
        File.Delete(configPath);
        File.CreateSymbolicLink(configPath, Path.Combine(fixture.Root, "outside"));
        Assert.ThrowsExactly<IOException>(() => ConfigurationStore.Load(Path.Combine(fixture.Root, "config")));
    }
}

[SupportedOSPlatform("macos")]
internal sealed class Fixture : IDisposable
{
    public string Root { get; } = "/private/tmp/clone-tests-" + Guid.NewGuid().ToString("N");
    public Fixture() { Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    public string Subdirectory(string name) { var path = Path.Combine(Root, name); Directory.CreateDirectory(path); return path; }
    public string Script(string body, string? path = null)
    {
        path ??= Path.Combine(Root, "script-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "#!/bin/sh\nset -eu\n" + body);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    public async Task<string> LocalGit(bool allowLocal = true)
    {
        var git = Runner.ResolveExecutable("git");
        var source = Subdirectory("source/team/repo.git");
        var env = new Dictionary<string, string?> { ["GIT_CONFIG_GLOBAL"] = "/dev/null", ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_COUNT"] = null, ["GIT_CONFIG_PARAMETERS"] = null };
        async Task Run(params string[] args)
        {
            var result = await Runner.RunAsync(git, args, source, environment: env);
            Assert.AreEqual(0, result.ExitCode, string.Join('\n', result.Diagnostics));
        }
        await Run("init", "--template=", "-b", "main");
        File.WriteAllText(Path.Combine(source, "README"), "fixture");
        await Run("add", "README");
        await Run("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "-m", "test: add fixture");
        return Script("export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1\n" +
            (allowLocal ? "unset GIT_ALLOW_PROTOCOL\n" : "") + "exec " + Quote(git) + " " +
            (allowLocal ? "-c protocol.file.allow=always " : "") + "-c " + Quote("url." + Root + "/source/.insteadOf=https://example.com/") + " \"$@\"\n");
    }
    public static string Quote(string text) => "'" + text.Replace("'", "'\\''") + "'";
    public static bool IsRunning(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    public void Dispose() { Directory.Delete(Root, true); }
}

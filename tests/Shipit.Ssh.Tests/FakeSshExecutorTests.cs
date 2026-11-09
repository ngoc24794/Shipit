using Shipit.Core;
using Shipit.Ssh.Tests.TestDoubles;

namespace Shipit.Ssh.Tests;

public class FakeSshExecutorTests
{
    private readonly SshTarget _target = new(
        Host: "192.168.1.49",
        Port: 22,
        User: "devops",
        PrivateKeyPem: "dummy-key");

    [Fact]
    public async Task ExecuteAsync_MatchingRule_ProducesConfiguredOutputAndExitCode()
    {
        var fake = new FakeSshExecutor();
        fake.AddRule(
            cmd => cmd.CommandText.StartsWith("docker build"),
            exitCode: 42,
            outputLines:
            [
                ("Step 1/1", LogStream.Stdout),
                ("Warning: something", LogStream.Stderr)
            ]);

        var sink = new TestLogSink();
        var command = new RemoteCommand("docker build -t my-app .");
        var exitCode = await fake.ExecuteAsync(_target, command, sink);

        Assert.Equal(42, exitCode);
        Assert.Equal(["Step 1/1"], sink.StdoutLines);
        Assert.Equal(["Warning: something"], sink.StderrLines);
    }

    [Fact]
    public async Task ExecuteAsync_UnmatchedRule_ReturnsZeroWhenNotStrict()
    {
        var fake = new FakeSshExecutor { StrictMode = false };
        var sink = new TestLogSink();

        var exitCode = await fake.ExecuteAsync(_target, new RemoteCommand("unknown command"), sink);

        Assert.Equal(0, exitCode);
        Assert.Empty(sink.Lines);
    }

    [Fact]
    public async Task ExecuteAsync_UnmatchedRule_ThrowsWhenStrict()
    {
        var fake = new FakeSshExecutor { StrictMode = true };
        var sink = new TestLogSink();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fake.ExecuteAsync(_target, new RemoteCommand("unmatched"), sink));
    }

    [Fact]
    public async Task ExecuteAsync_RecordsCommandsInOrder_WithoutStoringStdinSecretValue()
    {
        var fake = new FakeSshExecutor();
        var sink = new TestLogSink();

        var secret1 = "secret-token-12345";
        var secret2 = "another-registry-password";

        await fake.ExecuteAsync(_target, new RemoteCommand("cmd 1", StdinSecret: secret1), sink);
        await fake.ExecuteAsync(_target, new RemoteCommand("cmd 2"), sink);
        await fake.ExecuteAsync(_target, new RemoteCommand("cmd 3", StdinSecret: secret2), sink);

        var executed = fake.ExecutedCommands;
        Assert.Equal(3, executed.Count);

        Assert.Equal("cmd 1", executed[0].CommandText);
        Assert.True(executed[0].HasStdin);
        Assert.Equal(secret1.Length, executed[0].StdinLength);

        Assert.Equal("cmd 2", executed[1].CommandText);
        Assert.False(executed[1].HasStdin);
        Assert.Equal(0, executed[1].StdinLength);

        Assert.Equal("cmd 3", executed[2].CommandText);
        Assert.True(executed[2].HasStdin);
        Assert.Equal(secret2.Length, executed[2].StdinLength);

        // Assert secret values are NOT contained in ExecutedCommands
        foreach (var cmd in executed)
        {
            Assert.DoesNotContain(secret1, cmd.ToString());
            Assert.DoesNotContain(secret2, cmd.ToString());
        }
    }

    [Fact]
    public async Task HappyPath_SimulatesEntireBuildFlowSuccessfully()
    {
        var fake = FakeSshExecutor.HappyPath();
        fake.StrictMode = true; // HappyPath should match all expected pipeline commands!

        var sink = new TestLogSink();

        // 1. Clone
        var cloneCmd = RemoteCommandBuilder.Clone("https://github.com/org/repo.git", "main", "/tmp/shipit/run-1", "token");
        var cloneExit = await fake.ExecuteAsync(_target, cloneCmd, sink);
        Assert.Equal(0, cloneExit);

        // 2. ShortSha
        var shaSink = new TestLogSink();
        var shaCmd = RemoteCommandBuilder.ShortSha("/tmp/shipit/run-1");
        var shaExit = await fake.ExecuteAsync(_target, shaCmd, shaSink);
        Assert.Equal(0, shaExit);
        Assert.Contains("abc1234", shaSink.StdoutLines);

        // 3. Docker Login
        var loginCmd = RemoteCommandBuilder.DockerLogin("192.168.1.241", "user", "pass");
        var loginExit = await fake.ExecuteAsync(_target, loginCmd, sink);
        Assert.Equal(0, loginExit);

        // 4. Docker Build
        var buildCmd = RemoteCommandBuilder.DockerBuild("/tmp/shipit/run-1", "Dockerfile", "192.168.1.241/app:abc1234");
        var buildExit = await fake.ExecuteAsync(_target, buildCmd, sink);
        Assert.Equal(0, buildExit);

        // 5. Docker Push
        var pushCmd = RemoteCommandBuilder.DockerPush("192.168.1.241/app:abc1234");
        var pushExit = await fake.ExecuteAsync(_target, pushCmd, sink);
        Assert.Equal(0, pushExit);

        // 6. Docker Logout
        var logoutCmd = RemoteCommandBuilder.DockerLogout("192.168.1.241");
        var logoutExit = await fake.ExecuteAsync(_target, logoutCmd, sink);
        Assert.Equal(0, logoutExit);

        // 7. Cleanup
        var cleanupCmd = RemoteCommandBuilder.Cleanup("/tmp/shipit/run-1");
        var cleanupExit = await fake.ExecuteAsync(_target, cleanupCmd, sink);
        Assert.Equal(0, cleanupExit);

        // 8. Test connection
        var (ok, _) = await fake.TestConnectionAsync(_target);
        Assert.True(ok);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsConfiguredDelay()
    {
        var fake = new FakeSshExecutor();
        fake.AddRule(
            cmd => cmd.CommandText == "delayed",
            exitCode: 0,
            outputLines: [("done", LogStream.Stdout)],
            delay: TimeSpan.FromMilliseconds(50));

        var sink = new TestLogSink();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var exit = await fake.ExecuteAsync(_target, new RemoteCommand("delayed"), sink);
        sw.Stop();

        Assert.Equal(0, exit);
        Assert.True(sw.ElapsedMilliseconds >= 30, $"Expected delay of ~50ms, took {sw.ElapsedMilliseconds}ms");
        Assert.Equal(["done"], sink.StdoutLines);
    }

    [Fact]
    public async Task TestConnectionAsync_DefaultReturnsOk_AndHonorsCustomHandler()
    {
        var fake = new FakeSshExecutor();
        var (okDefault, msgDefault) = await fake.TestConnectionAsync(_target);
        Assert.True(okDefault);
        Assert.Equal("OK", msgDefault);

        fake.TestConnectionHandler = (target, ct) => Task.FromResult((false, "Custom connection failure"));
        var (okCustom, msgCustom) = await fake.TestConnectionAsync(_target);
        Assert.False(okCustom);
        Assert.Equal("Custom connection failure", msgCustom);
    }
}

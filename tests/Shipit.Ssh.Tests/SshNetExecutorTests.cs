using Shipit.Core;
using Shipit.Ssh.Tests.TestDoubles;

namespace Shipit.Ssh.Tests;

public class SshNetExecutorTests
{
    private readonly SshTarget _defaultTarget = new(
        Host: "192.168.1.49",
        Port: 22,
        User: "devops",
        PrivateKeyPem: "-----BEGIN RSA PRIVATE KEY-----\nMIIEogIBAAKCAQEA0fakekey...\n-----END RSA PRIVATE KEY-----",
        Passphrase: "fake-passphrase");

    [Fact]
    public async Task ExecuteAsync_StreamsStdoutAndStderr_LineByLine()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);
        var sink = new TestLogSink();

        var stdoutText = "line 1\nline 2\r\nline 3\n";
        var stderrText = "warning 1\nwarning 2\n";

        factory.ConnectHandler = (target, ct) =>
        {
            var conn = new MockSshConnection();
            conn.CommandHandler = cmd => new MockSshCommandRunner(cmd, stdout: stdoutText, stderr: stderrText, exitCode: 0);
            return Task.FromResult<Internal.ISshConnection>(conn);
        };

        var command = new RemoteCommand("echo test");
        var exitCode = await executor.ExecuteAsync(_defaultTarget, command, sink);

        Assert.Equal(0, exitCode);
        Assert.Equal(["line 1", "line 2", "line 3"], sink.StdoutLines);
        Assert.Equal(["warning 1", "warning 2"], sink.StderrLines);
    }

    [Fact]
    public async Task ExecuteAsync_TrailingLineWithoutNewline_IsLogged()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);
        var sink = new TestLogSink();

        factory.ConnectHandler = (target, ct) =>
        {
            var conn = new MockSshConnection();
            conn.CommandHandler = cmd => new MockSshCommandRunner(cmd, stdout: "line 1\nlast line no newline", exitCode: 0);
            return Task.FromResult<Internal.ISshConnection>(conn);
        };

        var command = new RemoteCommand("echo test");
        var exitCode = await executor.ExecuteAsync(_defaultTarget, command, sink);

        Assert.Equal(0, exitCode);
        Assert.Equal(["line 1", "last line no newline"], sink.StdoutLines);
    }

    [Fact]
    public async Task ExecuteAsync_DeliversStdinSecret_WithoutLoggingIt()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);
        var sink = new TestLogSink();

        MockSshCommandRunner? executedRunner = null;
        factory.ConnectHandler = (target, ct) =>
        {
            var conn = new MockSshConnection();
            conn.CommandHandler = cmd =>
            {
                executedRunner = new MockSshCommandRunner(cmd, stdout: "OK", exitCode: 0);
                return executedRunner;
            };
            return Task.FromResult<Internal.ISshConnection>(conn);
        };

        var secret = "super_secret_token_12345";
        var command = new RemoteCommand("read_stdin_command", StdinSecret: secret);

        var exitCode = await executor.ExecuteAsync(_defaultTarget, command, sink);

        Assert.Equal(0, exitCode);
        Assert.NotNull(executedRunner);
        Assert.Equal(secret + "\n", executedRunner.GetReceivedStdin());

        // Assert secret was NEVER sent to the log sink
        foreach (var (line, _) in sink.Lines)
        {
            Assert.DoesNotContain(secret, line);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandTimesOut_CancelsCommandReturns124AndLogsTimedOut()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);
        var sink = new TestLogSink();

        MockSshCommandRunner? runner = null;
        factory.ConnectHandler = (target, ct) =>
        {
            var conn = new MockSshConnection();
            conn.CommandHandler = cmd =>
            {
                runner = new MockSshCommandRunner(cmd)
                {
                    ExecutionDelay = TimeSpan.FromSeconds(5),
                };
                return runner;
            };
            return Task.FromResult<Internal.ISshConnection>(conn);
        };

        var command = new RemoteCommand("sleep 100", Timeout: TimeSpan.FromMilliseconds(50));
        var exitCode = await executor.ExecuteAsync(_defaultTarget, command, sink);

        Assert.Equal(124, exitCode);
        Assert.NotNull(runner);
        Assert.True(runner.Cancelled);
        Assert.Contains(sink.SystemLines, l => l.Contains("timed out"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCallerCancels_ThrowsOperationCanceledExceptionAndDisposesConnection()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);
        var sink = new TestLogSink();

        MockSshConnection? connection = null;
        factory.ConnectHandler = (target, ct) =>
        {
            connection = new MockSshConnection();
            connection.CommandHandler = cmd => new MockSshCommandRunner(cmd)
            {
                ExecutionDelay = TimeSpan.FromSeconds(10),
            };
            return Task.FromResult<Internal.ISshConnection>(connection);
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var command = new RemoteCommand("long running command");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(_defaultTarget, command, sink, cts.Token));

        Assert.NotNull(connection);
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public async Task TestConnectionAsync_Success_ReturnsOkTrueWithVersion()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);

        factory.ConnectHandler = (target, ct) =>
        {
            var conn = new MockSshConnection();
            conn.CommandHandler = cmd =>
            {
                if (cmd.Contains("echo shipit-ok"))
                {
                    return new MockSshCommandRunner(cmd, stdout: "shipit-ok\n", exitCode: 0);
                }
                if (cmd.Contains("docker version"))
                {
                    return new MockSshCommandRunner(cmd, stdout: "24.0.7\n", exitCode: 0);
                }
                return new MockSshCommandRunner(cmd, exitCode: 0);
            };
            return Task.FromResult<Internal.ISshConnection>(conn);
        };

        var (ok, message) = await executor.TestConnectionAsync(_defaultTarget);

        Assert.True(ok);
        Assert.Contains("OK", message);
        Assert.Contains("24.0.7", message);
    }

    [Fact]
    public async Task TestConnectionAsync_DockerFailure_ReturnsOkFalseWithoutLeakingKeys()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);

        factory.ConnectHandler = (target, ct) =>
        {
            var conn = new MockSshConnection();
            conn.CommandHandler = cmd =>
            {
                if (cmd.Contains("echo shipit-ok"))
                {
                    return new MockSshCommandRunner(cmd, stdout: "shipit-ok\n", exitCode: 0);
                }
                if (cmd.Contains("docker version"))
                {
                    return new MockSshCommandRunner(cmd, stderr: "Cannot connect to the Docker daemon", exitCode: 1);
                }
                return new MockSshCommandRunner(cmd, exitCode: 0);
            };
            return Task.FromResult<Internal.ISshConnection>(conn);
        };

        var (ok, message) = await executor.TestConnectionAsync(_defaultTarget);

        Assert.False(ok);
        Assert.Contains("Docker check failed", message);
        Assert.DoesNotContain(_defaultTarget.PrivateKeyPem, message);
        Assert.DoesNotContain(_defaultTarget.Passphrase!, message);
    }

    [Fact]
    public async Task TestConnectionAsync_ConnectionFailure_ReturnsOkFalseWithoutLeakingKeys()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);

        factory.ConnectHandler = (target, ct) =>
        {
            throw new InvalidOperationException($"Auth failed with key {_defaultTarget.PrivateKeyPem} and {_defaultTarget.Passphrase}");
        };

        var (ok, message) = await executor.TestConnectionAsync(_defaultTarget);

        Assert.False(ok);
        Assert.Contains("SSH connection failed", message);
        Assert.DoesNotContain(_defaultTarget.PrivateKeyPem, message);
        Assert.DoesNotContain(_defaultTarget.Passphrase!, message);
    }

    [Fact]
    public async Task ExecuteAsync_CommandWithoutStdinSecret_DoesNotCreateInputStream()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);
        var sink = new TestLogSink();

        MockSshCommandRunner? executedRunner = null;
        factory.ConnectHandler = (target, ct) =>
        {
            var conn = new MockSshConnection();
            conn.CommandHandler = cmd =>
            {
                executedRunner = new MockSshCommandRunner(cmd, stdout: "output", exitCode: 0);
                return executedRunner;
            };
            return Task.FromResult<Internal.ISshConnection>(conn);
        };

        var command = new RemoteCommand("simple_command", StdinSecret: null);
        var exitCode = await executor.ExecuteAsync(_defaultTarget, command, sink);

        Assert.Equal(0, exitCode);
        Assert.NotNull(executedRunner);
        Assert.Equal("", executedRunner.GetReceivedStdin());
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExitCode_ReturnsCorrectExitCode()
    {
        var factory = new MockSshConnectionFactory();
        var executor = new SshNetExecutor(factory);
        var sink = new TestLogSink();

        factory.ConnectHandler = (target, ct) =>
        {
            var conn = new MockSshConnection();
            conn.CommandHandler = cmd => new MockSshCommandRunner(cmd, stdout: "some output", stderr: "error occurred", exitCode: 42);
            return Task.FromResult<Internal.ISshConnection>(conn);
        };

        var command = new RemoteCommand("failing_command");
        var exitCode = await executor.ExecuteAsync(_defaultTarget, command, sink);

        Assert.Equal(42, exitCode);
        Assert.Equal(["some output"], sink.StdoutLines);
        Assert.Equal(["error occurred"], sink.StderrLines);
    }

    [Fact]
    public async Task SshNetConnectionFactory_ThrowsWhenKeyIsEmpty()
    {
        var factory = new Internal.SshNetConnectionFactory();
        var invalidTarget = _defaultTarget with { PrivateKeyPem = "" };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.ConnectAsync(invalidTarget, CancellationToken.None));
    }

    [Fact]
    public async Task SshNetConnectionFactory_ThrowsWhenKeyIsInvalidPem()
    {
        var factory = new Internal.SshNetConnectionFactory();
        var invalidTarget = _defaultTarget with { PrivateKeyPem = "invalid pem content" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.ConnectAsync(invalidTarget, CancellationToken.None));
        Assert.Contains("Invalid private key format", ex.Message);
    }
}

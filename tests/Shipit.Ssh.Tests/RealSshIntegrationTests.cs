using Shipit.Core;
using Shipit.Ssh.Tests.TestDoubles;

namespace Shipit.Ssh.Tests;

public class RealSshIntegrationTests
{
    [SkippableFact]
    public async Task RealSshServer_Execute_WhenEnvVarsConfigured()
    {
        var host = Environment.GetEnvironmentVariable("SHIPIT_TEST_SSH_HOST");
        var user = Environment.GetEnvironmentVariable("SHIPIT_TEST_SSH_USER");
        var keyFile = Environment.GetEnvironmentVariable("SHIPIT_TEST_SSH_KEY_FILE");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile))
        {
            // Skip when env vars are not set
            return;
        }

        var keyPem = await File.ReadAllTextAsync(keyFile);
        var target = new SshTarget(host, 22, user, keyPem);

        var executor = new SshNetExecutor();
        var sink = new TestLogSink();

        var (ok, msg) = await executor.TestConnectionAsync(target);
        Assert.True(ok, $"TestConnection failed: {msg}");

        var exitCode = await executor.ExecuteAsync(target, new RemoteCommand("echo live-ssh-test"), sink);
        Assert.Equal(0, exitCode);
        Assert.Contains(sink.StdoutLines, l => l.Contains("live-ssh-test"));
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class SkippableFactAttribute : FactAttribute
{
    public SkippableFactAttribute()
    {
        var host = Environment.GetEnvironmentVariable("SHIPIT_TEST_SSH_HOST");
        var user = Environment.GetEnvironmentVariable("SHIPIT_TEST_SSH_USER");
        var keyFile = Environment.GetEnvironmentVariable("SHIPIT_TEST_SSH_KEY_FILE");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile))
        {
            Skip = "Skipped: Real SSH tests require SHIPIT_TEST_SSH_HOST, SHIPIT_TEST_SSH_USER, and SHIPIT_TEST_SSH_KEY_FILE.";
        }
    }
}

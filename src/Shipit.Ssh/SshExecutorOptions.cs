using Renci.SshNet.Common;

namespace Shipit.Ssh;

/// <summary>
/// Options for configuring SSH client connections.
/// </summary>
public sealed class SshExecutorOptions
{
    /// <summary>
    /// Optional validator for SSH host keys.
    /// In MVP, when this callback is null, all host keys are accepted without known_hosts verification.
    /// In production environments, configure this callback to verify host key fingerprints against trusted known_hosts.
    /// </summary>
    public Func<HostKeyEventArgs, bool>? HostKeyValidator { get; init; }

    /// <summary>
    /// Connection timeout when establishing SSH session. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for internal SSH.NET session operations (channel open, exec request, re-keying).
    /// SSH.NET uses <see cref="Renci.SshNet.ConnectionInfo.Timeout"/> for all session WaitOnHandle calls,
    /// so keeping this larger than <see cref="ConnectTimeout"/> prevents transient 30s session timeouts
    /// on busy build slaves. Defaults to 2 minutes.
    /// </summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Interval for sending SSH keep-alive packets on active connections.
    /// Defaults to 30 seconds. Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of retries when command execution fails with a transient
    /// <see cref="SshOperationTimeoutException"/> before command or caller timeout. Defaults to 1.
    /// </summary>
    public int TransientTimeoutRetries { get; init; } = 1;

    /// <summary>
    /// Delay before retrying after a transient <see cref="SshOperationTimeoutException"/>. Defaults to 2 seconds.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);
}

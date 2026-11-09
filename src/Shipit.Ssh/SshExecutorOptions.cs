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
}

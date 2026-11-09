namespace Shipit.Core;

/// <summary>Stream a log line came from.</summary>
public enum LogStream
{
    Stdout,
    Stderr,
    System,
}

/// <summary>Receives log lines produced while a pipeline runs. Implementations must be thread-safe.</summary>
public interface ILogSink
{
    ValueTask WriteAsync(string line, LogStream stream, CancellationToken cancellationToken = default);
}

/// <summary>Connection details for a build slave. <see cref="PrivateKeyPem"/> is already decrypted; never log it.</summary>
public sealed record SshTarget(string Host, int Port, string User, string PrivateKeyPem, string? Passphrase = null);

/// <summary>
/// A command to run on the slave. <see cref="StdinSecret"/> (if any) is written to the command's stdin and
/// must never appear in <see cref="CommandText"/> (argv) or in logs.
/// </summary>
public sealed record RemoteCommand(string CommandText, string? StdinSecret = null, TimeSpan? Timeout = null);

/// <summary>Runs commands on a build slave over SSH.</summary>
public interface ISshExecutor
{
    /// <summary>Runs <paramref name="command"/>, streaming output line by line to <paramref name="output"/>. Returns the exit code.</summary>
    Task<int> ExecuteAsync(SshTarget target, RemoteCommand command, ILogSink output, CancellationToken cancellationToken = default);

    /// <summary>Opens a connection and runs a trivial command to verify the target is usable.</summary>
    Task<(bool Ok, string Message)> TestConnectionAsync(SshTarget target, CancellationToken cancellationToken = default);
}

/// <summary>Sets the image of one container of one Deployment and waits for rollout.</summary>
public sealed record DeployRequest(string Namespace, string Deployment, string Container, string Image, TimeSpan RolloutTimeout);

public sealed record DeployResult(bool Success, string Message);

public interface IK8sDeployer
{
    /// <summary>Must reject targets outside the allowlist without calling the cluster.</summary>
    Task<DeployResult> SetImageAsync(DeployRequest request, ILogSink log, CancellationToken cancellationToken = default);
}

/// <summary>Encrypts and decrypts secrets stored in the database.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <exception cref="System.Security.Cryptography.CryptographicException">Value was tampered with or key is wrong.</exception>
    string Unprotect(string protectedValue);
}

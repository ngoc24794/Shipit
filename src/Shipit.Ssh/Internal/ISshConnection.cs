using Shipit.Core;

namespace Shipit.Ssh.Internal;

/// <summary>
/// Factory interface for creating SSH connections. Placed behind an adapter to allow testing without live SSH servers.
/// </summary>
public interface ISshConnectionFactory
{
    /// <summary>
    /// Opens an SSH connection to the specified target.
    /// </summary>
    Task<ISshConnection> ConnectAsync(SshTarget target, CancellationToken cancellationToken);
}

/// <summary>
/// Represents an active SSH connection to a remote host.
/// </summary>
public interface ISshConnection : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Creates a command runner for executing a shell command on the remote host.
    /// </summary>
    ISshCommandRunner CreateCommand(string commandText);
}

/// <summary>
/// Represents a command execution on an SSH connection.
/// </summary>
public interface ISshCommandRunner : IDisposable
{
    /// <summary>
    /// Command execution timeout.
    /// </summary>
    TimeSpan CommandTimeout { get; set; }

    /// <summary>
    /// Creates or returns the input stream to deliver stdin data to the running command.
    /// </summary>
    Stream? CreateInputStream();

    /// <summary>
    /// Stream providing stdout data from the running command.
    /// </summary>
    Stream OutputStream { get; }

    /// <summary>
    /// Stream providing stderr data from the running command.
    /// </summary>
    Stream ExtendedOutputStream { get; }

    /// <summary>
    /// Executes the command asynchronously and returns the remote exit code.
    /// </summary>
    Task<int> ExecuteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Cancels the running command.
    /// </summary>
    void Cancel();
}

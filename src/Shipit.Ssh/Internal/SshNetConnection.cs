using System.Text;
using Renci.SshNet;
using Shipit.Core;

namespace Shipit.Ssh.Internal;

/// <summary>
/// Production SSH.NET connection factory that reads PEM keys from memory.
/// </summary>
public sealed class SshNetConnectionFactory : ISshConnectionFactory
{
    private readonly SshExecutorOptions _options;

    public SshNetConnectionFactory(SshExecutorOptions? options = null)
    {
        _options = options ?? new SshExecutorOptions();
    }

    public async Task<ISshConnection> ConnectAsync(SshTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.PrivateKeyPem))
        {
            throw new ArgumentException("Private key PEM must not be empty.", nameof(target));
        }

        PrivateKeyFile keyFile;
        try
        {
            // Read private key directly from memory stream — never written to disk
            var keyBytes = Encoding.UTF8.GetBytes(target.PrivateKeyPem);
            using var ms = new MemoryStream(keyBytes);
            keyFile = string.IsNullOrEmpty(target.Passphrase)
                ? new PrivateKeyFile(ms)
                : new PrivateKeyFile(ms, target.Passphrase);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid private key format: {ex.Message}", ex);
        }

        var authMethod = new PrivateKeyAuthenticationMethod(target.User, keyFile);
        var connectionInfo = new ConnectionInfo(target.Host, target.Port, target.User, authMethod)
        {
            Timeout = _options.ConnectTimeout,
        };

        var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (sender, e) =>
        {
            if (_options.HostKeyValidator != null)
            {
                e.CanTrust = _options.HostKeyValidator(e);
            }
            else
            {
                // MVP: accept all host keys without known_hosts verification.
                // In production, configure HostKeyValidator to verify fingerprints against known_hosts.
                e.CanTrust = true;
            }
        };

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            keyFile.Dispose();
            throw;
        }

        return new SshNetConnection(client, keyFile);
    }
}

/// <summary>
/// Production SSH.NET connection wrapper.
/// </summary>
public sealed class SshNetConnection : ISshConnection
{
    private readonly SshClient _client;
    private readonly PrivateKeyFile _keyFile;
    private bool _disposed;

    public SshNetConnection(SshClient client, PrivateKeyFile keyFile)
    {
        _client = client;
        _keyFile = keyFile;
    }

    public ISshCommandRunner CreateCommand(string commandText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var cmd = _client.CreateCommand(commandText);
        return new SshNetCommandRunner(cmd);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        catch
        {
            // Ignore disconnect errors during disposal
        }
        _client.Dispose();
        _keyFile.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Production SSH.NET command runner wrapping <see cref="SshCommand"/>.
/// </summary>
public sealed class SshNetCommandRunner : ISshCommandRunner
{
    private readonly SshCommand _command;

    public SshNetCommandRunner(SshCommand command)
    {
        _command = command;
    }

    public TimeSpan CommandTimeout
    {
        get => _command.CommandTimeout;
        set => _command.CommandTimeout = value;
    }

    public Stream? CreateInputStream()
    {
        return _command.CreateInputStream();
    }

    public Stream OutputStream => _command.OutputStream;

    public Stream ExtendedOutputStream => _command.ExtendedOutputStream;

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        await _command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return _command.ExitStatus ?? 0;
    }

    public void Cancel()
    {
        try
        {
            _command.CancelAsync(forceKill: true, 5000);
        }
        catch
        {
            // Ignore cancel errors
        }
    }

    public void Dispose()
    {
        _command.Dispose();
    }
}

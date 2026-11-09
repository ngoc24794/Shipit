using System.Text;
using Shipit.Core;
using Shipit.Ssh.Internal;

namespace Shipit.Ssh;

/// <summary>
/// Executes remote commands over SSH using SSH.NET (Renci.SshNet).
/// </summary>
public class SshNetExecutor : ISshExecutor
{
    private readonly ISshConnectionFactory _connectionFactory;
    private readonly SshExecutorOptions _options;

    public SshNetExecutor(ISshConnectionFactory connectionFactory, SshExecutorOptions? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options ?? new SshExecutorOptions();
    }

    public SshNetExecutor(SshExecutorOptions? options = null)
        : this(new SshNetConnectionFactory(options), options)
    {
    }

    public async Task<int> ExecuteAsync(
        SshTarget target,
        RemoteCommand command,
        ILogSink output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);

        var timeout = command.Timeout ?? TimeSpan.FromMinutes(30);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        ISshConnection connection;
        try
        {
            connection = await _connectionFactory.ConnectAsync(target, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await output.WriteAsync("timed out", LogStream.System, CancellationToken.None).ConfigureAwait(false);
            return 124;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"SSH connection failed: {Sanitize(ex.Message, target)}", ex);
        }

        await using (connection.ConfigureAwait(false))
        {
            return await ExecuteOnConnectionAsync(connection, command, output, timeoutCts, cancellationToken, linkedCts.Token)
                .ConfigureAwait(false);
        }
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(
        SshTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        ISshConnection connection;
        try
        {
            connection = await _connectionFactory.ConnectAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"SSH connection failed: {Sanitize(ex.Message, target)}");
        }

        await using (connection.ConfigureAwait(false))
        {
            // 1. Run echo shipit-ok
            var echoSink = new MemoryLogSink();
            var echoTimeout = TimeSpan.FromSeconds(15);
            var echoCmd = new RemoteCommand("echo shipit-ok", Timeout: echoTimeout);
            using var echoTimeoutCts = new CancellationTokenSource(echoTimeout);
            using var echoLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, echoTimeoutCts.Token);

            int echoCode;
            try
            {
                echoCode = await ExecuteOnConnectionAsync(connection, echoCmd, echoSink, echoTimeoutCts, cancellationToken, echoLinkedCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, $"SSH test command failed: {Sanitize(ex.Message, target)}");
            }

            if (echoCode != 0 || !echoSink.Lines.Any(l => l.Line.Trim() == "shipit-ok"))
            {
                var err = string.Join("; ", echoSink.Lines.Select(l => l.Line));
                return (false, $"SSH test command failed: {Sanitize(err, target)}");
            }

            // 2. Run docker version --format '{{.Server.Version}}'
            var dockerSink = new MemoryLogSink();
            var dockerTimeout = TimeSpan.FromSeconds(15);
            var dockerCmd = new RemoteCommand("docker version --format '{{.Server.Version}}'", Timeout: dockerTimeout);
            using var dockerTimeoutCts = new CancellationTokenSource(dockerTimeout);
            using var dockerLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, dockerTimeoutCts.Token);

            int dockerCode;
            try
            {
                dockerCode = await ExecuteOnConnectionAsync(connection, dockerCmd, dockerSink, dockerTimeoutCts, cancellationToken, dockerLinkedCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, $"Docker check failed: {Sanitize(ex.Message, target)}");
            }

            if (dockerCode != 0)
            {
                var err = string.Join("; ", dockerSink.Lines.Select(l => l.Line));
                return (false, $"Docker check failed: {Sanitize(err, target)}");
            }

            var version = dockerSink.Lines.FirstOrDefault(l => l.Stream == LogStream.Stdout).Line?.Trim();
            if (string.IsNullOrEmpty(version))
            {
                version = "unknown";
            }
            return (true, $"OK (Docker {version})");
        }
    }

    private static async Task<int> ExecuteOnConnectionAsync(
        ISshConnection connection,
        RemoteCommand command,
        ILogSink output,
        CancellationTokenSource timeoutCts,
        CancellationToken callerToken,
        CancellationToken executionToken)
    {
        using var runner = connection.CreateCommand(command.CommandText);
        runner.CommandTimeout = command.Timeout ?? TimeSpan.FromMinutes(30);

        Task? stdinTask = null;
        if (command.StdinSecret != null)
        {
            var inStream = runner.CreateInputStream();
            if (inStream != null)
            {
                stdinTask = Task.Run(async () =>
                {
                    try
                    {
                        await using (inStream.ConfigureAwait(false))
                        {
                            var bytes = Encoding.UTF8.GetBytes(command.StdinSecret + "\n");
                            await inStream.WriteAsync(bytes, executionToken).ConfigureAwait(false);
                            await inStream.FlushAsync(executionToken).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        // Channel closed or caller cancelled
                    }
                }, executionToken);
            }
        }

        var stdoutTask = StreamLinesAsync(runner.OutputStream, LogStream.Stdout, output, executionToken);
        var stderrTask = StreamLinesAsync(runner.ExtendedOutputStream, LogStream.Stderr, output, executionToken);

        int exitCode;
        try
        {
            var execTask = runner.ExecuteAsync(executionToken);
            exitCode = await execTask.ConfigureAwait(false);
            if (stdinTask != null)
            {
                await stdinTask.ConfigureAwait(false);
            }
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            runner.Cancel();
            await output.WriteAsync("timed out", LogStream.System, CancellationToken.None).ConfigureAwait(false);
            return 124;
        }
        catch (OperationCanceledException)
        {
            runner.Cancel();
            throw;
        }

        return exitCode;
    }

    private static async Task StreamLinesAsync(
        Stream stream,
        LogStream logStream,
        ILogSink sink,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }

            if (line == null)
            {
                break;
            }

            await sink.WriteAsync(line, logStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Sanitize(string message, SshTarget? target)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unknown error";
        }

        if (target != null)
        {
            if (!string.IsNullOrEmpty(target.PrivateKeyPem))
            {
                message = message.Replace(target.PrivateKeyPem, "[REDACTED_KEY]");
            }
            if (!string.IsNullOrEmpty(target.Passphrase))
            {
                message = message.Replace(target.Passphrase, "[REDACTED_PASSPHRASE]");
            }
        }

        return message;
    }

    private sealed class MemoryLogSink : ILogSink
    {
        private readonly List<(string Line, LogStream Stream)> _lines = [];
        private readonly object _lock = new();

        public IReadOnlyList<(string Line, LogStream Stream)> Lines
        {
            get
            {
                lock (_lock)
                {
                    return _lines.ToList();
                }
            }
        }

        public ValueTask WriteAsync(string line, LogStream stream, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _lines.Add((line, stream));
            }
            return ValueTask.CompletedTask;
        }
    }
}

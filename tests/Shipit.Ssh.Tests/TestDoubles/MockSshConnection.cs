using System.Text;
using Shipit.Core;
using Shipit.Ssh.Internal;

namespace Shipit.Ssh.Tests.TestDoubles;

public sealed class MockSshConnectionFactory : ISshConnectionFactory
{
    public Func<SshTarget, CancellationToken, Task<ISshConnection>>? ConnectHandler { get; set; }
    public int ConnectCallCount { get; private set; }

    public Task<ISshConnection> ConnectAsync(SshTarget target, CancellationToken cancellationToken)
    {
        ConnectCallCount++;
        if (ConnectHandler != null)
        {
            return ConnectHandler(target, cancellationToken);
        }
        return Task.FromResult<ISshConnection>(new MockSshConnection());
    }
}

public sealed class MockSshConnection : ISshConnection
{
    public bool IsDisposed { get; private set; }
    public Func<string, ISshCommandRunner>? CommandHandler { get; set; }
    public List<MockSshCommandRunner> CreatedCommands { get; } = [];

    public ISshCommandRunner CreateCommand(string commandText)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (CommandHandler != null)
        {
            var cmd = CommandHandler(commandText);
            if (cmd is MockSshCommandRunner mock)
            {
                CreatedCommands.Add(mock);
            }
            return cmd;
        }

        var defaultCmd = new MockSshCommandRunner(commandText);
        CreatedCommands.Add(defaultCmd);
        return defaultCmd;
    }

    public void Dispose()
    {
        IsDisposed = true;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

public sealed class MockSshCommandRunner : ISshCommandRunner
{
    private readonly MemoryStream _outputStream;
    private readonly MemoryStream _extendedOutputStream;
    private readonly MemoryStream _inputStream = new();

    public string CommandText { get; }
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public int ExitCode { get; set; } = 0;
    public TimeSpan? ExecutionDelay { get; set; }
    public bool Cancelled { get; private set; }
    public bool Disposed { get; private set; }
    public Func<CancellationToken, Task<int>>? CustomExecute { get; set; }

    public MockSshCommandRunner(string commandText, string stdout = "", string stderr = "", int exitCode = 0)
    {
        CommandText = commandText;
        ExitCode = exitCode;
        _outputStream = new MemoryStream(Encoding.UTF8.GetBytes(stdout));
        _extendedOutputStream = new MemoryStream(Encoding.UTF8.GetBytes(stderr));
    }

    public Stream? CreateInputStream() => _inputStream;

    public Stream OutputStream => _outputStream;

    public Stream ExtendedOutputStream => _extendedOutputStream;

    public string GetReceivedStdin()
    {
        return Encoding.UTF8.GetString(_inputStream.ToArray());
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (CustomExecute != null)
        {
            return await CustomExecute(cancellationToken);
        }

        if (ExecutionDelay.HasValue && ExecutionDelay.Value > TimeSpan.Zero)
        {
            await Task.Delay(ExecutionDelay.Value, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ExitCode;
    }

    public void Cancel()
    {
        Cancelled = true;
    }

    public void Dispose()
    {
        Disposed = true;
        _outputStream.Dispose();
        _extendedOutputStream.Dispose();
        _inputStream.Dispose();
    }
}

public sealed class TestLogSink : ILogSink
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

    public IReadOnlyList<string> StdoutLines
    {
        get
        {
            lock (_lock)
            {
                return _lines.Where(l => l.Stream == LogStream.Stdout).Select(l => l.Line).ToList();
            }
        }
    }

    public IReadOnlyList<string> StderrLines
    {
        get
        {
            lock (_lock)
            {
                return _lines.Where(l => l.Stream == LogStream.Stderr).Select(l => l.Line).ToList();
            }
        }
    }

    public IReadOnlyList<string> SystemLines
    {
        get
        {
            lock (_lock)
            {
                return _lines.Where(l => l.Stream == LogStream.System).Select(l => l.Line).ToList();
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

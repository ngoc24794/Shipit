using Shipit.Core;

namespace Shipit.K8s.Tests;

public sealed class TestLogSink : ILogSink
{
    private readonly List<(string Line, LogStream Stream)> _messages = new();

    public IReadOnlyList<(string Line, LogStream Stream)> Messages => _messages;

    public ValueTask WriteAsync(string line, LogStream stream, CancellationToken cancellationToken = default)
    {
        _messages.Add((line, stream));
        return ValueTask.CompletedTask;
    }
}

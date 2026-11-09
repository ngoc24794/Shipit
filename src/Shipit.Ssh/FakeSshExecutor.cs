using Shipit.Core;

namespace Shipit.Ssh;

/// <summary>
/// Information about a remote command executed by <see cref="FakeSshExecutor"/>.
/// The stdin value is never stored, only whether it had stdin and its length.
/// </summary>
public sealed record ExecutedCommand(string CommandText, bool HasStdin, int StdinLength);

/// <summary>
/// A rule for the fake SSH executor to match incoming commands and produce simulated outputs.
/// </summary>
public sealed class FakeSshRule
{
    public Func<RemoteCommand, bool> Predicate { get; init; } = _ => true;
    public int ExitCode { get; init; }
    public IReadOnlyList<(string Line, LogStream Stream)> OutputLines { get; init; } = [];
    public TimeSpan? Delay { get; init; }
    public Func<RemoteCommand, CancellationToken, Task>? OnExecuteAsync { get; init; }
}

/// <summary>
/// Fake SSH executor for testing pipelines and running in <c>Shipit:UseFakes</c> mode without an SSH server.
/// </summary>
public class FakeSshExecutor : ISshExecutor
{
    private readonly List<FakeSshRule> _rules = [];
    private readonly List<ExecutedCommand> _executedCommands = [];
    private readonly object _lock = new();

    /// <summary>
    /// When true, throws <see cref="InvalidOperationException"/> if a command does not match any rule.
    /// When false, unmatched commands return exit code 0 with no output.
    /// </summary>
    public bool StrictMode { get; set; }

    /// <summary>
    /// Custom handler for <see cref="TestConnectionAsync"/>. Defaults to returning Ok=true.
    /// </summary>
    public Func<SshTarget, CancellationToken, Task<(bool Ok, string Message)>>? TestConnectionHandler { get; set; }

    /// <summary>
    /// Optional event raised when a command is about to be executed.
    /// </summary>
    public event Action<RemoteCommand>? CommandExecuting;

    /// <summary>
    /// Commands executed by this executor in chronological order.
    /// </summary>
    public IReadOnlyList<ExecutedCommand> ExecutedCommands
    {
        get
        {
            lock (_lock)
            {
                return _executedCommands.ToList();
            }
        }
    }

    public void AddRule(FakeSshRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_lock)
        {
            _rules.Add(rule);
        }
    }

    public void AddRule(
        Func<RemoteCommand, bool> predicate,
        int exitCode = 0,
        IEnumerable<(string Line, LogStream Stream)>? outputLines = null,
        TimeSpan? delay = null)
    {
        AddRule(new FakeSshRule
        {
            Predicate = predicate,
            ExitCode = exitCode,
            OutputLines = outputLines?.ToList() ?? [],
            Delay = delay,
        });
    }

    public void AddRule(
        string commandSubstring,
        int exitCode = 0,
        IEnumerable<string>? stdoutLines = null,
        IEnumerable<string>? stderrLines = null,
        TimeSpan? delay = null)
    {
        var lines = new List<(string Line, LogStream Stream)>();
        if (stdoutLines != null)
        {
            lines.AddRange(stdoutLines.Select(l => (l, LogStream.Stdout)));
        }
        if (stderrLines != null)
        {
            lines.AddRange(stderrLines.Select(l => (l, LogStream.Stderr)));
        }

        AddRule(
            cmd => cmd.CommandText.Contains(commandSubstring, StringComparison.Ordinal),
            exitCode,
            lines,
            delay);
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

        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _executedCommands.Add(new ExecutedCommand(
                command.CommandText,
                command.StdinSecret != null,
                command.StdinSecret?.Length ?? 0));
        }

        CommandExecuting?.Invoke(command);

        FakeSshRule? matchedRule;
        lock (_lock)
        {
            matchedRule = _rules.FirstOrDefault(r => r.Predicate(command));
        }

        if (matchedRule == null)
        {
            if (StrictMode)
            {
                throw new InvalidOperationException($"No fake rule matched command: {command.CommandText}");
            }
            return 0;
        }

        if (matchedRule.Delay.HasValue && matchedRule.Delay.Value > TimeSpan.Zero)
        {
            await Task.Delay(matchedRule.Delay.Value, cancellationToken).ConfigureAwait(false);
        }

        if (matchedRule.OnExecuteAsync != null)
        {
            await matchedRule.OnExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (line, stream) in matchedRule.OutputLines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteAsync(line, stream, cancellationToken).ConfigureAwait(false);
        }

        return matchedRule.ExitCode;
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(
        SshTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (TestConnectionHandler != null)
        {
            return await TestConnectionHandler(target, cancellationToken).ConfigureAwait(false);
        }

        return (true, "OK");
    }

    /// <summary>
    /// Creates a fake SSH executor pre-configured for a successful build and push pipeline flow.
    /// </summary>
    public static FakeSshExecutor HappyPath()
    {
        var fake = new FakeSshExecutor();

        // 1. git clone
        fake.AddRule(
            cmd => cmd.CommandText.Contains("git clone", StringComparison.Ordinal),
            exitCode: 0,
            outputLines: [("Cloning into repository...", LogStream.Stdout)]);

        // 2. git rev-parse (ShortSha)
        fake.AddRule(
            cmd => cmd.CommandText.Contains("rev-parse --short=7 HEAD", StringComparison.Ordinal),
            exitCode: 0,
            outputLines: [("abc1234", LogStream.Stdout)]);

        // 3. docker login
        fake.AddRule(
            cmd => cmd.CommandText.Contains("docker login", StringComparison.Ordinal),
            exitCode: 0,
            outputLines: [("Login Succeeded", LogStream.Stdout)]);

        // 4. docker build
        fake.AddRule(
            cmd => cmd.CommandText.Contains("docker build", StringComparison.Ordinal),
            exitCode: 0,
            outputLines:
            [
                ("Step 1/3 : FROM mcr.microsoft.com/dotnet/aspnet:10.0", LogStream.Stdout),
                ("Step 2/3 : WORKDIR /app", LogStream.Stdout),
                ("Successfully built abc1234", LogStream.Stdout)
            ]);

        // 5. docker push
        fake.AddRule(
            cmd => cmd.CommandText.Contains("docker push", StringComparison.Ordinal),
            exitCode: 0,
            outputLines:
            [
                ("The push refers to repository", LogStream.Stdout),
                ("abc1234: digest: sha256:1234567890abcdef size: 528", LogStream.Stdout)
            ]);

        // 6. docker logout
        fake.AddRule(
            cmd => cmd.CommandText.Contains("docker logout", StringComparison.Ordinal),
            exitCode: 0,
            outputLines: [("Removing login credentials", LogStream.Stdout)]);

        // 7. cleanup
        fake.AddRule(
            cmd => cmd.CommandText.Contains("rm -rf", StringComparison.Ordinal),
            exitCode: 0);

        // 8. test connection echo shipit-ok
        fake.AddRule(
            cmd => cmd.CommandText.Contains("echo shipit-ok", StringComparison.Ordinal),
            exitCode: 0,
            outputLines: [("shipit-ok", LogStream.Stdout)]);

        // 9. docker version
        fake.AddRule(
            cmd => cmd.CommandText.Contains("docker version", StringComparison.Ordinal),
            exitCode: 0,
            outputLines: [("24.0.7", LogStream.Stdout)]);

        return fake;
    }
}

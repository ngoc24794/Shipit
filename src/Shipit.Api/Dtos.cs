using Shipit.Core;
using Shipit.Data.Entities;

namespace Shipit.Api;

// Wire types, shaped exactly like docs/api-contract.yaml (System.Text.Json web defaults give camelCase).

public sealed record LoginRequest(string? Username, string? Password);

public sealed record MeDto(string Username);

public sealed record SlaveDto(long Id, string Name, string Host, int Port, string User, long SshCredentialId);

public sealed record SlaveInput(string? Name, string? Host, int? Port, string? User, long? SshCredentialId);

public sealed record ConnectionTestResultDto(bool Ok, string Message);

/// <summary>Credential metadata. There is deliberately no field that could carry the secret value.</summary>
public sealed record CredentialDto(long Id, string Name, string Kind, string? Username, bool HasValue, DateTime UpdatedAt);

public sealed record CredentialInput(string? Name, string? Kind, string? Username, string? Value);

public sealed record CredentialUpdate(string? Name, string? Username, string? Value);

public sealed record DeployTargetDto(long Id, string Namespace, string Deployment, DateTime CreatedAt);

public sealed record DeployTargetInput(string? Namespace, string? Deployment);

public sealed record PipelineDto(
    long Id,
    string Name,
    string GitUrl,
    string GitBranch,
    long GitCredentialId,
    long SlaveId,
    string DockerfilePath,
    string? BuildContext,
    string ImageName,
    string RegistryHost,
    string? DeployRegistryHost,
    long? RegistryCredentialId,
    string DeployNamespace,
    string DeployDeployment,
    string DeployContainer,
    RunDto? LastRun);

public sealed record PipelineInput(
    string? Name,
    string? GitUrl,
    string? GitBranch,
    long? GitCredentialId,
    long? SlaveId,
    string? DockerfilePath,
    string? BuildContext,
    string? ImageName,
    string? RegistryHost,
    string? DeployRegistryHost,
    long? RegistryCredentialId,
    string? DeployNamespace,
    string? DeployDeployment,
    string? DeployContainer);

public sealed record RunDto(
    long Id,
    long PipelineId,
    string Status,
    string? Tag,
    string? Image,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt);

public sealed record RunStepDto(string Name, string Status, DateTime? StartedAt, DateTime? FinishedAt);

public sealed record RunDetailDto(
    long Id,
    long PipelineId,
    string Status,
    string? Tag,
    string? Image,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    IReadOnlyList<RunStepDto> Steps);

public sealed record LogLineDto(long Seq, string Step, string Stream, string Text, DateTime At);

public sealed record ProblemDto(string Title, string? Detail = null, IReadOnlyDictionary<string, string[]>? Errors = null);

public sealed record ReadinessDto(bool Ready, string Database, int PendingMigrations);

/// <summary>Converts between domain enums and the strings used in the contract.</summary>
internal static class Wire
{
    public static string Of(CredentialKind kind) => kind switch
    {
        CredentialKind.SshKey => "ssh_key",
        CredentialKind.GitToken => "git_token",
        CredentialKind.Registry => "registry",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseKind(string? value, out CredentialKind kind)
    {
        switch (value)
        {
            case "ssh_key":
                kind = CredentialKind.SshKey;
                return true;
            case "git_token":
                kind = CredentialKind.GitToken;
                return true;
            case "registry":
                kind = CredentialKind.Registry;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string Of(RunStatus status) => status.ToString().ToLowerInvariant();

    public static string Of(StepName step) => step.ToString().ToLowerInvariant();

    public static string Of(StepStatus status) => status.ToString().ToLowerInvariant();

    public static string Of(LogStream stream) => stream.ToString().ToLowerInvariant();

    /// <summary>The database may hand back DateTimes with an unspecified kind; everything stored is UTC.</summary>
    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? Utc(DateTime? value) => value is null ? null : Utc(value.Value);
}

internal static class Mapping
{
    public static SlaveDto ToDto(this Slave s) => new(s.Id, s.Name, s.Host, s.Port, s.User, s.SshCredentialId);

    public static CredentialDto ToDto(this Credential c) =>
        new(c.Id, c.Name, Wire.Of(c.Kind), c.Username, !string.IsNullOrEmpty(c.ProtectedValue), Wire.Utc(c.UpdatedAt));

    public static RunDto ToDto(this Run r) => new(
        r.Id, r.PipelineId, Wire.Of(r.Status), r.Tag, r.Image, r.FailureReason,
        Wire.Utc(r.CreatedAt), Wire.Utc(r.StartedAt), Wire.Utc(r.FinishedAt));

    public static RunDetailDto ToDetailDto(this Run r) => new(
        r.Id, r.PipelineId, Wire.Of(r.Status), r.Tag, r.Image, r.FailureReason,
        Wire.Utc(r.CreatedAt), Wire.Utc(r.StartedAt), Wire.Utc(r.FinishedAt),
        r.Steps.OrderBy(s => s.Name)
            .Select(s => new RunStepDto(Wire.Of(s.Name), Wire.Of(s.Status), Wire.Utc(s.StartedAt), Wire.Utc(s.FinishedAt)))
            .ToList());

    public static PipelineDto ToDto(this Pipeline p, Run? lastRun) => new(
        p.Id, p.Name, p.GitUrl, p.GitBranch, p.GitCredentialId, p.SlaveId, p.DockerfilePath, p.BuildContext, p.ImageName,
        p.RegistryHost, p.DeployRegistryHost, p.RegistryCredentialId, p.DeployNamespace, p.DeployDeployment, p.DeployContainer,
        lastRun?.ToDto());

    public static LogLineDto ToDto(this RunLog l) =>
        new(l.Id, Wire.Of(l.Step), Wire.Of(l.Stream), l.Text, Wire.Utc(l.At));

    public static DeployTargetDto ToDto(this DeployTarget t) =>
        new(t.Id, t.Namespace, t.Deployment, Wire.Utc(t.CreatedAt));
}

using System.Text.Json;
using Shipit.Core;
using Shipit.Data.Entities;

namespace Shipit.Api;

// Wire types, shaped exactly like docs/api-contract.yaml (System.Text.Json web defaults give camelCase).

public sealed record LoginRequest(string? Username, string? Password, bool RememberMe = false);

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
    long? PipelineId,
    long? JobId,
    string Kind,
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
    long? PipelineId,
    long? JobId,
    string Kind,
    string Status,
    string? Tag,
    string? Image,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    IReadOnlyList<RunStepDto> Steps,
    IReadOnlyList<RunTaskDto> Tasks);

public sealed record RunTaskDto(int Position, string Label, string Status, int? ExitCode, DateTime? StartedAt, DateTime? FinishedAt);

public sealed record LogLineDto(long Seq, string Step, string Stream, string Text, DateTime At, int? TaskPosition = null);

// ---- Jobs (docs/api-contract.yaml, tag "jobs"). Names only: no field here can carry a secret value.

/// <summary>An env var for a task: a plain Value, or a reference (SecretName + SecretKey) resolved by Kubernetes.</summary>
public sealed record JobEnvVarDto(string? Name, string? Value, string? SecretName, string? SecretKey);

public sealed record JobTaskDto(
    int Position,
    string Label,
    IReadOnlyList<string> Args,
    IReadOnlyList<string> EnvFromConfigMaps,
    IReadOnlyList<string> EnvFromSecrets,
    IReadOnlyList<JobEnvVarDto> Env,
    int TimeoutSeconds,
    int BackoffLimit,
    int TtlSeconds);

/// <summary>Task position is the index in the submitted array.</summary>
public sealed record JobTaskInput(
    string? Label,
    string[]? Args,
    string[]? EnvFromConfigMaps,
    string[]? EnvFromSecrets,
    JobEnvVarDto[]? Env,
    int? TimeoutSeconds,
    int? BackoffLimit,
    int? TtlSeconds);

public sealed record JobDto(
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
    string Namespace,
    string ContainerName,
    string? ServiceAccount,
    IReadOnlyList<string> ImagePullSecrets,
    IReadOnlyList<JobTaskDto> Tasks,
    RunDto? LastRun);

public sealed record JobInput(
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
    string? Namespace,
    string? ContainerName,
    string? ServiceAccount,
    string[]? ImagePullSecrets,
    JobTaskInput[]? Tasks);

public sealed record JobValidationDto(bool Valid, IReadOnlyDictionary<string, string[]> Errors);

public sealed record JobPreviewTaskDto(int Position, string Label, string Yaml);

public sealed record JobPreviewDto(IReadOnlyDictionary<string, string[]> Errors, IReadOnlyList<JobPreviewTaskDto> Tasks);

public sealed record JobAllowEntryDto(long Id, string Namespace, string Kind, string Name, DateTime CreatedAt);

public sealed record JobAllowEntryInput(string? Namespace, string? Kind, string? Name);

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

    public static string KindOf(Run run) => run.JobId is not null ? "job" : "pipeline";

    public static string Of(JobAllowKind kind) => kind switch
    {
        JobAllowKind.Namespace => "namespace",
        JobAllowKind.ConfigMap => "configmap",
        JobAllowKind.Secret => "secret",
        JobAllowKind.PullSecret => "pullsecret",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseAllowKind(string? value, out JobAllowKind kind)
    {
        switch (value)
        {
            case "namespace":
                kind = JobAllowKind.Namespace;
                return true;
            case "configmap":
                kind = JobAllowKind.ConfigMap;
                return true;
            case "secret":
                kind = JobAllowKind.Secret;
                return true;
            case "pullsecret":
                kind = JobAllowKind.PullSecret;
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
        r.Id, r.PipelineId, r.JobId, Wire.KindOf(r), Wire.Of(r.Status), r.Tag, r.Image, r.FailureReason,
        Wire.Utc(r.CreatedAt), Wire.Utc(r.StartedAt), Wire.Utc(r.FinishedAt));

    public static RunDetailDto ToDetailDto(this Run r) => new(
        r.Id, r.PipelineId, r.JobId, Wire.KindOf(r), Wire.Of(r.Status), r.Tag, r.Image, r.FailureReason,
        Wire.Utc(r.CreatedAt), Wire.Utc(r.StartedAt), Wire.Utc(r.FinishedAt),
        r.Steps.OrderBy(s => s.Name)
            .Select(s => new RunStepDto(Wire.Of(s.Name), Wire.Of(s.Status), Wire.Utc(s.StartedAt), Wire.Utc(s.FinishedAt)))
            .ToList(),
        r.Tasks.OrderBy(t => t.Position)
            .Select(t => new RunTaskDto(t.Position, t.Label, Wire.Of(t.Status), t.ExitCode, Wire.Utc(t.StartedAt), Wire.Utc(t.FinishedAt)))
            .ToList());

    public static PipelineDto ToDto(this Pipeline p, Run? lastRun) => new(
        p.Id, p.Name, p.GitUrl, p.GitBranch, p.GitCredentialId, p.SlaveId, p.DockerfilePath, p.BuildContext, p.ImageName,
        p.RegistryHost, p.DeployRegistryHost, p.RegistryCredentialId, p.DeployNamespace, p.DeployDeployment, p.DeployContainer,
        lastRun?.ToDto());

    public static LogLineDto ToDto(this RunLog l) =>
        new(l.Id, Wire.Of(l.Step), Wire.Of(l.Stream), l.Text, Wire.Utc(l.At), l.TaskPosition);

    public static DeployTargetDto ToDto(this DeployTarget t) =>
        new(t.Id, t.Namespace, t.Deployment, Wire.Utc(t.CreatedAt));

    public static JobAllowEntryDto ToDto(this JobAllowEntry e) =>
        new(e.Id, e.Namespace, Wire.Of(e.Kind), e.Name, Wire.Utc(e.CreatedAt));

    public static JobTaskDto ToDto(this JobTask t) =>
        new(
            t.Position,
            t.Label,
            DeserializeList(t.ArgsJson),
            DeserializeList(t.EnvFromConfigMapsJson),
            DeserializeList(t.EnvFromSecretsJson),
            DeserializeEnvList(t.EnvJson),
            t.TimeoutSeconds,
            t.BackoffLimit,
            t.TtlSeconds);

    public static JobDto ToDto(this Job j, Run? lastRun) =>
        new(
            j.Id,
            j.Name,
            j.GitUrl,
            j.GitBranch,
            j.GitCredentialId,
            j.SlaveId,
            j.DockerfilePath,
            j.BuildContext,
            j.ImageName,
            j.RegistryHost,
            j.DeployRegistryHost,
            j.RegistryCredentialId,
            j.Namespace,
            j.ContainerName,
            j.ServiceAccount,
            DeserializeList(j.ImagePullSecretsJson),
            (j.Tasks ?? []).OrderBy(t => t.Position).Select(t => t.ToDto()).ToList(),
            lastRun?.ToDto());

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<JobEnvVarDto> DeserializeEnvList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<JobEnvVarDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;
using Shipit.Engine;
using Shipit.K8s;
using Shipit.Ssh;

namespace Shipit.Api;

internal static partial class JobEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    private static partial Regex K8sLabel();

    [GeneratedRegex("^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$")]
    private static partial Regex K8sSubdomain();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvVarName();

    [GeneratedRegex("^[-._a-zA-Z0-9]+$")]
    private static partial Regex SecretKey();

    public static void MapJobs(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api/jobs");

        jobs.MapGet("", async (ShipitDbContext db, CancellationToken ct) =>
        {
            var list = await db.Jobs.AsNoTracking()
                .Include(j => j.Tasks)
                .OrderBy(j => j.Name)
                .ToListAsync(ct);
            var last = await LastRunsAsync(db, list.Select(j => j.Id).ToList(), ct);
            return Results.Ok(list.Select(j => j.ToDto(last.GetValueOrDefault(j.Id))));
        });

        jobs.MapGet("{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
        {
            var job = await db.Jobs.AsNoTracking()
                .Include(j => j.Tasks)
                .FirstOrDefaultAsync(j => j.Id == id, ct);
            if (job is null)
            {
                return Results.NotFound();
            }

            var last = await LastRunsAsync(db, [id], ct);
            return Results.Ok(job.ToDto(last.GetValueOrDefault(id)));
        });

        jobs.MapPost("", async (JobInput input, ShipitDbContext db, IJobAllowList allowList, CancellationToken ct) =>
        {
            var name = (input.Name ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(name) && await db.Jobs.AnyAsync(j => j.Name == name, ct))
            {
                return Problems.Conflict("Job name already exists", "A job with this name already exists.");
            }

            var errors = await ValidateAsync(input, db, allowList, existingId: null, isValidateOnly: false, ct);
            if (errors.Any)
            {
                return errors.ToResult();
            }

            var now = DateTime.UtcNow;
            var job = new Job { CreatedAt = now, UpdatedAt = now };
            Apply(job, input);
            db.Jobs.Add(job);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/jobs/{job.Id}", job.ToDto(lastRun: null));
        });

        jobs.MapPut("{id:long}", async (long id, JobInput input, ShipitDbContext db, IJobAllowList allowList, CancellationToken ct) =>
        {
            var job = await db.Jobs.Include(j => j.Tasks).FirstOrDefaultAsync(j => j.Id == id, ct);
            if (job is null)
            {
                return Results.NotFound();
            }

            var name = (input.Name ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(name) && await db.Jobs.AnyAsync(j => j.Name == name && j.Id != id, ct))
            {
                return Problems.Conflict("Job name already exists", "A job with this name already exists.");
            }

            var errors = await ValidateAsync(input, db, allowList, existingId: id, isValidateOnly: false, ct);
            if (errors.Any)
            {
                return errors.ToResult();
            }

            db.JobTasks.RemoveRange(job.Tasks);
            Apply(job, input);
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var last = await LastRunsAsync(db, [id], ct);
            return Results.Ok(job.ToDto(last.GetValueOrDefault(id)));
        });

        jobs.MapDelete("{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
        {
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);
            if (job is null)
            {
                return Results.NotFound();
            }

            if (await db.Runs.AnyAsync(r => r.JobId == id && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running), ct))
            {
                return Problems.Conflict("Job has an active run", "Wait for the run to finish before deleting the job.");
            }

            db.Jobs.Remove(job);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        jobs.MapPost("{id:long}/runs", async (long id, HttpRequest http, IRunCoordinator coordinator, CancellationToken ct) =>
        {
            var (ok, skipSteps, badRequest) = await RunEndpoints.TryParseTriggerSkipStepsAsync(http, isJob: true, ct);
            if (!ok)
            {
                return badRequest!;
            }

            try
            {
                var run = await coordinator.TriggerJobAsync(id, ct, skipSteps);
                return Results.Json(run.ToDto(), statusCode: StatusCodes.Status202Accepted);
            }
            catch (JobNotFoundException)
            {
                return Results.NotFound();
            }
            catch (JobRunConflictException)
            {
                return Problems.Conflict("Job already has an active run", "Wait for the current run to finish.");
            }
        });

        jobs.MapPost("validate", async (long? existingId, JobInput input, ShipitDbContext db, IJobAllowList allowList, CancellationToken ct) =>
        {
            var errors = await ValidateAsync(input, db, allowList, existingId: existingId, isValidateOnly: true, ct);
            return Results.Ok(new JobValidationDto(!errors.Any, errors.ToDictionary()));
        });

        jobs.MapPost("preview", async (long? existingId, JobInput input, ShipitDbContext db, IJobAllowList allowList, CancellationToken ct) =>
        {
            var errors = await ValidateAsync(input, db, allowList, existingId: existingId, isValidateOnly: true, ct);
            if (errors.Any)
            {
                return Results.Ok(new JobPreviewDto(errors.ToDictionary(), []));
            }

            var tasks = BuildPreviewTasks(input);
            return Results.Ok(new JobPreviewDto(new Dictionary<string, string[]>(), tasks));
        });

        var allow = app.MapGroup("/api/job-allow-entries");

        allow.MapGet("", async (ShipitDbContext db, CancellationToken ct) =>
        {
            var entries = await db.JobAllowEntries.AsNoTracking()
                .OrderBy(e => e.Namespace)
                .ThenBy(e => e.Kind)
                .ThenBy(e => e.Name)
                .ToListAsync(ct);
            return Results.Ok(entries.Select(e => e.ToDto()));
        });

        allow.MapPost("", async (JobAllowEntryInput input, ShipitDbContext db, CancellationToken ct) =>
        {
            var errors = new FieldErrors();
            var ns = (input.Namespace ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(ns))
            {
                errors.Add("namespace", "Namespace is required.");
            }
            else if (!K8sLabel().IsMatch(ns) || ns.Length > 63)
            {
                errors.Add("namespace", "Namespace must be a valid Kubernetes name.");
            }

            if (!Wire.TryParseAllowKind(input.Kind, out var kind))
            {
                errors.Add("kind", "Kind must be one of: namespace, configmap, secret, pullsecret.");
            }

            var rawName = (input.Name ?? string.Empty).Trim();
            string name;
            if (kind == JobAllowKind.Namespace)
            {
                name = ns;
            }
            else
            {
                name = rawName;
                if (string.IsNullOrEmpty(name))
                {
                    errors.Add("name", "Name is required.");
                }
                else if (!K8sSubdomain().IsMatch(name) || name.Length > 253)
                {
                    errors.Add("name", "Name must be a valid Kubernetes resource name.");
                }
            }

            if (errors.Any)
            {
                return errors.ToResult();
            }

            if (await db.JobAllowEntries.AnyAsync(e => e.Namespace == ns && e.Kind == kind && e.Name == name, ct))
            {
                return Problems.Conflict("Job allow entry already exists", "An entry with this namespace, kind, and name already exists.");
            }

            var entry = new JobAllowEntry
            {
                Namespace = ns,
                Kind = kind,
                Name = name,
                CreatedAt = DateTime.UtcNow
            };
            db.JobAllowEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/job-allow-entries/{entry.Id}", entry.ToDto());
        });

        allow.MapDelete("{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
        {
            var entry = await db.JobAllowEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (entry is null)
            {
                return Results.NotFound();
            }

            if (entry.Kind == JobAllowKind.Namespace)
            {
                if (await db.Jobs.AnyAsync(j => j.Namespace == entry.Namespace, ct))
                {
                    return Problems.Conflict("Job allow entry is in use", $"Namespace '{entry.Namespace}' is in use by one or more jobs.");
                }
            }
            else if (entry.Kind == JobAllowKind.PullSecret)
            {
                var secretsJson = await db.Jobs.Where(j => j.Namespace == entry.Namespace)
                    .Select(j => j.ImagePullSecretsJson)
                    .ToListAsync(ct);
                if (secretsJson.Any(json => Mapping.DeserializeList(json).Contains(entry.Name, StringComparer.Ordinal)))
                {
                    return Problems.Conflict("Job allow entry is in use", $"Pull secret '{entry.Name}' is in use by one or more jobs.");
                }
            }
            else if (entry.Kind == JobAllowKind.ConfigMap)
            {
                var cmsJson = await db.JobTasks.Where(t => t.Job!.Namespace == entry.Namespace)
                    .Select(t => t.EnvFromConfigMapsJson)
                    .ToListAsync(ct);
                if (cmsJson.Any(json => Mapping.DeserializeList(json).Contains(entry.Name, StringComparer.Ordinal)))
                {
                    return Problems.Conflict("Job allow entry is in use", $"ConfigMap '{entry.Name}' is in use by one or more job tasks.");
                }
            }
            else if (entry.Kind == JobAllowKind.Secret)
            {
                var tasks = await db.JobTasks.Where(t => t.Job!.Namespace == entry.Namespace)
                    .Select(t => new { t.EnvFromSecretsJson, t.EnvJson })
                    .ToListAsync(ct);
                var inUse = tasks.Any(t =>
                    Mapping.DeserializeList(t.EnvFromSecretsJson).Contains(entry.Name, StringComparer.Ordinal) ||
                    Mapping.DeserializeEnvList(t.EnvJson).Any(e => string.Equals(e.SecretName, entry.Name, StringComparison.Ordinal)));
                if (inUse)
                {
                    return Problems.Conflict("Job allow entry is in use", $"Secret '{entry.Name}' is in use by one or more job tasks.");
                }
            }

            db.JobAllowEntries.Remove(entry);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<Dictionary<long, Run>> LastRunsAsync(ShipitDbContext db, List<long> jobIds, CancellationToken ct)
    {
        if (jobIds.Count == 0)
        {
            return [];
        }

        var latestIds = await db.Runs.AsNoTracking()
            .Where(r => r.JobId != null && jobIds.Contains(r.JobId.Value))
            .GroupBy(r => r.JobId)
            .Select(g => g.Max(r => r.Id))
            .ToListAsync(ct);
        var runs = await db.Runs.AsNoTracking().Where(r => latestIds.Contains(r.Id)).ToListAsync(ct);
        return runs.ToDictionary(r => r.JobId!.Value);
    }

    private static void Apply(Job j, JobInput i)
    {
        j.Name = i.Name!.Trim();
        j.GitUrl = i.GitUrl!.Trim();
        j.GitBranch = string.IsNullOrWhiteSpace(i.GitBranch) ? "main" : i.GitBranch.Trim();
        j.GitCredentialId = i.GitCredentialId!.Value;
        j.SlaveId = i.SlaveId!.Value;
        j.DockerfilePath = string.IsNullOrWhiteSpace(i.DockerfilePath) ? "Dockerfile" : i.DockerfilePath.Trim();
        j.BuildContext = string.IsNullOrWhiteSpace(i.BuildContext) ? null : i.BuildContext.Trim();
        j.ImageName = i.ImageName!.Trim();
        j.RegistryHost = i.RegistryHost!.Trim();
        j.DeployRegistryHost = string.IsNullOrWhiteSpace(i.DeployRegistryHost) ? null : i.DeployRegistryHost.Trim();
        j.RegistryCredentialId = i.RegistryCredentialId;
        j.Namespace = i.Namespace!.Trim();
        j.ContainerName = string.IsNullOrWhiteSpace(i.ContainerName) ? "app" : i.ContainerName.Trim();
        j.ServiceAccount = string.IsNullOrWhiteSpace(i.ServiceAccount) ? null : i.ServiceAccount.Trim();

        var pullSecrets = (i.ImagePullSecrets ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();
        j.ImagePullSecretsJson = JsonSerializer.Serialize(pullSecrets, Json);

        j.Tasks.Clear();
        if (i.Tasks != null)
        {
            for (var idx = 0; idx < i.Tasks.Length; idx++)
            {
                var t = i.Tasks[idx];
                var task = new JobTask
                {
                    Position = idx,
                    Label = t.Label!.Trim(),
                    TimeoutSeconds = t.TimeoutSeconds ?? 1800,
                    BackoffLimit = t.BackoffLimit ?? 0,
                    TtlSeconds = t.TtlSeconds ?? 3600,
                    ArgsJson = JsonSerializer.Serialize((t.Args ?? []).Select(a => a.Trim()).ToList(), Json),
                    EnvFromConfigMapsJson = JsonSerializer.Serialize((t.EnvFromConfigMaps ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList(), Json),
                    EnvFromSecretsJson = JsonSerializer.Serialize((t.EnvFromSecrets ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList(), Json),
                    EnvJson = JsonSerializer.Serialize((t.Env ?? []).Select(e => new JobEnvVarDto(
                        e.Name?.Trim(),
                        e.Value,
                        string.IsNullOrWhiteSpace(e.SecretName) ? null : e.SecretName.Trim(),
                        string.IsNullOrWhiteSpace(e.SecretKey) ? null : e.SecretKey.Trim())).ToList(), Json)
                };
                j.Tasks.Add(task);
            }
        }
    }

    private static async Task<FieldErrors> ValidateAsync(
        JobInput i, ShipitDbContext db, IJobAllowList allowList, long? existingId, bool isValidateOnly, CancellationToken ct)
    {
        var errors = new FieldErrors();

        var name = (i.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            errors.Add("name", "Name is required.");
        }
        else if (name.Length > 40)
        {
            errors.Add("name", "Name must not exceed 40 characters.");
        }
        else if (!K8sLabel().IsMatch(name))
        {
            errors.Add("name", "Name must be a valid DNS-1123 label (lowercase alphanumeric and hyphens, starting and ending with alphanumeric).");
        }
        else if (isValidateOnly && await db.Jobs.AnyAsync(j => j.Name == name && j.Id != existingId, ct))
        {
            errors.Add("name", "A job with this name already exists.");
        }

        var branch = string.IsNullOrWhiteSpace(i.GitBranch) ? "main" : i.GitBranch.Trim();
        Check(errors, "gitUrl", i.GitUrl, v => RemoteCommandBuilder.Clone(v, branch, "/tmp/shipit/run-1", "x"), required: true);
        Check(errors, "gitBranch", branch, v => RemoteCommandBuilder.Clone("https://example.com/r.git", v, "/tmp/shipit/run-1", "x"));
        var dockerfile = string.IsNullOrWhiteSpace(i.DockerfilePath) ? "Dockerfile" : i.DockerfilePath.Trim();
        Check(errors, "dockerfilePath", dockerfile, v => RemoteCommandBuilder.DockerBuild("/tmp/shipit/run-1", v, "example.com/x:t"));
        Check(errors, "buildContext", i.BuildContext, v => RemoteCommandBuilder.ValidateBuildContext(v));
        Check(errors, "registryHost", i.RegistryHost, v => RemoteCommandBuilder.ValidateRegistryHost(v), required: true);
        Check(errors, "deployRegistryHost", i.DeployRegistryHost, v => RemoteCommandBuilder.ValidateRegistryHost(v));
        Check(errors, "imageName", i.ImageName, v => RemoteCommandBuilder.BuildImageRef("example.com", v, "t"), required: true);

        await CheckCredentialAsync(errors, db, "gitCredentialId", i.GitCredentialId, CredentialKind.GitToken, "git_token", ct);
        if (i.RegistryCredentialId is not null)
        {
            await CheckCredentialAsync(errors, db, "registryCredentialId", i.RegistryCredentialId, CredentialKind.Registry, "registry", ct);
        }

        if (i.SlaveId is null)
        {
            errors.Add("slaveId", "A slave is required.");
        }
        else if (!await db.Slaves.AnyAsync(s => s.Id == i.SlaveId, ct))
        {
            errors.Add("slaveId", "Slave does not exist.");
        }

        var ns = (i.Namespace ?? string.Empty).Trim();
        var nsOk = true;
        if (string.IsNullOrEmpty(ns))
        {
            errors.Add("namespace", "Namespace is required.");
            nsOk = false;
        }
        else if (!K8sLabel().IsMatch(ns) || ns.Length > 63)
        {
            errors.Add("namespace", "Namespace must be a valid Kubernetes name.");
            nsOk = false;
        }
        else if (!await allowList.IsNamespaceAllowedAsync(ns, ct))
        {
            errors.Add("namespace", "Namespace is not in the allowed list.");
            nsOk = false;
        }

        var container = string.IsNullOrWhiteSpace(i.ContainerName) ? "app" : i.ContainerName.Trim();
        if (!K8sLabel().IsMatch(container) || container.Length > 63)
        {
            errors.Add("containerName", "Container name must be a valid Kubernetes name.");
        }

        if (!string.IsNullOrWhiteSpace(i.ServiceAccount))
        {
            var sa = i.ServiceAccount.Trim();
            if (!K8sSubdomain().IsMatch(sa) || sa.Length > 253)
            {
                errors.Add("serviceAccount", "Service account must be a valid Kubernetes name.");
            }
        }

        if (i.ImagePullSecrets != null)
        {
            for (var idx = 0; idx < i.ImagePullSecrets.Length; idx++)
            {
                var ps = (i.ImagePullSecrets[idx] ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(ps) || !K8sSubdomain().IsMatch(ps) || ps.Length > 253)
                {
                    errors.Add($"imagePullSecrets[{idx}]", "Pull secret name is invalid.");
                }
                else if (nsOk && !await allowList.IsPullSecretAllowedAsync(ns, ps, ct))
                {
                    errors.Add($"imagePullSecrets[{idx}]", $"Pull secret '{ps}' is not in the allowed list.");
                }
            }
        }

        if (i.Tasks == null || i.Tasks.Length < 1 || i.Tasks.Length > 20)
        {
            errors.Add("tasks", "A job must have between 1 and 20 tasks.");
        }
        else
        {
            for (var idx = 0; idx < i.Tasks.Length; idx++)
            {
                var t = i.Tasks[idx];
                if (string.IsNullOrWhiteSpace(t.Label))
                {
                    errors.Add($"tasks[{idx}].label", "Label is required.");
                }

                if (t.TimeoutSeconds is { } timeout && (timeout < 1 || timeout > 86400))
                {
                    errors.Add($"tasks[{idx}].timeoutSeconds", "Timeout must be between 1 and 86400 seconds.");
                }

                if (t.BackoffLimit is { } backoff && (backoff < 0 || backoff > 6))
                {
                    errors.Add($"tasks[{idx}].backoffLimit", "Backoff limit must be between 0 and 6.");
                }

                if (t.TtlSeconds is { } ttl && (ttl < 60 || ttl > 604800))
                {
                    errors.Add($"tasks[{idx}].ttlSeconds", "TTL must be between 60 and 604800 seconds.");
                }

                if (t.Args != null)
                {
                    if (t.Args.Length > 64)
                    {
                        errors.Add($"tasks[{idx}].args", "Task cannot have more than 64 arguments.");
                    }

                    foreach (var arg in t.Args)
                    {
                        if (arg == null)
                        {
                            errors.Add($"tasks[{idx}].args", "Argument cannot be null.");
                        }
                        else if (arg.Length > 1024)
                        {
                            errors.Add($"tasks[{idx}].args", "Argument exceeds maximum length of 1024 characters.");
                        }
                        else if (arg.Any(char.IsControl))
                        {
                            errors.Add($"tasks[{idx}].args", "Argument cannot contain control characters.");
                        }
                    }
                }

                if (t.EnvFromConfigMaps != null)
                {
                    for (var cmIdx = 0; cmIdx < t.EnvFromConfigMaps.Length; cmIdx++)
                    {
                        var cm = (t.EnvFromConfigMaps[cmIdx] ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(cm) || !K8sSubdomain().IsMatch(cm) || cm.Length > 253)
                        {
                            errors.Add($"tasks[{idx}].envFromConfigMaps[{cmIdx}]", "ConfigMap name is invalid.");
                        }
                        else if (nsOk && !await allowList.IsConfigMapAllowedAsync(ns, cm, ct))
                        {
                            errors.Add($"tasks[{idx}].envFromConfigMaps[{cmIdx}]", $"ConfigMap '{cm}' is not in the allowed list.");
                        }
                    }
                }

                if (t.EnvFromSecrets != null)
                {
                    for (var secIdx = 0; secIdx < t.EnvFromSecrets.Length; secIdx++)
                    {
                        var sec = (t.EnvFromSecrets[secIdx] ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(sec) || !K8sSubdomain().IsMatch(sec) || sec.Length > 253)
                        {
                            errors.Add($"tasks[{idx}].envFromSecrets[{secIdx}]", "Secret name is invalid.");
                        }
                        else if (nsOk && !await allowList.IsSecretAllowedAsync(ns, sec, ct))
                        {
                            errors.Add($"tasks[{idx}].envFromSecrets[{secIdx}]", $"Secret '{sec}' is not in the allowed list.");
                        }
                    }
                }

                if (t.Env != null)
                {
                    var seenEnvNames = new HashSet<string>(StringComparer.Ordinal);
                    for (var envIdx = 0; envIdx < t.Env.Length; envIdx++)
                    {
                        var ev = t.Env[envIdx];
                        var evName = (ev.Name ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(evName) || !EnvVarName().IsMatch(evName))
                        {
                            errors.Add($"tasks[{idx}].env[{envIdx}].name", "Environment variable name must match [A-Za-z_][A-Za-z0-9_]*.");
                        }
                        else if (!seenEnvNames.Add(evName))
                        {
                            errors.Add($"tasks[{idx}].env[{envIdx}].name", $"Duplicate environment variable name '{evName}'.");
                        }

                        var hasValue = ev.Value is not null;
                        var hasSecretName = !string.IsNullOrWhiteSpace(ev.SecretName);
                        var hasSecretKey = !string.IsNullOrWhiteSpace(ev.SecretKey);
                        var hasSecret = hasSecretName || hasSecretKey;

                        if ((hasValue && hasSecret) || (!hasValue && !hasSecret) || (hasSecret && (!hasSecretName || !hasSecretKey)))
                        {
                            errors.Add($"tasks[{idx}].env[{envIdx}]", "Environment variable must specify either a value or both secretName and secretKey.");
                        }
                        else if (hasSecret)
                        {
                            var secName = ev.SecretName!.Trim();
                            var secKey = ev.SecretKey!.Trim();

                            if (!K8sSubdomain().IsMatch(secName) || secName.Length > 253)
                            {
                                errors.Add($"tasks[{idx}].env[{envIdx}].secretName", "Secret name is invalid.");
                            }
                            else if (nsOk && !await allowList.IsSecretAllowedAsync(ns, secName, ct))
                            {
                                errors.Add($"tasks[{idx}].env[{envIdx}].secretName", $"Secret '{secName}' is not in the allowed list.");
                            }

                            if (!SecretKey().IsMatch(secKey) || secKey.Length > 253)
                            {
                                errors.Add($"tasks[{idx}].env[{envIdx}].secretKey", "Secret key is invalid.");
                            }
                        }
                    }
                }
            }
        }

        return errors;
    }

    private static List<JobPreviewTaskDto> BuildPreviewTasks(JobInput input)
    {
        var list = new List<JobPreviewTaskDto>();
        if (input.Tasks == null) return list;

        var jobName = (input.Name ?? "job").Trim();
        var ns = (input.Namespace ?? "default").Trim();
        var container = string.IsNullOrWhiteSpace(input.ContainerName) ? "app" : input.ContainerName.Trim();
        var host = string.IsNullOrWhiteSpace(input.DeployRegistryHost) ? (input.RegistryHost ?? "").Trim() : input.DeployRegistryHost.Trim();
        var image = $"{host}/{(input.ImageName ?? "").Trim()}:tag-preview";
        var sa = string.IsNullOrWhiteSpace(input.ServiceAccount) ? null : input.ServiceAccount.Trim();
        var pullSecrets = (input.ImagePullSecrets ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();

        for (var idx = 0; idx < input.Tasks.Length; idx++)
        {
            var t = input.Tasks[idx];
            var pos = idx;
            var label = (t.Label ?? $"task-{pos}").Trim();
            var taskJobName = $"{jobName}-preview-{pos}";

            var env = (t.Env ?? []).Select(e => new JobEnvVar(
                e.Name?.Trim() ?? string.Empty,
                e.Value,
                string.IsNullOrWhiteSpace(e.SecretName) ? null : e.SecretName.Trim(),
                string.IsNullOrWhiteSpace(e.SecretKey) ? null : e.SecretKey.Trim())).ToList();
            var envFromCm = (t.EnvFromConfigMaps ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
            var envFromSec = (t.EnvFromSecrets ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            var args = (t.Args ?? []).Select(a => a.Trim()).ToList();

            var request = new JobTaskRequest(
                Namespace: ns,
                JobName: taskJobName,
                ContainerName: container,
                Image: image,
                Args: args,
                EnvFromConfigMaps: envFromCm,
                EnvFromSecrets: envFromSec,
                Env: env,
                ImagePullSecrets: pullSecrets,
                ServiceAccount: sa,
                Labels: new Dictionary<string, string>
                {
                    ["shipit/job-name"] = jobName,
                    ["shipit/task"] = pos.ToString()
                },
                Timeout: TimeSpan.FromSeconds(t.TimeoutSeconds ?? 1800),
                BackoffLimit: t.BackoffLimit ?? 0,
                Ttl: TimeSpan.FromSeconds(t.TtlSeconds ?? 3600));

            var yaml = JobSpecBuilder.ToYaml(request);
            list.Add(new JobPreviewTaskDto(pos, label, yaml));
        }

        return list;
    }

    private static void Check(FieldErrors errors, string field, string? value, Action<string> validate, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add(field, "This field is required.");
            }

            return;
        }

        try
        {
            validate(value.Trim());
        }
        catch (ArgumentException)
        {
            errors.Add(field, "Value is not valid.");
        }
    }

    private static async Task CheckCredentialAsync(
        FieldErrors errors, ShipitDbContext db, string field, long? id, CredentialKind expected, string expectedWire, CancellationToken ct)
    {
        if (id is null)
        {
            errors.Add(field, "A credential is required.");
            return;
        }

        var kind = await db.Credentials.Where(c => c.Id == id).Select(c => (CredentialKind?)c.Kind).FirstOrDefaultAsync(ct);
        if (kind is null)
        {
            errors.Add(field, "Credential does not exist.");
        }
        else if (kind != expected)
        {
            errors.Add(field, $"Credential must be of kind {expectedWire}.");
        }
    }
}

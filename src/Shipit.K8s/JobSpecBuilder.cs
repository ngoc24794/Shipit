using k8s;
using k8s.Models;
using Shipit.Core;

namespace Shipit.K8s;

/// <summary>
/// Builds Kubernetes batch/v1 Job specifications from <see cref="JobTaskRequest"/>.
/// Pod template is fixed to guarantee non-root execution and security hardening.
/// </summary>
public static class JobSpecBuilder
{
    private const string ManagedByLabelKey = "app.kubernetes.io/managed-by";
    private const string ManagedByLabelValue = "shipit";

    public static V1Job Build(JobTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Timeout must be greater than zero.");
        }

        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Labels != null)
        {
            foreach (var kvp in request.Labels)
            {
                labels[kvp.Key] = kvp.Value;
            }
        }
        labels[ManagedByLabelKey] = ManagedByLabelValue;

        // Container envFrom: ConfigMaps & Secrets (by name only)
        List<V1EnvFromSource>? envFrom = null;
        var hasCm = request.EnvFromConfigMaps != null && request.EnvFromConfigMaps.Count > 0;
        var hasSec = request.EnvFromSecrets != null && request.EnvFromSecrets.Count > 0;
        if (hasCm || hasSec)
        {
            envFrom = new List<V1EnvFromSource>();
            if (hasCm)
            {
                foreach (var cm in request.EnvFromConfigMaps!)
                {
                    if (!string.IsNullOrEmpty(cm))
                    {
                        envFrom.Add(new V1EnvFromSource
                        {
                            ConfigMapRef = new V1ConfigMapEnvSource { Name = cm }
                        });
                    }
                }
            }
            if (hasSec)
            {
                foreach (var sec in request.EnvFromSecrets!)
                {
                    if (!string.IsNullOrEmpty(sec))
                    {
                        envFrom.Add(new V1EnvFromSource
                        {
                            SecretRef = new V1SecretEnvSource { Name = sec }
                        });
                    }
                }
            }
        }

        // Container env: fixed HOME=/tmp followed by request env vars (plain value or secretKeyRef)
        var env = new List<V1EnvVar>
        {
            new() { Name = "HOME", Value = "/tmp" }
        };
        if (request.Env != null)
        {
            foreach (var ev in request.Env)
            {
                if (ev.Value != null)
                {
                    env.Add(new V1EnvVar
                    {
                        Name = ev.Name,
                        Value = ev.Value
                    });
                }
                else if (!string.IsNullOrEmpty(ev.SecretName) && !string.IsNullOrEmpty(ev.SecretKey))
                {
                    env.Add(new V1EnvVar
                    {
                        Name = ev.Name,
                        ValueFrom = new V1EnvVarSource
                        {
                            SecretKeyRef = new V1SecretKeySelector
                            {
                                Name = ev.SecretName,
                                Key = ev.SecretKey
                            }
                        }
                    });
                }
            }
        }

        // Image pull secrets
        List<V1LocalObjectReference>? imagePullSecrets = null;
        if (request.ImagePullSecrets != null && request.ImagePullSecrets.Count > 0)
        {
            imagePullSecrets = request.ImagePullSecrets
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new V1LocalObjectReference { Name = s })
                .ToList();
            if (imagePullSecrets.Count == 0)
            {
                imagePullSecrets = null;
            }
        }

        var container = new V1Container
        {
            Name = request.ContainerName,
            Image = request.Image,
            ImagePullPolicy = "IfNotPresent",
            Args = request.Args != null && request.Args.Count > 0 ? request.Args.ToList() : null,
            Command = null, // Always null: args only
            SecurityContext = new V1SecurityContext
            {
                AllowPrivilegeEscalation = false,
                Capabilities = new V1Capabilities
                {
                    Drop = new List<string> { "ALL" }
                }
            },
            VolumeMounts = new List<V1VolumeMount>
            {
                new()
                {
                    Name = "tmp",
                    MountPath = "/tmp"
                }
            },
            EnvFrom = envFrom,
            Env = env
        };

        var podSpec = new V1PodSpec
        {
            RestartPolicy = "Never",
            SecurityContext = new V1PodSecurityContext
            {
                RunAsNonRoot = true,
                RunAsUser = 1654L,
                RunAsGroup = 1654L,
                FsGroup = 1654L
            },
            ServiceAccountName = string.IsNullOrEmpty(request.ServiceAccount) ? null : request.ServiceAccount,
            ImagePullSecrets = imagePullSecrets,
            Volumes = new List<V1Volume>
            {
                new()
                {
                    Name = "tmp",
                    EmptyDir = new V1EmptyDirVolumeSource
                    {
                        SizeLimit = new ResourceQuantity("512Mi")
                    }
                }
            },
            Containers = new List<V1Container> { container }
        };

        int? ttlSeconds = request.Ttl >= TimeSpan.Zero ? (int)request.Ttl.TotalSeconds : null;

        var job = new V1Job
        {
            ApiVersion = "batch/v1",
            Kind = "Job",
            Metadata = new V1ObjectMeta
            {
                Name = request.JobName,
                NamespaceProperty = request.Namespace,
                Labels = new Dictionary<string, string>(labels, StringComparer.Ordinal)
            },
            Spec = new V1JobSpec
            {
                BackoffLimit = request.BackoffLimit,
                TtlSecondsAfterFinished = ttlSeconds,
                ActiveDeadlineSeconds = (long)request.Timeout.TotalSeconds,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>(labels, StringComparer.Ordinal)
                    },
                    Spec = podSpec
                }
            }
        };

        return job;
    }

    public static V1Job Build(JobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Build(new JobTaskRequest(
            request.Namespace,
            request.JobName,
            request.ContainerName,
            request.Image,
            request.Args,
            request.EnvFromConfigMaps,
            request.EnvFromSecrets,
            request.Env,
            request.ImagePullSecrets,
            request.ServiceAccount,
            request.Labels,
            request.Timeout,
            request.BackoffLimit,
            request.Ttl));
    }

    public static string ToYaml(JobTaskRequest request)
    {
        var job = Build(request);
        return KubernetesYaml.Serialize(job);
    }

    public static string ToYaml(JobRequest request)
    {
        var job = Build(request);
        return KubernetesYaml.Serialize(job);
    }
}

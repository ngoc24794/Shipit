using k8s.Models;
using Shipit.Core;
using Xunit;

namespace Shipit.K8s.Tests;

public class JobSpecBuilderTests
{
    private static JobTaskRequest CreateSampleRequest(
        TimeSpan? timeout = null,
        int backoffLimit = 0,
        TimeSpan? ttl = null,
        IReadOnlyList<string>? args = null,
        IReadOnlyList<string>? envFromCm = null,
        IReadOnlyList<string>? envFromSec = null,
        IReadOnlyList<JobEnvVar>? env = null,
        IReadOnlyList<string>? pullSecrets = null,
        string? sa = null) =>
        new(
            Namespace: "avina-workspace",
            JobName: "job-migrate-1-1",
            ContainerName: "migrator",
            Image: "registry.example.com/avina-dbmigrator:sha123",
            Args: args ?? ["migrate", "--dry-run"],
            EnvFromConfigMaps: envFromCm ?? ["app-config"],
            EnvFromSecrets: envFromSec ?? ["db-credentials"],
            Env: env ??
            [
                new JobEnvVar("APP_MODE", "staging"),
                new JobEnvVar("DB_PASS", SecretName: "db-secret", SecretKey: "password")
            ],
            ImagePullSecrets: pullSecrets ?? ["regcred"],
            ServiceAccount: sa ?? "shipit-migrator-sa",
            Labels: new Dictionary<string, string>
            {
                ["shipit/job-id"] = "42",
                ["shipit/run-id"] = "101",
                ["shipit/task"] = "1"
            },
            Timeout: timeout ?? TimeSpan.FromMinutes(30),
            BackoffLimit: backoffLimit,
            Ttl: ttl ?? TimeSpan.FromHours(1));

    [Fact]
    public void Build_CreatesJobWithSecurityContext_AndHardenedDefaults()
    {
        var request = CreateSampleRequest();
        var job = JobSpecBuilder.Build(request);

        Assert.Equal("batch/v1", job.ApiVersion);
        Assert.Equal("Job", job.Kind);
        Assert.Equal("job-migrate-1-1", job.Metadata.Name);
        Assert.Equal("avina-workspace", job.Metadata.NamespaceProperty);

        // Labels on Job
        Assert.Equal("shipit", job.Metadata.Labels["app.kubernetes.io/managed-by"]);
        Assert.Equal("42", job.Metadata.Labels["shipit/job-id"]);
        Assert.Equal("101", job.Metadata.Labels["shipit/run-id"]);
        Assert.Equal("1", job.Metadata.Labels["shipit/task"]);

        // Job spec properties
        Assert.Equal(0, job.Spec.BackoffLimit);
        Assert.Equal(1800, job.Spec.ActiveDeadlineSeconds);
        Assert.Equal(3600, job.Spec.TtlSecondsAfterFinished);

        // Pod template metadata labels
        Assert.Equal("shipit", job.Spec.Template.Metadata.Labels["app.kubernetes.io/managed-by"]);
        Assert.Equal("42", job.Spec.Template.Metadata.Labels["shipit/job-id"]);

        // Pod spec properties
        var podSpec = job.Spec.Template.Spec;
        Assert.Equal("Never", podSpec.RestartPolicy);
        Assert.Equal("shipit-migrator-sa", podSpec.ServiceAccountName);

        // Pod Security Context
        Assert.NotNull(podSpec.SecurityContext);
        Assert.True(podSpec.SecurityContext.RunAsNonRoot);
        Assert.Equal(1654L, podSpec.SecurityContext.RunAsUser);
        Assert.Equal(1654L, podSpec.SecurityContext.RunAsGroup);
        Assert.Equal(1654L, podSpec.SecurityContext.FsGroup);

        // EmptyDir volume for /tmp (512Mi)
        Assert.Single(podSpec.Volumes);
        var vol = podSpec.Volumes[0];
        Assert.Equal("tmp", vol.Name);
        Assert.NotNull(vol.EmptyDir);
        Assert.Equal("512Mi", vol.EmptyDir.SizeLimit.ToString());

        // Image pull secrets
        Assert.Single(podSpec.ImagePullSecrets);
        Assert.Equal("regcred", podSpec.ImagePullSecrets[0].Name);

        // Container
        Assert.Single(podSpec.Containers);
        var container = podSpec.Containers[0];
        Assert.Equal("migrator", container.Name);
        Assert.Equal("registry.example.com/avina-dbmigrator:sha123", container.Image);
        Assert.Equal("IfNotPresent", container.ImagePullPolicy);
        Assert.Null(container.Command);
        Assert.Equal(["migrate", "--dry-run"], container.Args);

        // Container Security Context
        Assert.NotNull(container.SecurityContext);
        Assert.False(container.SecurityContext.AllowPrivilegeEscalation);
        Assert.Contains("ALL", container.SecurityContext.Capabilities.Drop);

        // Container VolumeMounts
        Assert.Single(container.VolumeMounts);
        Assert.Equal("tmp", container.VolumeMounts[0].Name);
        Assert.Equal("/tmp", container.VolumeMounts[0].MountPath);

        // Container envFrom
        Assert.Equal(2, container.EnvFrom.Count);
        Assert.Equal("app-config", container.EnvFrom[0].ConfigMapRef.Name);
        Assert.Equal("db-credentials", container.EnvFrom[1].SecretRef.Name);

        // Container env (HOME first, then plain and secretKeyRef)
        Assert.Equal(3, container.Env.Count);
        Assert.Equal("HOME", container.Env[0].Name);
        Assert.Equal("/tmp", container.Env[0].Value);
        Assert.Equal("APP_MODE", container.Env[1].Name);
        Assert.Equal("staging", container.Env[1].Value);
        Assert.Equal("DB_PASS", container.Env[2].Name);
        Assert.Equal("db-secret", container.Env[2].ValueFrom.SecretKeyRef.Name);
        Assert.Equal("password", container.Env[2].ValueFrom.SecretKeyRef.Key);
    }

    [Fact]
    public void Build_ThrowsOnZeroOrNegativeTimeout()
    {
        var requestZero = CreateSampleRequest(timeout: TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSpecBuilder.Build(requestZero));

        var requestNegative = CreateSampleRequest(timeout: TimeSpan.FromSeconds(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => JobSpecBuilder.Build(requestNegative));
    }

    [Fact]
    public void ToYaml_ProducesValidYaml_WithoutSecretValues()
    {
        var request = CreateSampleRequest();
        var yaml = JobSpecBuilder.ToYaml(request);

        Assert.NotEmpty(yaml);
        Assert.Contains("kind: Job", yaml);
        Assert.Contains("runAsNonRoot: true", yaml);
        Assert.Contains("1654", yaml);
        Assert.Contains("sizeLimit: 512Mi", yaml);
        Assert.Contains("mountPath: /tmp", yaml);
        Assert.Contains("name: HOME", yaml);
        Assert.Contains("value: /tmp", yaml);
        Assert.Contains("name: db-secret", yaml);
        Assert.Contains("key: password", yaml);
        Assert.Contains("app.kubernetes.io/managed-by: shipit", yaml);
        Assert.Contains("restartPolicy: Never", yaml);

        // Ensure no raw secret value is embedded
        Assert.DoesNotContain("secretValue", yaml);
    }
}

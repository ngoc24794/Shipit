using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Core;
using Shipit.Data.Entities;
using Shipit.K8s;

namespace Shipit.E2E.Tests;

public class JobEndToEndTests
{
    private const string Namespace = "avina-workspace";

    [Fact]
    public async Task HappyPath_DbMigratorJob_SucceedsEndToEnd()
    {
        using var factory = new E2EFactory();
        var client = await factory.LoggedInClientAsync();

        // 1. Create credentials (ssh_key, git_token, registry)
        var sshId = await client.CreateAsync("/api/credentials", new
        {
            name = "job-ssh-key",
            kind = "ssh_key",
            value = E2EFactory.SshKey
        });

        var gitId = await client.CreateAsync("/api/credentials", new
        {
            name = "job-git-token",
            kind = "git_token",
            value = E2EFactory.GitToken
        });

        var regId = await client.CreateAsync("/api/credentials", new
        {
            name = "job-registry-pass",
            kind = "registry",
            username = "e2e-reg-user",
            value = E2EFactory.RegistryPassword
        });

        // 2. Create slave
        var slaveId = await client.CreateAsync("/api/slaves", new
        {
            name = "job-build-slave",
            host = "192.168.1.49",
            port = 22,
            user = "devops",
            sshCredentialId = sshId
        });

        // 3. Create allowlist entries for namespace, configmaps, secrets, and pullsecret
        await client.SeedJobAllowEntriesAsync(Namespace);

        // 4. Validate & Preview DbMigrator Job payload
        var jobPayload = CreateDbMigratorJobPayload(gitId, slaveId, regId, Namespace);

        var validateResponse = await client.PostAsJsonAsync("/api/jobs/validate", jobPayload);
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        var validateJson = await validateResponse.ReadJsonAsync();
        Assert.True(validateJson.GetProperty("valid").GetBoolean(), "Expected job to be valid");
        Assert.Empty(validateJson.GetProperty("errors").EnumerateObject());

        var previewResponse = await client.PostAsJsonAsync("/api/jobs/preview", jobPayload);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var previewJson = await previewResponse.ReadJsonAsync();
        Assert.Empty(previewJson.GetProperty("errors").EnumerateObject());

        var previewTasks = previewJson.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(2, previewTasks.Count);
        Assert.Equal(0, previewTasks[0].GetProperty("position").GetInt32());
        Assert.Equal("files", previewTasks[0].GetProperty("label").GetString());
        var yamlFiles = previewTasks[0].GetProperty("yaml").GetString()!;
        Assert.Contains("kind: Job", yamlFiles);
        Assert.Contains("avina-files-config", yamlFiles);
        Assert.Contains("avina-files-secret", yamlFiles);
        Assert.Contains("runAsNonRoot: true", yamlFiles);
        Assert.Contains("runAsUser: 1654", yamlFiles);
        Assert.Contains("backoffLimit: 0", yamlFiles);
        Assert.Contains("activeDeadlineSeconds: 1800", yamlFiles);

        Assert.Equal(1, previewTasks[1].GetProperty("position").GetInt32());
        Assert.Equal("users", previewTasks[1].GetProperty("label").GetString());
        var yamlUsers = previewTasks[1].GetProperty("yaml").GetString()!;
        Assert.Contains("kind: Job", yamlUsers);
        Assert.Contains("avina-users-config", yamlUsers);
        Assert.Contains("avina-users-secret", yamlUsers);

        // 5. Create the Job
        var jobId = await client.CreateAsync("/api/jobs", jobPayload);

        var jobDetailsResponse = await client.GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, jobDetailsResponse.StatusCode);
        var jobDetailsText = await jobDetailsResponse.Content.ReadAsStringAsync();
        AssertNoSecrets(jobDetailsText);

        // 6. Trigger a Run for the Job
        var triggerResponse = await client.PostAsync($"/api/jobs/{jobId}/runs", content: null);
        Assert.Equal(HttpStatusCode.Accepted, triggerResponse.StatusCode);
        var triggerJson = await triggerResponse.ReadJsonAsync();
        var runId = triggerJson.GetProperty("id").GetInt64();
        Assert.Equal("job", triggerJson.GetProperty("kind").GetString());
        Assert.Equal(jobId, triggerJson.GetProperty("jobId").GetInt64());

        // Connect SSE stream in background to capture live log events
        using var sseCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/logs");
        sseRequest.Headers.Accept.ParseAdd("text/event-stream");
        var sseTask = client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead, sseCts.Token);

        // 7. Wait for run to reach success
        var run = await client.WaitForRunAsync(runId, "success", "failed", "cancelled");
        Assert.Equal("success", run.GetProperty("status").GetString());
        Assert.Equal("abc1234", run.GetProperty("tag").GetString());
        Assert.Equal("192.168.1.241/avina-dbmigrator:abc1234", run.GetProperty("image").GetString());

        // 8. Verify all 4 steps: checkout, build, push, runjob are all success
        var steps = run.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["checkout", "build", "push", "runjob"], steps.Select(s => s.GetProperty("name").GetString()));
        Assert.All(steps, s => Assert.Equal("success", s.GetProperty("status").GetString()));

        // 9. Verify tasks[] has 2 success tasks: files (pos 0) and users (pos 1)
        var tasks = run.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(2, tasks.Count);
        Assert.Equal(0, tasks[0].GetProperty("position").GetInt32());
        Assert.Equal("files", tasks[0].GetProperty("label").GetString());
        Assert.Equal("success", tasks[0].GetProperty("status").GetString());
        Assert.Equal(0, tasks[0].GetProperty("exitCode").GetInt32());

        Assert.Equal(1, tasks[1].GetProperty("position").GetInt32());
        Assert.Equal("users", tasks[1].GetProperty("label").GetString());
        Assert.Equal("success", tasks[1].GetProperty("status").GetString());
        Assert.Equal(0, tasks[1].GetProperty("exitCode").GetInt32());

        // 10. Verify logs via GET /api/runs/{runId}/logs have taskPosition 0 and 1
        var logsResponse = await client.GetAsync($"/api/runs/{runId}/logs");
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
        var logsText = await logsResponse.Content.ReadAsStringAsync();
        AssertNoSecrets(logsText);

        var logLines = (await logsResponse.ReadJsonAsync()).EnumerateArray().ToList();
        Assert.NotEmpty(logLines);

        var taskPositions = logLines
            .Where(l => l.TryGetProperty("taskPosition", out var tp) && tp.ValueKind == JsonValueKind.Number)
            .Select(l => l.GetProperty("taskPosition").GetInt32())
            .Distinct()
            .ToList();
        Assert.Contains(0, taskPositions);
        Assert.Contains(1, taskPositions);

        // 11. Verify SSE stream ends with event "end" and status success
        using var sseResponse = await sseTask;
        Assert.Equal(HttpStatusCode.OK, sseResponse.StatusCode);
        Assert.Equal("text/event-stream", sseResponse.Content.Headers.ContentType?.MediaType);
        var sseBody = await sseResponse.Content.ReadAsStringAsync(sseCts.Token);
        AssertNoSecrets(sseBody);

        Assert.Contains("\"taskPosition\":0", sseBody);
        Assert.Contains("\"taskPosition\":1", sseBody);

        var sseEvents = sseBody.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var lastEvent = sseEvents[^1];
        Assert.Contains("event: end", lastEvent);
        Assert.Contains("\"status\":\"success\"", lastEvent);
    }

    [Fact]
    public async Task FailedTask_Task0ExitsCode3_MarksRunFailed_SkipsTask1_AndStatesExitCodeInFailureReason()
    {
        using var factory = new E2EFactory();
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IK8sJobRunner>(sp =>
                {
                    var allowList = sp.GetRequiredService<IJobAllowList>();
                    var fake = new FakeK8sJobRunner(allowList);
                    fake.ScenariosByPosition["0"] = new FakeJobScenario
                    {
                        ExitCode = 3,
                        FailureMessage = "job exited with code 3"
                    };
                    return fake;
                });
            });
        });

        var client = await app.LoggedInClientAsync();

        // 1. Create resources
        var sshId = await client.CreateAsync("/api/credentials", new
        {
            name = "err-ssh-key",
            kind = "ssh_key",
            value = E2EFactory.SshKey
        });
        var gitId = await client.CreateAsync("/api/credentials", new
        {
            name = "err-git-token",
            kind = "git_token",
            value = E2EFactory.GitToken
        });
        var regId = await client.CreateAsync("/api/credentials", new
        {
            name = "err-reg-pass",
            kind = "registry",
            username = "e2e-reg-user",
            value = E2EFactory.RegistryPassword
        });
        var slaveId = await client.CreateAsync("/api/slaves", new
        {
            name = "err-build-slave",
            host = "192.168.1.49",
            port = 22,
            user = "devops",
            sshCredentialId = sshId
        });

        await client.SeedJobAllowEntriesAsync(Namespace);

        // 2. Create Job
        var jobPayload = CreateDbMigratorJobPayload(gitId, slaveId, regId, Namespace, name: "err-dbmigrator");
        var jobId = await client.CreateAsync("/api/jobs", jobPayload);

        // 3. Trigger run
        var triggerResponse = await client.PostAsync($"/api/jobs/{jobId}/runs", content: null);
        Assert.Equal(HttpStatusCode.Accepted, triggerResponse.StatusCode);
        var triggerJson = await triggerResponse.ReadJsonAsync();
        var runId = triggerJson.GetProperty("id").GetInt64();

        // 4. Wait for run failed
        var run = await client.WaitForRunAsync(runId, "failed", "success");
        Assert.Equal("failed", run.GetProperty("status").GetString());

        var failureReason = run.GetProperty("failureReason").GetString()!;
        Assert.Contains("3", failureReason);

        // Steps: checkout, build, push success; runjob failed
        var steps = run.GetProperty("steps").EnumerateArray()
            .ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("status").GetString()!);
        Assert.Equal("success", steps["checkout"]);
        Assert.Equal("success", steps["build"]);
        Assert.Equal("success", steps["push"]);
        Assert.Equal("failed", steps["runjob"]);

        // Tasks: task 0 failed with exit code 3, task 1 skipped
        var tasks = run.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(2, tasks.Count);

        Assert.Equal(0, tasks[0].GetProperty("position").GetInt32());
        Assert.Equal("files", tasks[0].GetProperty("label").GetString());
        Assert.Equal("failed", tasks[0].GetProperty("status").GetString());
        Assert.Equal(3, tasks[0].GetProperty("exitCode").GetInt32());

        Assert.Equal(1, tasks[1].GetProperty("position").GetInt32());
        Assert.Equal("users", tasks[1].GetProperty("label").GetString());
        Assert.Equal("skipped", tasks[1].GetProperty("status").GetString());

        // Verify response contains no secrets
        var detailResponse = await client.GetAsync($"/api/runs/{runId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailText = await detailResponse.Content.ReadAsStringAsync();
        AssertNoSecrets(detailText);
    }

    [Fact]
    public async Task CancelRun_DuringHangingTask_CancelsRun_DeletesK8sJob_AndSkipsRemainingTasks()
    {
        var taskStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeK8sJobRunner? capturedRunner = null;

        using var factory = new E2EFactory();
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IK8sJobRunner>(sp =>
                {
                    var allowList = sp.GetRequiredService<IJobAllowList>();
                    var fake = new FakeK8sJobRunner(allowList)
                    {
                        ScenarioFactory = _ =>
                        {
                            taskStarted.TrySetResult();
                            return new FakeJobScenario { HangUntilCancelled = true };
                        }
                    };
                    capturedRunner = fake;
                    return fake;
                });
            });
        });

        var client = await app.LoggedInClientAsync();

        var sshId = await client.CreateAsync("/api/credentials", new
        {
            name = "cancel-ssh-key",
            kind = "ssh_key",
            value = E2EFactory.SshKey
        });
        var gitId = await client.CreateAsync("/api/credentials", new
        {
            name = "cancel-git-token",
            kind = "git_token",
            value = E2EFactory.GitToken
        });
        var regId = await client.CreateAsync("/api/credentials", new
        {
            name = "cancel-reg-pass",
            kind = "registry",
            username = "e2e-reg-user",
            value = E2EFactory.RegistryPassword
        });
        var slaveId = await client.CreateAsync("/api/slaves", new
        {
            name = "cancel-build-slave",
            host = "192.168.1.49",
            port = 22,
            user = "devops",
            sshCredentialId = sshId
        });

        await client.SeedJobAllowEntriesAsync(Namespace);

        var jobPayload = CreateDbMigratorJobPayload(gitId, slaveId, regId, Namespace, name: "cancel-dbmigrator");
        var jobId = await client.CreateAsync("/api/jobs", jobPayload);

        // Connect SSE client before triggering
        var triggerResponse = await client.PostAsync($"/api/jobs/{jobId}/runs", content: null);
        Assert.Equal(HttpStatusCode.Accepted, triggerResponse.StatusCode);
        var triggerJson = await triggerResponse.ReadJsonAsync();
        var runId = triggerJson.GetProperty("id").GetInt64();

        using var sseCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/logs");
        sseRequest.Headers.Accept.ParseAdd("text/event-stream");
        var sseTask = client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead, sseCts.Token);

        // Wait until FakeK8sJobRunner begins executing task 0
        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Send cancel request
        var cancelResponse = await client.PostAsync($"/api/runs/{runId}/cancel", content: null);
        Assert.Equal(HttpStatusCode.Accepted, cancelResponse.StatusCode);

        // SSE should close with event: end and cancelled status
        using var sseResponse = await sseTask;
        Assert.Equal(HttpStatusCode.OK, sseResponse.StatusCode);
        var sseBody = await sseResponse.Content.ReadAsStringAsync(sseCts.Token);
        AssertNoSecrets(sseBody);
        Assert.Contains("event: end", sseBody);
        Assert.Contains("\"status\":\"cancelled\"", sseBody);

        // Run should be cancelled
        var run = await client.WaitForRunAsync(runId, "cancelled");
        Assert.Equal("cancelled", run.GetProperty("status").GetString());

        var steps = run.GetProperty("steps").EnumerateArray()
            .ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("status").GetString()!);
        Assert.Equal("success", steps["checkout"]);
        Assert.Equal("success", steps["build"]);
        Assert.Equal("success", steps["push"]);
        Assert.Equal("cancelled", steps["runjob"]);

        var tasks = run.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(2, tasks.Count);
        Assert.Equal("cancelled", tasks[0].GetProperty("status").GetString());
        Assert.Equal("cancelled", tasks[1].GetProperty("status").GetString());

        // Verify no secret leak in logs
        var logsResponse = await client.GetAsync($"/api/runs/{runId}/logs");
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
        var logsText = await logsResponse.Content.ReadAsStringAsync();
        AssertNoSecrets(logsText);
    }

    private static object CreateDbMigratorJobPayload(long gitId, long slaveId, long regId, string ns, string name = "avina-dbmigrator")
    {
        return new
        {
            name,
            gitUrl = "https://git.example.com/team/avina.git",
            gitBranch = "main",
            gitCredentialId = gitId,
            slaveId,
            dockerfilePath = "docker/Dockerfile.dbmigrator",
            buildContext = ".",
            imageName = "avina-dbmigrator",
            registryHost = "192.168.1.241",
            deployRegistryHost = (string?)null,
            registryCredentialId = regId,
            @namespace = ns,
            containerName = "app",
            serviceAccount = (string?)null,
            imagePullSecrets = new[] { "registry-secret" },
            tasks = new object[]
            {
                new
                {
                    label = "files",
                    args = new[] { "migrate", "--service", "files" },
                    envFromConfigMaps = new[] { "avina-files-config" },
                    envFromSecrets = new[] { "avina-files-secret" },
                    env = Array.Empty<object>(),
                    timeoutSeconds = 1800,
                    backoffLimit = 0,
                    ttlSeconds = 3600
                },
                new
                {
                    label = "users",
                    args = new[] { "migrate", "--service", "users" },
                    envFromConfigMaps = new[] { "avina-users-config" },
                    envFromSecrets = new[] { "avina-users-secret" },
                    env = Array.Empty<object>(),
                    timeoutSeconds = 1800,
                    backoffLimit = 0,
                    ttlSeconds = 3600
                }
            }
        };
    }

    private static void AssertNoSecrets(string content)
    {
        foreach (var secret in E2EFactory.Secrets)
        {
            Assert.DoesNotContain(secret, content, StringComparison.OrdinalIgnoreCase);
        }
    }
}

internal static class JobTestClientExtensions
{
    public static async Task SeedJobAllowEntriesAsync(this HttpClient client, string ns)
    {
        await client.CreateAsync("/api/job-allow-entries", new { @namespace = ns, kind = "namespace", name = ns });
        await client.CreateAsync("/api/job-allow-entries", new { @namespace = ns, kind = "configmap", name = "avina-files-config" });
        await client.CreateAsync("/api/job-allow-entries", new { @namespace = ns, kind = "configmap", name = "avina-users-config" });
        await client.CreateAsync("/api/job-allow-entries", new { @namespace = ns, kind = "secret", name = "avina-files-secret" });
        await client.CreateAsync("/api/job-allow-entries", new { @namespace = ns, kind = "secret", name = "avina-users-secret" });
        await client.CreateAsync("/api/job-allow-entries", new { @namespace = ns, kind = "pullsecret", name = "registry-secret" });
    }
}

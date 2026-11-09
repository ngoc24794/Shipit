using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;
using Shipit.K8s;
using Shipit.Ssh;

namespace Shipit.E2E.Tests;

public class EndToEndFlowTests
{
    [Fact]
    public async Task HappyPath_MVP_SucceedsEndToEnd()
    {
        using var factory = new E2EFactory();
        var client = await factory.LoggedInClientAsync();

        // 1. Create 3 credentials
        var sshId = await client.CreateAsync("/api/credentials", new
        {
            name = "happy-ssh-key",
            kind = "ssh_key",
            value = E2EFactory.SshKey
        });
        var gitId = await client.CreateAsync("/api/credentials", new
        {
            name = "happy-git-token",
            kind = "git_token",
            value = E2EFactory.GitToken
        });
        var regId = await client.CreateAsync("/api/credentials", new
        {
            name = "happy-registry-pass",
            kind = "registry",
            username = "ci-bot",
            value = E2EFactory.RegistryPassword
        });

        // 2. Create slave
        var slaveId = await client.CreateAsync("/api/slaves", new
        {
            name = "happy-slave-1",
            host = "192.168.1.49",
            port = 22,
            user = "devops",
            sshCredentialId = sshId
        });

        // 3. Test slave connection
        var testResponse = await client.PostAsync($"/api/slaves/{slaveId}/test", content: null);
        Assert.Equal(HttpStatusCode.OK, testResponse.StatusCode);
        var testResult = await testResponse.ReadJsonAsync();
        Assert.True(testResult.GetProperty("ok").GetBoolean());

        // 4. Create pipeline
        var pipelineId = await client.CreateAsync("/api/pipelines", new
        {
            name = "happy-pipeline",
            gitUrl = "https://git.example.com/team/demo.git",
            gitBranch = "main",
            gitCredentialId = gitId,
            slaveId = slaveId,
            dockerfilePath = "Dockerfile",
            imageName = "shipit-demo",
            registryHost = "192.168.1.241",
            registryCredentialId = regId,
            deployNamespace = "devops",
            deployDeployment = "shipit-demo",
            deployContainer = "app"
        });

        // 5. Trigger run
        var runId = await client.TriggerRunAsync(pipelineId);

        // 6. Wait for success
        var run = await client.WaitForRunAsync(runId, "success", "failed");
        Assert.Equal("success", run.GetProperty("status").GetString());
        Assert.Equal("abc1234", run.GetProperty("tag").GetString());
        Assert.Equal("192.168.1.241/shipit-demo:abc1234", run.GetProperty("image").GetString());

        // 7. Verify 4 steps are all success
        var steps = run.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["checkout", "build", "push", "deploy"], steps.Select(s => s.GetProperty("name").GetString()));
        Assert.All(steps, s => Assert.Equal("success", s.GetProperty("status").GetString()));

        // 8. Verify JSON logs in order and contain all steps
        var logsResponse = await client.GetAsync($"/api/runs/{runId}/logs");
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
        var logsJson = await logsResponse.ReadJsonAsync();
        var logLines = logsJson.EnumerateArray().ToList();
        Assert.NotEmpty(logLines);

        var seqs = logLines.Select(l => l.GetProperty("seq").GetInt64()).ToList();
        Assert.Equal(seqs.OrderBy(x => x), seqs);

        var loggedSteps = logLines.Select(l => l.GetProperty("step").GetString()).Distinct().ToList();
        Assert.Contains("checkout", loggedSteps);
        Assert.Contains("build", loggedSteps);
        Assert.Contains("push", loggedSteps);
        Assert.Contains("deploy", loggedSteps);

        // 9. Verify SSE stream ends with event "end" and success status
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/logs");
        sseRequest.Headers.Accept.ParseAdd("text/event-stream");
        using var sseResponse = await client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", sseResponse.Content.Headers.ContentType?.MediaType);

        var sseBody = await sseResponse.Content.ReadAsStringAsync(cts.Token);
        var sseEvents = sseBody.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var lastEvent = sseEvents[^1];
        Assert.Contains("event: end", lastEvent);
        Assert.Contains("\"status\":\"success\"", lastEvent);
    }

    [Fact]
    public async Task DockerBuildFailure_FailsRun_SkipsLaterSteps_LogsOut_AndDoesNotDeploy()
    {
        var customSsh = new FakeSshExecutor();
        customSsh.AddRule(cmd => cmd.CommandText.Contains("git clone"), exitCode: 0, outputLines: [("Cloning repo...", LogStream.Stdout)]);
        customSsh.AddRule(cmd => cmd.CommandText.Contains("rev-parse --short=7 HEAD"), exitCode: 0, outputLines: [("abc1234", LogStream.Stdout)]);
        customSsh.AddRule(cmd => cmd.CommandText.Contains("docker login"), exitCode: 0, outputLines: [("Login Succeeded", LogStream.Stdout)]);
        customSsh.AddRule(cmd => cmd.CommandText.Contains("docker build"), exitCode: 1, outputLines: [("Error: docker build failed", LogStream.Stderr)]);
        customSsh.AddRule(cmd => cmd.CommandText.Contains("docker logout"), exitCode: 0, outputLines: [("Removing login credentials", LogStream.Stdout)]);
        customSsh.AddRule(cmd => cmd.CommandText.Contains("rm -rf"), exitCode: 0);
        customSsh.AddRule(cmd => cmd.CommandText.Contains("echo shipit-ok"), exitCode: 0, outputLines: [("shipit-ok", LogStream.Stdout)]);
        customSsh.AddRule(cmd => cmd.CommandText.Contains("docker version"), exitCode: 0, outputLines: [("24.0.7", LogStream.Stdout)]);

        var customK8s = new FakeK8sDeployer(new DeployAllowList([new DeployAllowListEntry("devops", ["shipit-demo"])]));

        using var factory = new E2EFactory();
        using var customApp = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISshExecutor>(customSsh);
                services.AddSingleton<IK8sDeployer>(customK8s);
            });
        });

        var client = await customApp.LoggedInClientAsync();
        var seeded = await client.SeedResourcesAsync();

        var runId = await client.TriggerRunAsync(seeded.PipelineId);
        var run = await client.WaitForRunAsync(runId, "failed", "success");

        Assert.Equal("failed", run.GetProperty("status").GetString());
        var failureReason = run.GetProperty("failureReason").GetString();
        Assert.NotNull(failureReason);
        Assert.StartsWith("build failed", failureReason, StringComparison.OrdinalIgnoreCase);

        var steps = run.GetProperty("steps").EnumerateArray().ToDictionary(
            s => s.GetProperty("name").GetString()!,
            s => s.GetProperty("status").GetString()!);

        Assert.Equal("success", steps["checkout"]);
        Assert.Equal("failed", steps["build"]);
        Assert.Equal("skipped", steps["push"]);
        Assert.Equal("skipped", steps["deploy"]);

        // No deployment was requested
        Assert.Empty(customK8s.Requests);

        // Docker logout was executed during cleanup
        Assert.Contains(customSsh.ExecutedCommands, cmd => cmd.CommandText.Contains("docker logout"));
    }

    [Fact]
    public async Task DeployRejectedByAllowList_FailsAtDeployStep()
    {
        // The host's DeployAllowList allows devops/shipit-demo, so pipeline creation succeeds.
        // We inject a FakeK8sDeployer with an empty allowlist so the deploy request is rejected at runtime.
        var rejectingDeployer = new FakeK8sDeployer(new DeployAllowList([]));

        using var factory = new E2EFactory();
        using var customApp = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IK8sDeployer>(rejectingDeployer);
            });
        });

        var client = await customApp.LoggedInClientAsync();
        var seeded = await client.SeedResourcesAsync();

        var runId = await client.TriggerRunAsync(seeded.PipelineId);
        var run = await client.WaitForRunAsync(runId, "failed", "success");

        Assert.Equal("failed", run.GetProperty("status").GetString());
        var failureReason = run.GetProperty("failureReason").GetString();
        Assert.NotNull(failureReason);
        Assert.StartsWith("deploy failed", failureReason, StringComparison.OrdinalIgnoreCase);

        var steps = run.GetProperty("steps").EnumerateArray().ToDictionary(
            s => s.GetProperty("name").GetString()!,
            s => s.GetProperty("status").GetString()!);

        Assert.Equal("success", steps["checkout"]);
        Assert.Equal("success", steps["build"]);
        Assert.Equal("success", steps["push"]);
        Assert.Equal("failed", steps["deploy"]);
    }

    [Fact]
    public async Task NoSecretLeak_InApiResponses_Logs_AndSshCommands()
    {
        var customSsh = FakeSshExecutor.HappyPath();

        using var factory = new E2EFactory();
        using var customApp = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISshExecutor>(customSsh);
            });
        });

        var client = await customApp.LoggedInClientAsync();
        var seeded = await client.SeedResourcesAsync();

        var runId = await client.TriggerRunAsync(seeded.PipelineId);
        await client.WaitForRunAsync(runId, "success");

        // Collect all API responses
        var credsList = await (await client.GetAsync("/api/credentials")).Content.ReadAsStringAsync();
        var sshCred = await (await client.GetAsync($"/api/credentials/{seeded.SshId}")).Content.ReadAsStringAsync();
        var gitCred = await (await client.GetAsync($"/api/credentials/{seeded.GitId}")).Content.ReadAsStringAsync();
        var regCred = await (await client.GetAsync($"/api/credentials/{seeded.RegId}")).Content.ReadAsStringAsync();

        var slavesList = await (await client.GetAsync("/api/slaves")).Content.ReadAsStringAsync();
        var slaveDetail = await (await client.GetAsync($"/api/slaves/{seeded.SlaveId}")).Content.ReadAsStringAsync();

        var pipelinesList = await (await client.GetAsync("/api/pipelines")).Content.ReadAsStringAsync();
        var pipelineDetail = await (await client.GetAsync($"/api/pipelines/{seeded.PipelineId}")).Content.ReadAsStringAsync();

        var runDetail = await (await client.GetAsync($"/api/runs/{runId}")).Content.ReadAsStringAsync();
        var jsonLogs = await (await client.GetAsync($"/api/runs/{runId}/logs")).Content.ReadAsStringAsync();

        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/logs");
        sseRequest.Headers.Accept.ParseAdd("text/event-stream");
        var sseResponse = await client.SendAsync(sseRequest);
        var sseLogs = await sseResponse.Content.ReadAsStringAsync();

        var allResponses = new[]
        {
            credsList, sshCred, gitCred, regCred,
            slavesList, slaveDetail,
            pipelinesList, pipelineDetail,
            runDetail, jsonLogs, sseLogs
        };

        foreach (var secret in E2EFactory.Secrets)
        {
            foreach (var responseText in allResponses)
            {
                Assert.DoesNotContain(secret, responseText);
            }

            foreach (var cmd in customSsh.ExecutedCommands)
            {
                Assert.DoesNotContain(secret, cmd.CommandText);
            }
        }
    }

    [Fact]
    public async Task OneRunPerPipeline_SecondRunReturnsConflict_UntilFirstCompletes()
    {
        var slowSsh = new FakeSshExecutor();
        slowSsh.AddRule(cmd => cmd.CommandText.Contains("git clone"), exitCode: 0, outputLines: [("Cloning...", LogStream.Stdout)]);
        slowSsh.AddRule(cmd => cmd.CommandText.Contains("rev-parse --short=7 HEAD"), exitCode: 0, outputLines: [("abc1234", LogStream.Stdout)]);
        slowSsh.AddRule(cmd => cmd.CommandText.Contains("docker login"), exitCode: 0, outputLines: [("Login Succeeded", LogStream.Stdout)]);
        slowSsh.AddRule(cmd => cmd.CommandText.Contains("docker build"), exitCode: 0, outputLines: [("Built image", LogStream.Stdout)], delay: TimeSpan.FromSeconds(2));
        slowSsh.AddRule(cmd => cmd.CommandText.Contains("docker push"), exitCode: 0, outputLines: [("Pushed image", LogStream.Stdout)]);
        slowSsh.AddRule(cmd => cmd.CommandText.Contains("docker logout"), exitCode: 0);
        slowSsh.AddRule(cmd => cmd.CommandText.Contains("rm -rf"), exitCode: 0);
        slowSsh.AddRule(cmd => cmd.CommandText.Contains("echo shipit-ok"), exitCode: 0, outputLines: [("shipit-ok", LogStream.Stdout)]);
        slowSsh.AddRule(cmd => cmd.CommandText.Contains("docker version"), exitCode: 0, outputLines: [("24.0.7", LogStream.Stdout)]);

        using var factory = new E2EFactory();
        using var customApp = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISshExecutor>(slowSsh);
            });
        });

        var client = await customApp.LoggedInClientAsync();
        var seeded = await client.SeedResourcesAsync();

        // 1. Trigger first run
        var firstRunResponse = await client.PostAsync($"/api/pipelines/{seeded.PipelineId}/runs", null);
        Assert.Equal(HttpStatusCode.Accepted, firstRunResponse.StatusCode);
        var run1Id = (await firstRunResponse.ReadJsonAsync()).GetProperty("id").GetInt64();

        // 2. Trigger second run immediately while first is running -> 409 Conflict
        var secondRunResponse = await client.PostAsync($"/api/pipelines/{seeded.PipelineId}/runs", null);
        Assert.Equal(HttpStatusCode.Conflict, secondRunResponse.StatusCode);

        // 3. Wait for first run to finish
        var run1 = await client.WaitForRunAsync(run1Id, "success");
        Assert.Equal("success", run1.GetProperty("status").GetString());

        // 4. Trigger new run now that the first has completed -> 202 Accepted
        var thirdRunResponse = await client.PostAsync($"/api/pipelines/{seeded.PipelineId}/runs", null);
        Assert.Equal(HttpStatusCode.Accepted, thirdRunResponse.StatusCode);
        var run3Id = (await thirdRunResponse.ReadJsonAsync()).GetProperty("id").GetInt64();

        var run3 = await client.WaitForRunAsync(run3Id, "success");
        Assert.Equal("success", run3.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RecoveryAfterRestart_InterruptedRunningRun_BecomesFailedWithInterruptedReason()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"shipit-restart-{Guid.NewGuid():N}.db");
        long pipelineId;
        long runId;

        try
        {
            // Step 1: Initialize DB schema and seed pipeline using Factory 1
            using (var factory1 = new E2EFactory(dbPath: dbPath, deleteDbOnDispose: false))
            {
                var client1 = await factory1.LoggedInClientAsync();
                var seeded = await client1.SeedResourcesAsync();
                pipelineId = seeded.PipelineId;
            }

            // Step 2: Directly insert a 'Running' run into the SQLite DB (as if the app was killed during execution)
            var options = new DbContextOptionsBuilder<ShipitDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            await using (var db = new ShipitDbContext(options))
            {
                var run = new Run
                {
                    PipelineId = pipelineId,
                    Status = RunStatus.Running,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                    StartedAt = DateTime.UtcNow.AddMinutes(-4),
                    Steps =
                    [
                        new RunStep { Name = StepName.Checkout, Status = StepStatus.Success, StartedAt = DateTime.UtcNow.AddMinutes(-4), FinishedAt = DateTime.UtcNow.AddMinutes(-3) },
                        new RunStep { Name = StepName.Build, Status = StepStatus.Running, StartedAt = DateTime.UtcNow.AddMinutes(-3) },
                        new RunStep { Name = StepName.Push, Status = StepStatus.Pending },
                        new RunStep { Name = StepName.Deploy, Status = StepStatus.Pending },
                    ]
                };
                db.Runs.Add(run);
                await db.SaveChangesAsync();
                runId = run.Id;
            }

            // Step 3: Start Factory 2 on the SAME SQLite file
            using (var factory2 = new E2EFactory(dbPath: dbPath, deleteDbOnDispose: false))
            {
                var client2 = await factory2.LoggedInClientAsync();

                // On startup, RunCoordinator.RecoverAsync marks leftover Running runs as Failed with failureReason "interrupted"
                var runJson = await client2.WaitForRunAsync(runId, "failed");
                Assert.Equal("failed", runJson.GetProperty("status").GetString());
                Assert.Equal("interrupted", runJson.GetProperty("failureReason").GetString());

                var steps = runJson.GetProperty("steps").EnumerateArray().ToDictionary(
                    s => s.GetProperty("name").GetString()!,
                    s => s.GetProperty("status").GetString()!);

                Assert.Equal("success", steps["checkout"]);
                Assert.Equal("failed", steps["build"]);
                Assert.Equal("skipped", steps["push"]);
                Assert.Equal("skipped", steps["deploy"]);
            }
        }
        finally
        {
            E2EFactory.CleanupFiles(dbPath);
        }
    }

    [Fact]
    public async Task UiServing_ReturnsIndexHtml_AndSpaFallback_OrConditionalSkipIfUnbuilt()
    {
        using var factory = new E2EFactory();
        var client = await factory.LoggedInClientAsync();

        // Check if index.html exists in src/Shipit.Api/wwwroot
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var indexPath = Path.Combine(repoRoot, "src", "Shipit.Api", "wwwroot", "index.html");

        if (File.Exists(indexPath))
        {
            // Root should return HTML
            var rootResponse = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
            Assert.Contains("text/html", rootResponse.Content.Headers.ContentType?.MediaType);
            var rootContent = await rootResponse.Content.ReadAsStringAsync();
            Assert.Contains("<html", rootContent, StringComparison.OrdinalIgnoreCase);

            // Client route (SPA fallback) should return HTML
            var spaResponse = await client.GetAsync("/runs/1");
            Assert.Equal(HttpStatusCode.OK, spaResponse.StatusCode);
            Assert.Contains("text/html", spaResponse.Content.Headers.ContentType?.MediaType);
            var spaContent = await spaResponse.Content.ReadAsStringAsync();
            Assert.Contains("<html", spaContent, StringComparison.OrdinalIgnoreCase);
        }

        // Unknown API route must return 404
        var unknownResponse = await client.GetAsync("/api/unknown");
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
    }
}

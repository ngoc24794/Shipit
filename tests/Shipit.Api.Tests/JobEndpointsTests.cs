using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api.Tests;

public class JobEndpointsTests
{
    private static async Task<(long JobId, long SlaveId, long GitCredId, long RegCredId)> CreateTestPrerequisitesAsync(
        HttpClient client, string jobName = "test-job", string ns = "devops")
    {
        var seeded = await client.SeedAsync();

        // Ensure allowlist has the namespace, configmaps, and secrets needed
        await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = ns, kind = "namespace", name = ns });
        await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = ns, kind = "configmap", name = "app-cm" });
        await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = ns, kind = "secret", name = "app-sec" });
        await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = ns, kind = "pullsecret", name = "reg-pull-sec" });

        var jobPayload = new
        {
            name = jobName,
            gitUrl = "https://github.com/example/repo.git",
            gitBranch = "main",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            dockerfilePath = "Dockerfile",
            imageName = "my-job",
            registryHost = "registry.example.com",
            registryCredentialId = seeded.RegCred,
            @namespace = ns,
            containerName = "app",
            imagePullSecrets = new[] { "reg-pull-sec" },
            tasks = new object[]
            {
                new
                {
                    label = "task-zero",
                    args = new[] { "--migrate", "step1" },
                    envFromConfigMaps = new[] { "app-cm" },
                    envFromSecrets = new[] { "app-sec" },
                    env = new[]
                    {
                        new { name = "PLAIN_VAR", value = (string?)"hello", secretName = (string?)null, secretKey = (string?)null },
                        new { name = "SECRET_VAR", value = (string?)null, secretName = (string?)"app-sec", secretKey = (string?)"password" }
                    },
                    timeoutSeconds = (int?)1200,
                    backoffLimit = (int?)1,
                    ttlSeconds = (int?)1800
                },
                new
                {
                    label = "task-one",
                    args = new[] { "--seed" },
                    envFromConfigMaps = (string[]?)null,
                    envFromSecrets = (string[]?)null,
                    env = new[]
                    {
                        new { name = "APP_ENV", value = "production", secretName = (string?)null, secretKey = (string?)null }
                    },
                    timeoutSeconds = (int?)null,
                    backoffLimit = (int?)null,
                    ttlSeconds = (int?)null
                }
            }
        };

        var resp = await client.PostAsJsonAsync("/api/jobs", jobPayload);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var created = await resp.ReadJsonAsync();
        var jobId = created.GetProperty("id").GetInt64();
        return (jobId, seeded.Slave, seeded.GitCred, seeded.RegCred);
    }

    [Fact]
    public async Task Job_CRUD_and_task_order_preserved()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        var (jobId, slaveId, gitCredId, regCredId) = await CreateTestPrerequisitesAsync(client, "crud-job");

        // 1. Get job by ID: tasks ordered by position, timeout defaults applied for task 1
        var getResp = await client.GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var job = await getResp.ReadJsonAsync();
        Assert.Equal("crud-job", job.GetProperty("name").GetString());
        Assert.Equal("devops", job.GetProperty("namespace").GetString());
        Assert.Equal("app", job.GetProperty("containerName").GetString());
        Assert.Null(job.GetProperty("lastRun").ValueKind == JsonValueKind.Null ? null : "not-null");

        var tasks = job.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(2, tasks.Count);
        Assert.Equal(0, tasks[0].GetProperty("position").GetInt32());
        Assert.Equal("task-zero", tasks[0].GetProperty("label").GetString());
        Assert.Equal(1200, tasks[0].GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal(1, tasks[0].GetProperty("backoffLimit").GetInt32());
        Assert.Equal(1800, tasks[0].GetProperty("ttlSeconds").GetInt32());
        Assert.Equal(new[] { "--migrate", "step1" }, tasks[0].GetProperty("args").EnumerateArray().Select(a => a.GetString()!).ToArray());

        Assert.Equal(1, tasks[1].GetProperty("position").GetInt32());
        Assert.Equal("task-one", tasks[1].GetProperty("label").GetString());
        Assert.Equal(1800, tasks[1].GetProperty("timeoutSeconds").GetInt32()); // default applied
        Assert.Equal(0, tasks[1].GetProperty("backoffLimit").GetInt32()); // default applied
        Assert.Equal(3600, tasks[1].GetProperty("ttlSeconds").GetInt32()); // default applied

        // 2. List jobs
        var listResp = await client.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.ReadJsonAsync();
        Assert.Contains(list.EnumerateArray(), j => j.GetProperty("id").GetInt64() == jobId);

        // 3. Update job: replace tasks with a single new task
        var updatePayload = new
        {
            name = "crud-job-updated",
            gitUrl = "https://github.com/example/repo.git",
            gitBranch = "release",
            gitCredentialId = gitCredId,
            slaveId,
            imageName = "my-job-updated",
            registryHost = "registry.example.com",
            @namespace = "devops",
            containerName = "worker",
            tasks = new[]
            {
                new
                {
                    label = "single-task",
                    args = new[] { "--run-all" }
                }
            }
        };

        var putResp = await client.PutAsJsonAsync($"/api/jobs/{jobId}", updatePayload);
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        var updated = await putResp.ReadJsonAsync();
        Assert.Equal("crud-job-updated", updated.GetProperty("name").GetString());
        Assert.Equal("release", updated.GetProperty("gitBranch").GetString());
        Assert.Equal("worker", updated.GetProperty("containerName").GetString());
        var updatedTasks = updated.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Single(updatedTasks);
        Assert.Equal(0, updatedTasks[0].GetProperty("position").GetInt32());
        Assert.Equal("single-task", updatedTasks[0].GetProperty("label").GetString());

        // 4. Delete job when no active runs -> 204
        var delResp = await client.DeleteAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // 5. GET deleted job -> 404
        var get404 = await client.GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.NotFound, get404.StatusCode);
    }

    [Fact]
    public async Task Delete_job_returns_409_when_run_is_active()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        var (jobId, _, _, _) = await CreateTestPrerequisitesAsync(client, "active-run-job");

        // Manually insert an active run for this job into the database
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Runs.Add(new Run
            {
                JobId = jobId,
                Status = RunStatus.Running,
                Tag = "abc1234",
                Image = "registry.example.com/my-job:abc1234"
            });
            await db.SaveChangesAsync();
        }

        // Delete should return 409 Conflict
        var delResp = await client.DeleteAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.Conflict, delResp.StatusCode);
    }

    [Fact]
    public async Task Validation_rules_produce_expected_field_errors()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        // 1. Invalid namespace outside allowlist
        var invalidNsPayload = new
        {
            name = "valid-name",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "unknown-ns",
            tasks = new[]
            {
                new { label = "task1" }
            }
        };
        var resp1 = await client.PostAsJsonAsync("/api/jobs/validate", invalidNsPayload);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        var val1 = await resp1.ReadJsonAsync();
        Assert.False(val1.GetProperty("valid").GetBoolean());
        var errors1 = val1.GetProperty("errors");
        Assert.True(errors1.TryGetProperty("namespace", out _));

        // Allow namespace "devops"
        await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = "devops", kind = "namespace", name = "devops" });

        // 2. ConfigMap outside allowlist
        var invalidCmPayload = new
        {
            name = "valid-name",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "devops",
            tasks = new[]
            {
                new
                {
                    label = "task1",
                    envFromConfigMaps = new[] { "not-allowed-cm" }
                }
            }
        };
        var resp2 = await client.PostAsJsonAsync("/api/jobs/validate", invalidCmPayload);
        var val2 = await resp2.ReadJsonAsync();
        Assert.False(val2.GetProperty("valid").GetBoolean());
        Assert.True(val2.GetProperty("errors").TryGetProperty("tasks[0].envFromConfigMaps[0]", out _));

        // 3. Secret outside allowlist (in envFromSecrets and in env)
        var invalidSecPayload = new
        {
            name = "valid-name",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "devops",
            tasks = new[]
            {
                new
                {
                    label = "task1",
                    envFromSecrets = new[] { "not-allowed-sec" },
                    env = new[]
                    {
                        new { name = "SEC_REF", secretName = "not-allowed-sec-2", secretKey = "key" }
                    }
                }
            }
        };
        var resp3 = await client.PostAsJsonAsync("/api/jobs/validate", invalidSecPayload);
        var val3 = await resp3.ReadJsonAsync();
        Assert.False(val3.GetProperty("valid").GetBoolean());
        var errors3 = val3.GetProperty("errors");
        Assert.True(errors3.TryGetProperty("tasks[0].envFromSecrets[0]", out _));
        Assert.True(errors3.TryGetProperty("tasks[0].env[0].secretName", out _));

        // 4. Image pull secret outside allowlist
        var invalidPullSecPayload = new
        {
            name = "valid-name",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "devops",
            imagePullSecrets = new[] { "not-allowed-pull" },
            tasks = new[] { new { label = "task1" } }
        };
        var resp4 = await client.PostAsJsonAsync("/api/jobs/validate", invalidPullSecPayload);
        var val4 = await resp4.ReadJsonAsync();
        Assert.False(val4.GetProperty("valid").GetBoolean());
        Assert.True(val4.GetProperty("errors").TryGetProperty("imagePullSecrets[0]", out _));

        // 5. Env var duplicate name
        var duplicateEnvPayload = new
        {
            name = "valid-name",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "devops",
            tasks = new[]
            {
                new
                {
                    label = "task1",
                    env = new[]
                    {
                        new { name = "MY_VAR", value = "first" },
                        new { name = "MY_VAR", value = "second" }
                    }
                }
            }
        };
        var resp5 = await client.PostAsJsonAsync("/api/jobs/validate", duplicateEnvPayload);
        var val5 = await resp5.ReadJsonAsync();
        Assert.False(val5.GetProperty("valid").GetBoolean());
        Assert.True(val5.GetProperty("errors").TryGetProperty("tasks[0].env[1].name", out _));

        // 6. Args containing control character (newline)
        var ctrlArgsPayload = new
        {
            name = "valid-name",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "devops",
            tasks = new[]
            {
                new
                {
                    label = "task1",
                    args = new[] { "arg1\narg2" }
                }
            }
        };
        var resp6 = await client.PostAsJsonAsync("/api/jobs/validate", ctrlArgsPayload);
        var val6 = await resp6.ReadJsonAsync();
        Assert.False(val6.GetProperty("valid").GetBoolean());
        Assert.True(val6.GetProperty("errors").TryGetProperty("tasks[0].args", out _));

        // 7. Credential wrong kind: SshKey used as gitCredentialId
        var wrongCredPayload = new
        {
            name = "valid-name",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.SshCred, // SshKey instead of GitToken
            slaveId = seeded.Slave,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "devops",
            tasks = new[] { new { label = "task1" } }
        };
        var resp7 = await client.PostAsJsonAsync("/api/jobs/validate", wrongCredPayload);
        var val7 = await resp7.ReadJsonAsync();
        Assert.False(val7.GetProperty("valid").GetBoolean());
        Assert.True(val7.GetProperty("errors").TryGetProperty("gitCredentialId", out _));
    }

    [Fact]
    public async Task Duplicate_job_name_returns_409_on_create_and_update()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        var (job1Id, slaveId, gitCredId, _) = await CreateTestPrerequisitesAsync(client, "unique-job-1");

        var job2Payload = new
        {
            name = "unique-job-2",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = gitCredId,
            slaveId,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "devops",
            tasks = new[] { new { label = "task1" } }
        };
        var create2Resp = await client.PostAsJsonAsync("/api/jobs", job2Payload);
        Assert.Equal(HttpStatusCode.Created, create2Resp.StatusCode);
        var job2Id = (await create2Resp.ReadJsonAsync()).GetProperty("id").GetInt64();

        // Try creating job with existing name "unique-job-1" -> 409 Conflict
        var dupCreate = new
        {
            name = "unique-job-1",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = gitCredId,
            slaveId,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "devops",
            tasks = new[] { new { label = "task1" } }
        };
        var postResp = await client.PostAsJsonAsync("/api/jobs", dupCreate);
        Assert.Equal(HttpStatusCode.Conflict, postResp.StatusCode);

        // Try updating job 2 to have the name of job 1 -> 409 Conflict
        var dupUpdate = new
        {
            name = "unique-job-1",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = gitCredId,
            slaveId,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "devops",
            tasks = new[] { new { label = "task1" } }
        };
        var putResp = await client.PutAsJsonAsync($"/api/jobs/{job2Id}", dupUpdate);
        Assert.Equal(HttpStatusCode.Conflict, putResp.StatusCode);
    }

    [Fact]
    public async Task Validate_and_preview_do_not_persist_and_preview_generates_yaml()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = "devops", kind = "namespace", name = "devops" });
        await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = "devops", kind = "secret", name = "super-secret" });

        var payload = new
        {
            name = "preview-job",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            imageName = "migrator",
            registryHost = "reg.local",
            @namespace = "devops",
            tasks = new[]
            {
                new
                {
                    label = "migration-task",
                    args = new[] { "migrate", "up" },
                    env = new[]
                    {
                        new { name = "DB_PASS", secretName = "super-secret", secretKey = "password" }
                    }
                }
            }
        };

        // 1. POST /api/jobs/preview
        var previewResp = await client.PostAsJsonAsync("/api/jobs/preview", payload);
        Assert.Equal(HttpStatusCode.OK, previewResp.StatusCode);
        var preview = await previewResp.ReadJsonAsync();
        var errors = preview.GetProperty("errors").EnumerateObject().ToList();
        Assert.Empty(errors);

        var previewTasks = preview.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Single(previewTasks);
        Assert.Equal(0, previewTasks[0].GetProperty("position").GetInt32());
        Assert.Equal("migration-task", previewTasks[0].GetProperty("label").GetString());
        var yaml = previewTasks[0].GetProperty("yaml").GetString();
        Assert.NotNull(yaml);
        Assert.Contains("runAsNonRoot: true", yaml);
        Assert.Contains("preview-job-preview-0", yaml);
        Assert.Contains("reg.local/migrator:tag-preview", yaml);

        // Ensure YAML does NOT contain secret values
        Assert.DoesNotContain(ApiFactory.GitToken, yaml);
        Assert.DoesNotContain(ApiFactory.RegistryPassword, yaml);

        // 2. Validate with error produces empty tasks in preview
        var invalidPayload = new
        {
            name = "preview-job",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            imageName = "migrator",
            registryHost = "reg.local",
            @namespace = "devops",
            tasks = new[]
            {
                new
                {
                    label = "migration-task",
                    args = new[] { "bad\narg" }
                }
            }
        };
        var badPreviewResp = await client.PostAsJsonAsync("/api/jobs/preview", invalidPayload);
        Assert.Equal(HttpStatusCode.OK, badPreviewResp.StatusCode);
        var badPreview = await badPreviewResp.ReadJsonAsync();
        Assert.NotEmpty(badPreview.GetProperty("errors").EnumerateObject().ToList());
        Assert.Empty(badPreview.GetProperty("tasks").EnumerateArray().ToList());

        // 3. Confirm nothing was saved to DB
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.False(await db.Jobs.AnyAsync(j => j.Name == "preview-job"));
    }

    [Fact]
    public async Task Trigger_job_run_end_to_end_with_fake_and_conflict_handling()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        var (jobId, _, _, _) = await CreateTestPrerequisitesAsync(client, "runnable-job");

        // 1. Non-existent job trigger returns 404
        var nonExistentResp = await client.PostAsync("/api/jobs/999999/runs", null);
        Assert.Equal(HttpStatusCode.NotFound, nonExistentResp.StatusCode);

        // 2. Trigger real run -> 202
        var triggerResp = await client.PostAsync($"/api/jobs/{jobId}/runs", null);
        Assert.Equal(HttpStatusCode.Accepted, triggerResp.StatusCode);
        var runJson = await triggerResp.ReadJsonAsync();
        var runId = runJson.GetProperty("id").GetInt64();
        Assert.Equal("job", runJson.GetProperty("kind").GetString());
        Assert.Equal(jobId, runJson.GetProperty("jobId").GetInt64());

        // 3. Poll until success
        var finishedRun = await client.WaitForRunAsync(runId, "success", "failed");
        Assert.Equal("success", finishedRun.GetProperty("status").GetString());
        Assert.Equal("job", finishedRun.GetProperty("kind").GetString());
        Assert.Equal(jobId, finishedRun.GetProperty("jobId").GetInt64());

        // Tasks array in RunDetailDto
        var tasks = finishedRun.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(2, tasks.Count);
        Assert.Equal("task-zero", tasks[0].GetProperty("label").GetString());
        Assert.Equal("success", tasks[0].GetProperty("status").GetString());
        Assert.Equal("task-one", tasks[1].GetProperty("label").GetString());
        Assert.Equal("success", tasks[1].GetProperty("status").GetString());

        // 4. Log lines have taskPosition
        var logsResp = await client.GetAsync($"/api/runs/{runId}/logs");
        Assert.Equal(HttpStatusCode.OK, logsResp.StatusCode);
        var logs = await logsResp.ReadJsonAsync();
        var logLines = logs.EnumerateArray().ToList();
        Assert.NotEmpty(logLines);
        Assert.Contains(logLines, l => l.GetProperty("step").GetString() == "runjob" && l.TryGetProperty("taskPosition", out var tp) && tp.ValueKind != JsonValueKind.Null);

        // 5. Test 409 conflict when triggering while another run is active
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Runs.Add(new Run
            {
                JobId = jobId,
                Status = RunStatus.Running,
                Tag = "tag1",
                Image = "img1"
            });
            await db.SaveChangesAsync();
        }

        var conflictResp = await client.PostAsync($"/api/jobs/{jobId}/runs", null);
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);
    }

    [Fact]
    public async Task Response_bodies_never_contain_credential_or_secret_values()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        var (jobId, _, _, _) = await CreateTestPrerequisitesAsync(client, "secret-leak-test");

        // Verify GET /api/jobs/{id}
        var jobResp = await client.GetAsync($"/api/jobs/{jobId}");
        var jobBody = await jobResp.Content.ReadAsStringAsync();
        foreach (var secret in ApiFactory.Secrets)
        {
            Assert.DoesNotContain(secret, jobBody);
        }

        // Verify GET /api/jobs
        var listResp = await client.GetAsync("/api/jobs");
        var listBody = await listResp.Content.ReadAsStringAsync();
        foreach (var secret in ApiFactory.Secrets)
        {
            Assert.DoesNotContain(secret, listBody);
        }

        // Verify GET /api/job-allow-entries
        var allowResp = await client.GetAsync("/api/job-allow-entries");
        var allowBody = await allowResp.Content.ReadAsStringAsync();
        foreach (var secret in ApiFactory.Secrets)
        {
            Assert.DoesNotContain(secret, allowBody);
        }
    }

    [Fact]
    public async Task Allowlist_crud_and_in_use_checks()
    {
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            [Shipit.Core.ShipitOptions.JobAllowListKey] = ""
        });
        var client = await factory.LoggedInClientAsync();

        // 1. Create allow entries
        var createNs = await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = "prod", kind = "namespace" });
        Assert.Equal(HttpStatusCode.Created, createNs.StatusCode);
        var nsEntry = await createNs.ReadJsonAsync();
        Assert.Equal("prod", nsEntry.GetProperty("namespace").GetString());
        Assert.Equal("prod", nsEntry.GetProperty("name").GetString()); // normalized name = namespace
        var nsId = nsEntry.GetProperty("id").GetInt64();

        var createSec = await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = "prod", kind = "secret", name = "db-secret" });
        Assert.Equal(HttpStatusCode.Created, createSec.StatusCode);
        var secEntry = await createSec.ReadJsonAsync();
        var secId = secEntry.GetProperty("id").GetInt64();

        // 2. Duplicate entry -> 409
        var dupSec = await client.PostAsJsonAsync("/api/job-allow-entries", new { @namespace = "prod", kind = "secret", name = "db-secret" });
        Assert.Equal(HttpStatusCode.Conflict, dupSec.StatusCode);

        // 3. List allow entries (ordered by namespace, kind, name)
        var listResp = await client.GetAsync("/api/job-allow-entries");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var entries = (await listResp.ReadJsonAsync()).EnumerateArray().ToList();
        Assert.True(entries.Count >= 2);

        // 4. Create a job using "prod" namespace and "db-secret"
        var seeded = await client.SeedAsync();
        var jobPayload = new
        {
            name = "allow-test-job",
            gitUrl = "https://github.com/example/repo.git",
            gitCredentialId = seeded.GitCred,
            slaveId = seeded.Slave,
            imageName = "my-job",
            registryHost = "registry.example.com",
            @namespace = "prod",
            tasks = new[]
            {
                new
                {
                    label = "task1",
                    envFromSecrets = new[] { "db-secret" }
                }
            }
        };
        var jobCreateResp = await client.PostAsJsonAsync("/api/jobs", jobPayload);
        Assert.Equal(HttpStatusCode.Created, jobCreateResp.StatusCode);
        var jobJson = await jobCreateResp.ReadJsonAsync();
        var jobId = jobJson.GetProperty("id").GetInt64();

        // 5. Try deleting namespace entry -> 409 because it's in use by the job
        var delNsResp = await client.DeleteAsync($"/api/job-allow-entries/{nsId}");
        Assert.Equal(HttpStatusCode.Conflict, delNsResp.StatusCode);

        // 6. Try deleting secret entry -> 409 because task references it
        var delSecResp = await client.DeleteAsync($"/api/job-allow-entries/{secId}");
        Assert.Equal(HttpStatusCode.Conflict, delSecResp.StatusCode);

        // 7. Delete the job first, then deleting allow entries should succeed (204)
        var delJobResp = await client.DeleteAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.NoContent, delJobResp.StatusCode);

        var delSecOk = await client.DeleteAsync($"/api/job-allow-entries/{secId}");
        Assert.Equal(HttpStatusCode.NoContent, delSecOk.StatusCode);

        var delNsOk = await client.DeleteAsync($"/api/job-allow-entries/{nsId}");
        Assert.Equal(HttpStatusCode.NoContent, delNsOk.StatusCode);

        // 8. Delete non-existent entry -> 404
        var del404 = await client.DeleteAsync("/api/job-allow-entries/999999");
        Assert.Equal(HttpStatusCode.NotFound, del404.StatusCode);
    }

    [Fact]
    public async Task JobAllowList_seed_lifecycle_and_idempotency()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"shipit-job-seed-test-{Guid.NewGuid():N}.db");
        try
        {
            var seedJson = "[{\"namespace\":\"seed-ns\",\"kind\":\"namespace\",\"name\":\"seed-ns\"},{\"namespace\":\"seed-ns\",\"kind\":\"configmap\",\"name\":\"seed-cm\"}]";
            using (var factory1 = new ApiFactory(new Dictionary<string, string>
            {
                [ShipitHost.SqlitePathKey] = dbPath,
                ["Shipit:JobAllowList"] = seedJson
            }))
            {
                var client = await factory1.LoggedInClientAsync();
                var listResp = await client.GetAsync("/api/job-allow-entries");
                Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
                var list = await listResp.ReadJsonAsync();
                Assert.Equal(2, list.GetArrayLength());
                Assert.Contains(list.EnumerateArray(), t => t.GetProperty("namespace").GetString() == "seed-ns" && t.GetProperty("kind").GetString() == "namespace");
                Assert.Contains(list.EnumerateArray(), t => t.GetProperty("namespace").GetString() == "seed-ns" && t.GetProperty("kind").GetString() == "configmap");
            }

            using (var factory2 = new ApiFactory(new Dictionary<string, string>
            {
                [ShipitHost.SqlitePathKey] = dbPath,
                ["Shipit:JobAllowList"] = "[{\"namespace\":\"new-ns\",\"kind\":\"namespace\",\"name\":\"new-ns\"}]"
            }))
            {
                var client = await factory2.LoggedInClientAsync();
                var listResp = await client.GetAsync("/api/job-allow-entries");
                var list = await listResp.ReadJsonAsync();
                Assert.Equal(2, list.GetArrayLength());
                Assert.DoesNotContain(list.EnumerateArray(), t => t.GetProperty("namespace").GetString() == "new-ns");
            }
        }
        finally
        {
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}

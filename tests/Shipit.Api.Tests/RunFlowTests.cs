using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api.Tests;

public class RunFlowTests
{
    [Fact]
    public async Task A_triggered_run_succeeds_end_to_end_with_fakes_and_leaks_no_secret()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        var response = await client.PostAsync($"/api/pipelines/{seeded.Pipeline}/runs", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.ReadJsonAsync();
        Assert.Contains(accepted.GetProperty("status").GetString(), new[] { "queued", "running", "success" });
        var runId = accepted.GetProperty("id").GetInt64();

        var run = await client.WaitForRunAsync(runId, "success", "failed");
        Assert.Equal("success", run.GetProperty("status").GetString());
        Assert.Equal("abc1234", run.GetProperty("tag").GetString());
        Assert.Equal("registry.example.com/shipit-demo:abc1234", run.GetProperty("image").GetString());
        var steps = run.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["checkout", "build", "push", "deploy"], steps.Select(s => s.GetProperty("name").GetString()));
        Assert.All(steps, s => Assert.Equal("success", s.GetProperty("status").GetString()));

        // JSON log: ordered, not empty, secret free.
        var logs = await (await client.GetAsync($"/api/runs/{runId}/logs")).ReadJsonAsync();
        var lines = logs.EnumerateArray().ToList();
        Assert.NotEmpty(lines);
        var seqs = lines.Select(l => l.GetProperty("seq").GetInt64()).ToList();
        Assert.Equal(seqs.OrderBy(x => x), seqs);
        Assert.Contains(lines, l => l.GetProperty("step").GetString() == "deploy");
        foreach (var secret in ApiFactory.Secrets)
        {
            Assert.DoesNotContain(secret, logs.GetRawText());
        }

        // `after` resumes after a given sequence number.
        var resumed = await (await client.GetAsync($"/api/runs/{runId}/logs?after={seqs[^3]}")).ReadJsonAsync();
        Assert.Equal(seqs.TakeLast(2), resumed.EnumerateArray().Select(l => l.GetProperty("seq").GetInt64()));

        // The pipeline list shows the last run, and the run list contains it.
        var pipelines = await (await client.GetAsync("/api/pipelines")).ReadJsonAsync();
        Assert.Equal(runId, pipelines[0].GetProperty("lastRun").GetProperty("id").GetInt64());
        var runs = await (await client.GetAsync($"/api/runs?pipelineId={seeded.Pipeline}&limit=5")).ReadJsonAsync();
        Assert.Contains(runs.EnumerateArray(), r => r.GetProperty("id").GetInt64() == runId);
    }

    [Fact]
    public async Task Server_sent_events_stream_every_line_then_end()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();
        var runId = await client.TriggerAsync(seeded.Pipeline);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/logs");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(cts.Token); // the server closes the stream after `end`

        var events = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("event: end", events[^1]);
        Assert.Contains("\"status\":\"success\"", events[^1]);
        var logEvents = events.Where(e => e.Contains("event: log")).ToList();
        Assert.NotEmpty(logEvents);

        // Same lines as the JSON endpoint, in the same order, and nothing arrives after `end`.
        var stored = await (await client.GetAsync($"/api/runs/{runId}/logs")).ReadJsonAsync();
        Assert.Equal(stored.GetArrayLength(), logEvents.Count);
        var streamedIds = logEvents.Select(e => long.Parse(e.Split('\n').First(l => l.StartsWith("id: ")).Substring(4))).ToList();
        Assert.Equal(stored.EnumerateArray().Select(l => l.GetProperty("seq").GetInt64()), streamedIds);
        foreach (var secret in ApiFactory.Secrets)
        {
            Assert.DoesNotContain(secret, body);
        }
    }

    [Fact]
    public async Task Last_event_id_resumes_a_stream()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();
        var runId = await client.TriggerAsync(seeded.Pipeline);
        await client.WaitForRunAsync(runId, "success");
        var stored = (await (await client.GetAsync($"/api/runs/{runId}/logs")).ReadJsonAsync()).EnumerateArray().ToList();
        var resumeFrom = stored[2].GetProperty("seq").GetInt64();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/logs");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("Last-Event-ID", resumeFrom.ToString());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);

        var firstId = long.Parse(body.Split('\n').First(l => l.StartsWith("id: ")).Substring(4));
        Assert.Equal(stored[3].GetProperty("seq").GetInt64(), firstId);
        Assert.Contains("event: end", body);
    }

    [Fact]
    public async Task A_second_run_is_rejected_while_one_is_active()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        // An active run (as if started a moment ago); the pipeline must refuse another and cannot be deleted.
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContextAsync())
        {
            db.Runs.Add(new Run { PipelineId = seeded.Pipeline, Status = RunStatus.Running, StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var conflict = await client.PostAsync($"/api/pipelines/{seeded.Pipeline}/runs", null);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("Pipeline already has an active run", (await conflict.ReadJsonAsync()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/pipelines/{seeded.Pipeline}")).StatusCode);
    }

    [Fact]
    public async Task Missing_resources_return_404()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/pipelines/9999/runs", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/runs/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/runs/9999/logs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/pipelines/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/slaves/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/runs/9999/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Cancel_requires_authentication()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/runs/1/cancel", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_queued_run_returns_202_and_subsequent_cancel_returns_409()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();

        long runId;
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<ShipitDbContext>>().CreateDbContextAsync())
        {
            var r = new Run
            {
                PipelineId = seeded.Pipeline,
                Status = RunStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                Steps = Enum.GetValues<StepName>().Select(n => new RunStep { Name = n, Status = StepStatus.Pending }).ToList(),
            };
            db.Runs.Add(r);
            await db.SaveChangesAsync();
            runId = r.Id;
        }

        var cancelResponse = await client.PostAsync($"/api/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.Accepted, cancelResponse.StatusCode);
        var body = await cancelResponse.ReadJsonAsync();
        Assert.Equal("cancelled", body.GetProperty("status").GetString());
        Assert.Equal("cancelled by user", body.GetProperty("failureReason").GetString());
        Assert.NotNull(body.GetProperty("finishedAt").GetString());

        var conflictResponse = await client.PostAsync($"/api/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
    }

    [Fact]
    public async Task Cancel_running_run_returns_202_is_idempotent_while_stopping_and_returns_409_when_done()
    {
        using var factory = new ApiFactory();
        var ssh = (Shipit.Ssh.FakeSshExecutor)factory.Services.GetRequiredService<Shipit.Core.ISshExecutor>();
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ssh.AddRule(cmd => cmd.CommandText.Contains("docker build"), exitCode: 0, delay: TimeSpan.FromSeconds(30));
        ssh.CommandExecuting += cmd =>
        {
            if (cmd.CommandText.Contains("docker build"))
            {
                buildStarted.TrySetResult();
            }
        };

        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();
        var runId = await client.TriggerAsync(seeded.Pipeline);

        await buildStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var firstCancel = await client.PostAsync($"/api/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.Accepted, firstCancel.StatusCode);

        var secondCancel = await client.PostAsync($"/api/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.Accepted, secondCancel.StatusCode);

        var run = await client.WaitForRunAsync(runId, "cancelled");
        Assert.Equal("cancelled", run.GetProperty("status").GetString());
        Assert.Equal("cancelled by user", run.GetProperty("failureReason").GetString());

        var thirdCancel = await client.PostAsync($"/api/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, thirdCancel.StatusCode);

        var nextRunId = await client.TriggerAsync(seeded.Pipeline);
        Assert.NotEqual(runId, nextRunId);
    }

    [Fact]
    public async Task Cancel_finished_run_returns_409()
    {
        using var factory = new ApiFactory();
        var client = await factory.LoggedInClientAsync();
        var seeded = await client.SeedAsync();
        var runId = await client.TriggerAsync(seeded.Pipeline);
        await client.WaitForRunAsync(runId, "success");

        var response = await client.PostAsync($"/api/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}

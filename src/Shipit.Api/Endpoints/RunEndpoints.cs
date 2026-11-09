using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shipit.Data;
using Shipit.Data.Entities;
using Shipit.Engine;

namespace Shipit.Api;

internal static class RunEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private const int PageSize = 500;

    public static void MapRuns(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pipelines/{id:long}/runs", async (long id, IRunCoordinator coordinator, CancellationToken ct) =>
        {
            try
            {
                var run = await coordinator.TriggerAsync(id, ct);
                return Results.Json(run.ToDto(), statusCode: StatusCodes.Status202Accepted);
            }
            catch (PipelineNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RunConflictException)
            {
                return Problems.Conflict("Pipeline already has an active run", "Wait for the current run to finish.");
            }
        });

        app.MapGet("/api/runs", async (long? pipelineId, int? limit, ShipitDbContext db, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 50, 1, 200);
            var query = db.Runs.AsNoTracking();
            if (pipelineId is not null)
            {
                query = query.Where(r => r.PipelineId == pipelineId);
            }

            var runs = await query.OrderByDescending(r => r.Id).Take(take).ToListAsync(ct);
            return Results.Ok(runs.Select(r => r.ToDto()));
        });

        app.MapGet("/api/runs/{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
        {
            var run = await db.Runs.AsNoTracking().Include(r => r.Steps).FirstOrDefaultAsync(r => r.Id == id, ct);
            return run is null ? Results.NotFound() : Results.Ok(run.ToDetailDto());
        });

        app.MapGet("/api/runs/{id:long}/logs", async (long id, long? after, HttpContext http, IDbContextFactory<ShipitDbContext> dbFactory) =>
        {
            var ct = http.RequestAborted;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                if (!await db.Runs.AnyAsync(r => r.Id == id, ct))
                {
                    return Results.NotFound();
                }
            }

            var cursor = after ?? LastEventId(http) ?? 0;
            if (!http.Request.Headers.Accept.Any(a => a is not null && a.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase)))
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var lines = await db.RunLogs.AsNoTracking()
                    .Where(l => l.RunId == id && l.Id > cursor)
                    .OrderBy(l => l.Id)
                    .ToListAsync(ct);
                return Results.Ok(lines.Select(l => l.ToDto()));
            }

            await StreamAsync(http, dbFactory, id, cursor);
            return Results.Empty;
        });
    }

    private static long? LastEventId(HttpContext http) =>
        long.TryParse(http.Request.Headers["Last-Event-ID"].FirstOrDefault(), out var value) ? value : null;

    /// <summary>
    /// Server-Sent Events: one <c>log</c> event per stored line, then one <c>end</c> event once the run is final.
    /// The engine makes a run final only after its last line is stored, so "final status, then an empty read" is the end.
    /// </summary>
    private static async Task StreamAsync(HttpContext http, IDbContextFactory<ShipitDbContext> dbFactory, long runId, long cursor)
    {
        var ct = http.RequestAborted;
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no"; // keep proxies from buffering the stream
        await http.Response.Body.FlushAsync(ct);

        var lastWrite = DateTime.UtcNow;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                RunStatus status;
                List<RunLog> lines;
                await using (var db = await dbFactory.CreateDbContextAsync(ct))
                {
                    // Read the status first: if it is final, every line is already stored and the read below sees them all.
                    status = await db.Runs.AsNoTracking().Where(r => r.Id == runId).Select(r => r.Status).FirstAsync(ct);
                    lines = await db.RunLogs.AsNoTracking()
                        .Where(l => l.RunId == runId && l.Id > cursor)
                        .OrderBy(l => l.Id)
                        .Take(PageSize)
                        .ToListAsync(ct);
                }

                foreach (var line in lines)
                {
                    await WriteEventAsync(http, "log", JsonSerializer.Serialize(line.ToDto(), Json), line.Id, ct);
                    cursor = line.Id;
                }

                if (lines.Count > 0)
                {
                    lastWrite = DateTime.UtcNow;
                    if (lines.Count == PageSize)
                    {
                        continue; // more waiting
                    }
                }

                if (lines.Count == 0 && status is RunStatus.Success or RunStatus.Failed)
                {
                    await WriteEventAsync(http, "end", JsonSerializer.Serialize(new { status = Wire.Of(status) }, Json), null, ct);
                    return;
                }

                if (DateTime.UtcNow - lastWrite >= Heartbeat)
                {
                    await http.Response.WriteAsync(": ping\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                    lastWrite = DateTime.UtcNow;
                }

                await Task.Delay(PollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client went away.
        }
    }

    private static async Task WriteEventAsync(HttpContext http, string name, string data, long? id, CancellationToken ct)
    {
        var text = (id is null ? string.Empty : $"id: {id}\n") + $"event: {name}\ndata: {data}\n\n";
        await http.Response.WriteAsync(text, ct);
        await http.Response.Body.FlushAsync(ct);
    }
}

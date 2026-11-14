namespace Shipit.Api;

/// <summary>
/// Job endpoints. The routes exist so the contract and its conformance tests hold from the start; the handlers
/// answer 501 until task J5 (job-api) implements them (docs/plan.md section 9).
/// </summary>
internal static class JobEndpoints
{
    private const string Pending = "Implemented in task J5 (job-api).";

    public static void MapJobs(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api/jobs");
        jobs.MapGet("", () => Problems.NotImplemented(Pending));
        jobs.MapPost("", () => Problems.NotImplemented(Pending));
        jobs.MapPost("validate", () => Problems.NotImplemented(Pending));
        jobs.MapPost("preview", () => Problems.NotImplemented(Pending));
        jobs.MapGet("{id:long}", (long id) => Problems.NotImplemented(Pending));
        jobs.MapPut("{id:long}", (long id) => Problems.NotImplemented(Pending));
        jobs.MapDelete("{id:long}", (long id) => Problems.NotImplemented(Pending));
        jobs.MapPost("{id:long}/runs", (long id) => Problems.NotImplemented(Pending));

        var allow = app.MapGroup("/api/job-allow-entries");
        allow.MapGet("", () => Problems.NotImplemented(Pending));
        allow.MapPost("", () => Problems.NotImplemented(Pending));
        allow.MapDelete("{id:long}", (long id) => Problems.NotImplemented(Pending));
    }
}

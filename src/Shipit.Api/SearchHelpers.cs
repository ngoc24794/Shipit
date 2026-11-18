using Microsoft.EntityFrameworkCore;
using Shipit.Data.Entities;

namespace Shipit.Api;

/// <summary>Shared LIKE-escaping and run-query helpers for /api/runs and /api/search.</summary>
internal static class SearchHelpers
{
    public const string LikeEscape = "\\";

    public static string ToLikePattern(string query)
    {
        var escaped = query.Trim()
            .ToLowerInvariant()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    public static IQueryable<Run> FilterRunsByQuery(IQueryable<Run> query, string rawQuery)
    {
        var trimmed = rawQuery.Trim();
        var pattern = ToLikePattern(trimmed);
        var hasRunId = long.TryParse(trimmed.TrimStart('#'), out var runId);

        return query.Where(r =>
            (hasRunId && r.Id == runId)
            || (r.Pipeline != null && EF.Functions.Like(r.Pipeline.Name.ToLower(), pattern, LikeEscape))
            || (r.Job != null && EF.Functions.Like(r.Job.Name.ToLower(), pattern, LikeEscape))
            || (r.Image != null && EF.Functions.Like(r.Image.ToLower(), pattern, LikeEscape))
            || (r.Tag != null && EF.Functions.Like(r.Tag.ToLower(), pattern, LikeEscape)));
    }
}

namespace Shipit.Api;

/// <summary>Collects field errors and turns them into a 400 problem response.</summary>
internal sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(message);
    }

    public IResult ToResult() => Problems.BadRequest(
        "Validation failed",
        null,
        _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
}

internal static class Problems
{
    public static IResult BadRequest(string title, string? detail = null, IReadOnlyDictionary<string, string[]>? errors = null) =>
        Results.Json(new ProblemDto(title, detail, errors), statusCode: StatusCodes.Status400BadRequest);

    public static IResult Conflict(string title, string? detail = null) =>
        Results.Json(new ProblemDto(title, detail), statusCode: StatusCodes.Status409Conflict);
}

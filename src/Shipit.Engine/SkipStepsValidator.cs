using Shipit.Data.Entities;

namespace Shipit.Engine;

/// <summary>
/// Validates and parses <c>skipSteps</c> for pipeline and job runs.
/// Skipped steps must be valid wire names, contain no duplicates, never include <c>checkout</c>,
/// and form a trailing suffix of the run's step order.
/// </summary>
public static class SkipStepsValidator
{
    public static readonly StepName[] PipelineStepOrder = [StepName.Checkout, StepName.Build, StepName.Push, StepName.Deploy];
    public static readonly StepName[] JobStepOrder = [StepName.Checkout, StepName.Build, StepName.Push, StepName.RunJob];

    public static bool TryParse(
        IReadOnlyList<string>? skipSteps,
        bool isJob,
        out IReadOnlySet<StepName> parsed,
        out string? error)
    {
        if (skipSteps is null || skipSteps.Count == 0)
        {
            parsed = new HashSet<StepName>();
            error = null;
            return true;
        }

        var order = isJob ? JobStepOrder : PipelineStepOrder;
        var byWire = order.ToDictionary(s => s.ToString().ToLowerInvariant(), s => s, StringComparer.Ordinal);
        var validList = string.Join(", ", order.Select(s => s.ToString().ToLowerInvariant()));

        var set = new HashSet<StepName>();
        foreach (var raw in skipSteps)
        {
            var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (!byWire.TryGetValue(normalized, out var step))
            {
                parsed = new HashSet<StepName>();
                error = $"Unknown step name '{raw}'. Valid steps are: {validList}.";
                return false;
            }

            if (!set.Add(step))
            {
                parsed = new HashSet<StepName>();
                error = $"Duplicate step name '{normalized}' in skipSteps.";
                return false;
            }
        }

        if (!TryValidateSet(set, order, out error))
        {
            parsed = new HashSet<StepName>();
            return false;
        }

        parsed = set;
        error = null;
        return true;
    }

    public static void ValidateOrThrow(IReadOnlySet<StepName>? skipSteps, bool isJob)
    {
        if (skipSteps is null || skipSteps.Count == 0)
        {
            return;
        }

        var order = isJob ? JobStepOrder : PipelineStepOrder;
        foreach (var step in skipSteps)
        {
            if (!order.Contains(step))
            {
                throw new ArgumentException($"Step '{step}' is not valid for this run type.", nameof(skipSteps));
            }
        }

        if (!TryValidateSet(skipSteps, order, out var error))
        {
            throw new ArgumentException(error, nameof(skipSteps));
        }
    }

    private static bool TryValidateSet(IReadOnlySet<StepName> set, StepName[] order, out string? error)
    {
        if (set.Count == 0)
        {
            error = null;
            return true;
        }

        if (set.Contains(StepName.Checkout))
        {
            error = "The 'checkout' step is required and cannot be skipped.";
            return false;
        }

        var minSkippedIndex = order.Length;
        for (var i = 0; i < order.Length; i++)
        {
            if (set.Contains(order[i]))
            {
                minSkippedIndex = i;
                break;
            }
        }

        for (var i = minSkippedIndex; i < order.Length; i++)
        {
            if (!set.Contains(order[i]))
            {
                var trailing = string.Join(", ", order.Skip(minSkippedIndex).Select(s => s.ToString().ToLowerInvariant()));
                error = $"skipSteps must form a trailing suffix of the step order (expected '{order[i].ToString().ToLowerInvariant()}' to also be skipped in [{trailing}]).";
                return false;
            }
        }

        error = null;
        return true;
    }
}

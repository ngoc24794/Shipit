using System.Text;

namespace Shipit.Security;

/// <summary>
/// Replaces sensitive values in log lines with '***'.
/// Ignores secrets shorter than 4 characters.
/// Splits multi-line secrets into per-line targets.
/// Automatically adds base64-encoded equivalents for single-line secrets under 200 characters.
/// Thread-safe for concurrent read and write operations.
/// </summary>
public sealed class LogMasker
{
    private const string MaskReplacement = "***";
    private const int MinSecretLength = 4;
    private const int MaxSingleLineLengthForBase64 = 200;

    private readonly object _gate = new();
    private readonly HashSet<string> _rawTargets = new(StringComparer.Ordinal);
    private volatile string[] _cachedTargets = [];

    public LogMasker(IEnumerable<string>? secrets = null)
    {
        if (secrets != null)
        {
            foreach (var s in secrets)
            {
                AddInternal(s);
            }
            RebuildCachedTargets();
        }
    }

    /// <summary>Adds a secret to be masked.</summary>
    public void Add(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        lock (_gate)
        {
            if (AddInternal(secret))
            {
                RebuildCachedTargets();
            }
        }
    }

    private bool AddInternal(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var changed = false;

        // If multi-line, split into lines and mask each line individually
        if (secret.Contains('\n') || secret.Contains('\r'))
        {
            var lines = secret.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.Trim();
                if (trimmed.Length >= MinSecretLength && trimmed != MaskReplacement)
                {
                    if (_rawTargets.Add(trimmed))
                    {
                        changed = true;
                    }
                }
            }
        }
        else
        {
            // Single-line secret
            var trimmed = secret.Trim();
            if (trimmed.Length >= MinSecretLength && trimmed != MaskReplacement)
            {
                if (_rawTargets.Add(trimmed))
                {
                    changed = true;
                }
            }

            // Also mask base64 representation if under 200 characters
            if (trimmed.Length < MaxSingleLineLengthForBase64 && trimmed.Length >= MinSecretLength)
            {
                try
                {
                    var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(trimmed));
                    if (b64.Length >= MinSecretLength && b64 != MaskReplacement)
                    {
                        if (_rawTargets.Add(b64))
                        {
                            changed = true;
                        }
                    }
                }
                catch
                {
                    // Ignore conversion failures if any
                }
            }
        }

        return changed;
    }

    private void RebuildCachedTargets()
    {
        // Sort descending by length so longer targets match and mask first
        _cachedTargets = _rawTargets
            .OrderByDescending(t => t.Length)
            .ThenBy(t => t, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Masks occurrences of known secrets in the provided line.</summary>
    public string Mask(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line ?? string.Empty;
        }

        var targets = _cachedTargets;
        if (targets.Length == 0)
        {
            return line;
        }

        var result = line;
        foreach (var target in targets)
        {
            if (result.Contains(target, StringComparison.Ordinal))
            {
                result = result.Replace(target, MaskReplacement, StringComparison.Ordinal);
            }
        }

        return result;
    }
}

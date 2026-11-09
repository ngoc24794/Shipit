using Npgsql;

namespace Shipit.Data;

/// <summary>
/// Converts database URLs (postgres://, postgresql://) or connection strings
/// into valid Npgsql connection strings.
/// </summary>
public static class DbUrlParser
{
    public static string ToNpgsqlConnectionString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Database connection string or URL cannot be null or empty.", nameof(input));
        }

        var trimmed = input.Trim();

        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return ParsePostgresUrl(trimmed);
        }

        return ValidateExistingConnectionString(trimmed);
    }

    private static string ParsePostgresUrl(string url)
    {
        Uri uri;
        try
        {
            uri = new Uri(url);
        }
        catch (Exception)
        {
            throw new ArgumentException("Invalid database URL format.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Database URL must specify a valid host.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
        };

        var dbName = uri.AbsolutePath.TrimStart('/');
        if (!string.IsNullOrEmpty(dbName))
        {
            builder.Database = Uri.UnescapeDataString(dbName);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var colonIdx = uri.UserInfo.IndexOf(':');
            if (colonIdx >= 0)
            {
                builder.Username = Uri.UnescapeDataString(uri.UserInfo[..colonIdx]);
                builder.Password = Uri.UnescapeDataString(uri.UserInfo[(colonIdx + 1)..]);
            }
            else
            {
                builder.Username = Uri.UnescapeDataString(uri.UserInfo);
            }
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            var queryPairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in queryPairs)
            {
                var eqIdx = pair.IndexOf('=');
                var key = eqIdx >= 0 ? Uri.UnescapeDataString(pair[..eqIdx]).Trim() : Uri.UnescapeDataString(pair).Trim();
                var val = eqIdx >= 0 ? Uri.UnescapeDataString(pair[(eqIdx + 1)..]).Trim() : string.Empty;

                if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("ssl mode", StringComparison.OrdinalIgnoreCase))
                {
                    builder["SSL Mode"] = MapSslMode(val);
                }
                else
                {
                    try
                    {
                        builder[key] = val;
                    }
                    catch (Exception)
                    {
                        throw new ArgumentException("Invalid connection string parameter in database URL.");
                    }
                }
            }
        }

        return builder.ConnectionString;
    }

    private static string MapSslMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "disable" => "Disable",
            "allow" => "Allow",
            "prefer" => "Prefer",
            "require" => "Require",
            "verify-ca" => "VerifyCA",
            "verify-full" => "VerifyFull",
            _ => value
        };
    }

    private static string ValidateExistingConnectionString(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.Host) && string.IsNullOrWhiteSpace(builder.Database))
            {
                throw new ArgumentException("Invalid database connection string.");
            }
            return connectionString;
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Invalid database connection string or URL format.");
        }
        catch (Exception)
        {
            throw new ArgumentException("Invalid database connection string or URL format.");
        }
    }
}

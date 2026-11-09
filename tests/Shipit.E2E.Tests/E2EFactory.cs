using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Shipit.E2E.Tests;

/// <summary>
/// WebApplicationFactory dedicated to end-to-end testing in Shipit:UseFakes mode.
/// Uses an isolated throw-away SQLite database for each test instance.
/// </summary>
public class E2EFactory : WebApplicationFactory<Program>
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "test-e2e-admin-password!";
    public const string SshKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nMIIEowIBAAKCAQEA0FakeE2EKeyPart1abcdef\n0FakeE2EKeyPart2ghijklmnopq\n-----END OPENSSH PRIVATE KEY-----";
    public const string GitToken = "ghp_FAKE_GIT_TOKEN_FOR_E2E_0123456789";
    public const string RegistryPassword = "reg_FAKE_PASS_FOR_E2E_9876543210";

    public static readonly string[] Secrets =
    [
        GitToken,
        RegistryPassword,
        "0FakeE2EKeyPart1abcdef",
        "0FakeE2EKeyPart2ghijklmnopq"
    ];

    private readonly string _dbPath;
    private readonly bool _deleteDbOnDispose;
    private readonly IReadOnlyDictionary<string, string> _extraSettings;

    public string DbPath => _dbPath;

    public E2EFactory(string? dbPath = null, bool deleteDbOnDispose = true, IReadOnlyDictionary<string, string>? extraSettings = null)
    {
        _dbPath = dbPath ?? Path.Combine(Path.GetTempPath(), $"shipit-e2e-{Guid.NewGuid():N}.db");
        _deleteDbOnDispose = deleteDbOnDispose;
        _extraSettings = extraSettings ?? new Dictionary<string, string>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Shipit:UseFakes", "true");
        builder.UseSetting("Shipit:SqlitePath", _dbPath);
        builder.UseSetting("SHIPIT_ADMIN_USER", AdminUser);
        builder.UseSetting("SHIPIT_ADMIN_PASSWORD", AdminPassword);
        builder.UseSetting("SHIPIT_DEPLOY_ALLOWLIST", "{\"devops\":[\"shipit-demo\"],\"avina-workspace\":[\"avina-catalog\"]}");

        foreach (var (k, v) in _extraSettings)
        {
            builder.UseSetting(k, v);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        if (_deleteDbOnDispose)
        {
            CleanupFiles(_dbPath);
        }
    }

    public static void CleanupFiles(string path)
    {
        foreach (var p in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                if (File.Exists(p))
                {
                    File.Delete(p);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup
            }
        }
    }
}

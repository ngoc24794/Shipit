using Shipit.Data;

namespace Shipit.Data.Tests;

public class DbUrlParserTests
{
    [Fact]
    public void ToNpgsqlConnectionString_StandardPostgresUrl_ConvertsCorrectly()
    {
        var url = "postgres://devops:secret123@db.example.com:5432/shipit_db";
        var connStr = DbUrlParser.ToNpgsqlConnectionString(url);

        Assert.Contains("Host=db.example.com", connStr);
        Assert.Contains("Port=5432", connStr);
        Assert.Contains("Database=shipit_db", connStr);
        Assert.Contains("Username=devops", connStr);
        Assert.Contains("Password=secret123", connStr);
    }

    [Fact]
    public void ToNpgsqlConnectionString_PostgresqlUrlWithoutPort_UsesDefaultPort5432()
    {
        var url = "postgresql://myuser:mypass@localhost/appdb";
        var connStr = DbUrlParser.ToNpgsqlConnectionString(url);

        Assert.Contains("Host=localhost", connStr);
        Assert.Contains("Port=5432", connStr);
        Assert.Contains("Database=appdb", connStr);
        Assert.Contains("Username=myuser", connStr);
        Assert.Contains("Password=mypass", connStr);
    }

    [Fact]
    public void ToNpgsqlConnectionString_PasswordWithSpecialChars_DecodesPercentEncoding()
    {
        // Password: p@ss:w/o#r%d
        // Encoded: p%40ss%3Aw%2Fo%23r%25d
        var rawPassword = "p@ss:w/o#r%d";
        var url = "postgres://admin:p%40ss%3Aw%2Fo%23r%25d@localhost:5432/shipit";
        var connStr = DbUrlParser.ToNpgsqlConnectionString(url);

        Assert.Contains("Username=admin", connStr);
        Assert.Contains($"Password={rawPassword}", connStr);
    }

    [Fact]
    public void ToNpgsqlConnectionString_UserWithSpecialChars_DecodesPercentEncoding()
    {
        // User: dev@ops
        // Encoded: dev%40ops
        var url = "postgres://dev%40ops:secret@localhost:5432/shipit";
        var connStr = DbUrlParser.ToNpgsqlConnectionString(url);

        Assert.Contains("Username=dev@ops", connStr);
    }

    [Theory]
    [InlineData("sslmode=require", "Require")]
    [InlineData("sslmode=disable", "Disable")]
    [InlineData("sslmode=prefer", "Prefer")]
    [InlineData("sslmode=verify-ca", "VerifyCA")]
    [InlineData("sslmode=verify-full", "VerifyFull")]
    [InlineData("ssl%20mode=require", "Require")]
    public void ToNpgsqlConnectionString_SslModeQueryParam_SetsSslMode(string queryParam, string expectedSslMode)
    {
        var url = $"postgres://u:p@localhost:5432/db?{queryParam}";
        var connStr = DbUrlParser.ToNpgsqlConnectionString(url);

        Assert.Contains($"SSL Mode={expectedSslMode}", connStr);
    }

    [Fact]
    public void ToNpgsqlConnectionString_ExistingNpgsqlConnectionString_ReturnsUntouched()
    {
        var existing = "Host=127.0.0.1;Port=5432;Database=shipit_prod;Username=svc_user;Password=super_secret;";
        var result = DbUrlParser.ToNpgsqlConnectionString(existing);

        Assert.Equal(existing, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ToNpgsqlConnectionString_NullOrEmpty_ThrowsArgumentException(string? input)
    {
        Assert.Throws<ArgumentException>(() => DbUrlParser.ToNpgsqlConnectionString(input!));
    }

    [Fact]
    public void ToNpgsqlConnectionString_InvalidUrlOrConnStr_ThrowsArgumentExceptionWithoutPasswordLeak()
    {
        var secretPassword = "ultra-classified-password-xyz";
        // Malformed URL containing password
        var malformedUrl = $"postgres://user:{secretPassword}@::invalid-host::/db";

        var ex = Assert.Throws<ArgumentException>(() => DbUrlParser.ToNpgsqlConnectionString(malformedUrl));
        Assert.DoesNotContain(secretPassword, ex.Message);
    }

    [Fact]
    public void ToNpgsqlConnectionString_MalformedInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DbUrlParser.ToNpgsqlConnectionString("not a connection string or url"));
    }
}

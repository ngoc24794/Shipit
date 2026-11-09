using System.Text;
using Shipit.Security;

namespace Shipit.Security.Tests;

public class LogMaskerTests
{
    [Fact]
    public void Mask_SingleLineSecret_MasksCorrectly()
    {
        var masker = new LogMasker(["github_pat_11AAAAAA"]);
        var line = "Clone repository with token github_pat_11AAAAAA from github.com";

        var result = masker.Mask(line);

        Assert.Equal("Clone repository with token *** from github.com", result);
    }

    [Fact]
    public void Mask_SecretsShorterThanFourChars_AreIgnored()
    {
        var masker = new LogMasker(["abc", "12", ""]);
        var line = "This abc test 12 should not be masked";

        var result = masker.Mask(line);

        Assert.Equal(line, result);
    }

    [Fact]
    public void Mask_MultiLinePemSecret_MasksEachLine()
    {
        var pem = """
            -----BEGIN RSA PRIVATE KEY-----
            MIIEowIBAAKCAQEA0w5g7l3xL4uF9V6yK1wT
            abc123def456xyz789qwe000
            -----END RSA PRIVATE KEY-----
            """;

        var masker = new LogMasker([pem]);

        // Simulating single lines logged from execution
        var line1 = "Loading key: -----BEGIN RSA PRIVATE KEY----- now";
        var line2 = "Payload: MIIEowIBAAKCAQEA0w5g7l3xL4uF9V6yK1wT";
        var line3 = "Finished -----END RSA PRIVATE KEY-----";

        Assert.Equal("Loading key: *** now", masker.Mask(line1));
        Assert.Equal("Payload: ***", masker.Mask(line2));
        Assert.Equal("Finished ***", masker.Mask(line3));
    }

    [Fact]
    public void Mask_OverlappingSecrets_PrioritizesLongerSecretFirst()
    {
        // "token_secret_12345" vs "token_secret" vs "token"
        var masker = new LogMasker(["token", "token_secret_12345", "token_secret"]);

        var line = "Found token_secret_12345 and token_secret and token here";
        var result = masker.Mask(line);

        Assert.Equal("Found *** and *** and *** here", result);
    }

    [Fact]
    public void Mask_Base64OfSingleLineSecretUnder200Chars_IsMasked()
    {
        var secret = "admin:secretpassword123";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));

        var masker = new LogMasker([secret]);

        var authHeader = $"Authorization: Basic {base64}";
        var result = masker.Mask(authHeader);

        Assert.Equal("Authorization: Basic ***", result);
    }

    [Fact]
    public void Mask_LongSecretOver200Chars_DoesNotGenerateBase64Variant()
    {
        var longSecret = new string('A', 250);
        var masker = new LogMasker([longSecret]);

        var line = $"Long secret is {longSecret}";
        Assert.Equal("Long secret is ***", masker.Mask(line));

        // Base64 of longSecret should not be automatically registered
        var base64OfLong = Convert.ToBase64String(Encoding.UTF8.GetBytes(longSecret));
        var b64Line = $"Base64 is {base64OfLong}";
        Assert.Equal(b64Line, masker.Mask(b64Line));
    }

    [Fact]
    public void Add_DynamicallyRegistersNewSecretsThreadSafely()
    {
        var masker = new LogMasker();

        Parallel.For(0, 100, i =>
        {
            masker.Add($"dynamic_secret_{i:D4}");
            var masked = masker.Mask($"Log with dynamic_secret_{i:D4} test");
            Assert.Contains("***", masked);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Mask_NullOrEmpty_ReturnsGracefully(string? input)
    {
        var masker = new LogMasker(["secret1234"]);
        var result = masker.Mask(input!);

        if (input == null)
        {
            Assert.Equal(string.Empty, result);
        }
        else
        {
            Assert.Equal("", result);
        }
    }
}

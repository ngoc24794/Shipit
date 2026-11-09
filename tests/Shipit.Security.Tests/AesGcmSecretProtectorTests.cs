using System.Security.Cryptography;
using Shipit.Security;

namespace Shipit.Security.Tests;

public class AesGcmSecretProtectorTests
{
    [Fact]
    public void GenerateKey_ReturnsValidBase64Of32Bytes()
    {
        var key = AesGcmSecretProtector.GenerateKey();
        Assert.False(string.IsNullOrWhiteSpace(key));

        var bytes = Convert.FromBase64String(key);
        Assert.Equal(32, bytes.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base-64!!!")]
    [InlineData("c2hvcnQ=")] // base64 of "short" (5 bytes)
    public void Constructor_InvalidKey_ThrowsArgumentException(string invalidKey)
    {
        Assert.Throws<ArgumentException>(() => new AesGcmSecretProtector(invalidKey));
    }

    [Fact]
    public void ProtectAndUnprotect_RoundTripsSuccessfully()
    {
        var key = AesGcmSecretProtector.GenerateKey();
        var protector = new AesGcmSecretProtector(key);
        var plaintext = "super-secret-password-12345!@#$%^&*()_+";

        var protectedValue = protector.Protect(plaintext);

        Assert.StartsWith("v1:", protectedValue);
        Assert.DoesNotContain(plaintext, protectedValue);

        var restored = protector.Unprotect(protectedValue);
        Assert.Equal(plaintext, restored);
    }

    [Fact]
    public void Protect_ProducesDifferentCiphertextEachTimeDueToRandomNonce()
    {
        var key = AesGcmSecretProtector.GenerateKey();
        var protector = new AesGcmSecretProtector(key);
        var plaintext = "static-plaintext";

        var c1 = protector.Protect(plaintext);
        var c2 = protector.Protect(plaintext);

        Assert.NotEqual(c1, c2);
        Assert.Equal(plaintext, protector.Unprotect(c1));
        Assert.Equal(plaintext, protector.Unprotect(c2));
    }

    [Fact]
    public void Unprotect_WithWrongKey_ThrowsCryptographicException()
    {
        var key1 = AesGcmSecretProtector.GenerateKey();
        var key2 = AesGcmSecretProtector.GenerateKey();

        var protector1 = new AesGcmSecretProtector(key1);
        var protector2 = new AesGcmSecretProtector(key2);

        var protectedValue = protector1.Protect("my-secret");

        var ex = Assert.Throws<CryptographicException>(() => protector2.Unprotect(protectedValue));
        Assert.DoesNotContain("my-secret", ex.Message);
    }

    [Fact]
    public void Unprotect_TamperedOneByte_ThrowsCryptographicException()
    {
        var key = AesGcmSecretProtector.GenerateKey();
        var protector = new AesGcmSecretProtector(key);

        var protectedValue = protector.Protect("my-confidential-token");
        var rawPayload = Convert.FromBase64String(protectedValue["v1:".Length..]);

        // Flip one bit/byte in ciphertext or tag
        rawPayload[^1] ^= 0xFF;

        var tamperedValue = "v1:" + Convert.ToBase64String(rawPayload);

        var ex = Assert.Throws<CryptographicException>(() => protector.Unprotect(tamperedValue));
        Assert.DoesNotContain("my-confidential-token", ex.Message);
    }

    [Theory]
    [InlineData("v2:something")]
    [InlineData("plain-text-without-prefix")]
    [InlineData("v1:not_valid_base64!")]
    [InlineData("v1:c2hvcnQ=")] // short payload < 28 bytes
    public void Unprotect_InvalidFormat_ThrowsCryptographicException(string malformed)
    {
        var key = AesGcmSecretProtector.GenerateKey();
        var protector = new AesGcmSecretProtector(key);

        var ex = Assert.Throws<CryptographicException>(() => protector.Unprotect(malformed));
        Assert.DoesNotContain(malformed, ex.Message);
    }
}

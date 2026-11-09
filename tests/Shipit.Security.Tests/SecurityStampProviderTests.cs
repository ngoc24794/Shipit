using System.Security.Cryptography;
using Shipit.Security;

namespace Shipit.Security.Tests;

public class SecurityStampProviderTests
{
    private const string TestMasterKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE="; // 32 bytes

    [Fact]
    public void Constructor_GeneratesConsistentStamp_ForSameInputs()
    {
        var provider1 = new SecurityStampProvider(TestMasterKey, "admin", "secret-password");
        var provider2 = new SecurityStampProvider(TestMasterKey, "admin", "secret-password");

        Assert.Equal(provider1.CurrentStamp, provider2.CurrentStamp);
        Assert.True(provider1.ValidateStamp(provider2.CurrentStamp));
    }

    [Fact]
    public void DifferentPassword_ProducesDifferentStamp_AndFailsValidation()
    {
        var provider1 = new SecurityStampProvider(TestMasterKey, "admin", "old-password");
        var provider2 = new SecurityStampProvider(TestMasterKey, "admin", "new-password");

        Assert.NotEqual(provider1.CurrentStamp, provider2.CurrentStamp);
        Assert.False(provider2.ValidateStamp(provider1.CurrentStamp));
    }

    [Fact]
    public void DifferentMasterKey_ProducesDifferentStamp_AndFailsValidation()
    {
        var masterKey2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider1 = new SecurityStampProvider(TestMasterKey, "admin", "secret-password");
        var provider2 = new SecurityStampProvider(masterKey2, "admin", "secret-password");

        Assert.NotEqual(provider1.CurrentStamp, provider2.CurrentStamp);
        Assert.False(provider2.ValidateStamp(provider1.CurrentStamp));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-stamp")]
    public void ValidateStamp_InvalidCandidate_ReturnsFalse(string? candidate)
    {
        var provider = new SecurityStampProvider(TestMasterKey, "admin", "secret-password");
        Assert.False(provider.ValidateStamp(candidate));
    }

    [Fact]
    public void Stamp_DoesNotContainPlaintextPasswordOrUser()
    {
        var provider = new SecurityStampProvider(TestMasterKey, "admin", "super-secret-password");
        Assert.DoesNotContain("super-secret-password", provider.CurrentStamp);
        Assert.DoesNotContain("admin", provider.CurrentStamp);
    }
}

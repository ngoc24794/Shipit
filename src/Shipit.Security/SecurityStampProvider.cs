using System.Security.Cryptography;
using System.Text;

namespace Shipit.Security;

/// <summary>
/// Computes and validates a security stamp claim for admin sessions.
/// The stamp is HMAC-SHA256(derivedKey, $"{adminUser}:{adminPassword}") where derivedKey is derived
/// from the master key. It never contains the plaintext password, and changing SHIPIT_ADMIN_PASSWORD
/// invalidates all existing sessions.
/// </summary>
public sealed class SecurityStampProvider
{
    public const string SecurityStampClaimType = "shipit_security_stamp";

    private readonly string _currentStamp;
    private readonly byte[] _currentStampBytes;

    public SecurityStampProvider(string masterKey, string adminUser, string adminPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterKey);
        ArgumentNullException.ThrowIfNull(adminUser);
        ArgumentNullException.ThrowIfNull(adminPassword);

        byte[] masterKeyBytes;
        try
        {
            masterKeyBytes = Convert.FromBase64String(masterKey);
        }
        catch (FormatException)
        {
            masterKeyBytes = Encoding.UTF8.GetBytes(masterKey);
        }

        var derivedKey = HMACSHA256.HashData(masterKeyBytes, "shipit:security-stamp-key"u8.ToArray());
        var stampBytes = HMACSHA256.HashData(derivedKey, Encoding.UTF8.GetBytes($"{adminUser}:{adminPassword}"));

        _currentStamp = Convert.ToHexString(stampBytes);
        _currentStampBytes = Encoding.UTF8.GetBytes(_currentStamp);
    }

    public string CurrentStamp => _currentStamp;

    public bool ValidateStamp(string? candidateStamp)
    {
        if (string.IsNullOrEmpty(candidateStamp))
        {
            return false;
        }

        var candidateBytes = Encoding.UTF8.GetBytes(candidateStamp);
        return CryptographicOperations.FixedTimeEquals(_currentStampBytes, candidateBytes);
    }
}

using System.Security.Cryptography;
using System.Text;
using Shipit.Core;

namespace Shipit.Security;

/// <summary>
/// Encrypts and decrypts secrets using AES-256-GCM with a versioned payload format:
/// "v1:" + Base64(nonce[12] || tag[16] || ciphertext).
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const string VersionPrefix = "v1:";
    private const int KeySizeInBytes = 32;
    private const int NonceSizeInBytes = 12;
    private const int TagSizeInBytes = 16;

    private readonly byte[] _keyBytes;

    public AesGcmSecretProtector(string masterKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey))
        {
            throw new ArgumentException("Master key must not be null or empty.", nameof(masterKey));
        }

        try
        {
            _keyBytes = Convert.FromBase64String(masterKey);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Master key must be a valid base64-encoded string.", nameof(masterKey), ex);
        }

        if (_keyBytes.Length != KeySizeInBytes)
        {
            throw new ArgumentException($"Master key must be exactly {KeySizeInBytes} bytes (decoded length was {_keyBytes.Length}).", nameof(masterKey));
        }
    }

    /// <summary>Generates a cryptographically secure 256-bit (32-byte) key encoded as base64.</summary>
    public static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(KeySizeInBytes);
        return Convert.ToBase64String(bytes);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var tag = new byte[TagSizeInBytes];
        var ciphertext = new byte[plaintextBytes.Length];

        using (var aesGcm = new AesGcm(_keyBytes, TagSizeInBytes))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var payload = new byte[NonceSizeInBytes + TagSizeInBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeInBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeInBytes, TagSizeInBytes);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSizeInBytes + TagSizeInBytes, ciphertext.Length);

        return VersionPrefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue) || !protectedValue.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Invalid protected value format or missing version prefix.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedValue[VersionPrefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Invalid base64 payload.", ex);
        }

        const int minPayloadLength = NonceSizeInBytes + TagSizeInBytes;
        if (payload.Length < minPayloadLength)
        {
            throw new CryptographicException("Payload is too short to be a valid protected value.");
        }

        var nonce = payload.AsSpan(0, NonceSizeInBytes);
        var tag = payload.AsSpan(NonceSizeInBytes, TagSizeInBytes);
        var ciphertext = payload.AsSpan(minPayloadLength);
        var plaintextBytes = new byte[ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(_keyBytes, TagSizeInBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Decryption failed. Value may be tampered or master key is incorrect.");
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Decryption failed.", ex);
        }
    }
}

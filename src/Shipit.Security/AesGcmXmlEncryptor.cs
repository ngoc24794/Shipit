using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Shipit.Core;

namespace Shipit.Security;

/// <summary>
/// Encrypts XML key elements using Shipit's <see cref="ISecretProtector"/> (AES-256-GCM).
/// </summary>
public sealed class AesGcmXmlEncryptor : IXmlEncryptor
{
    private readonly ISecretProtector _protector;

    public AesGcmXmlEncryptor(ISecretProtector protector)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var xmlString = plaintextElement.ToString(SaveOptions.DisableFormatting);
        var cipher = _protector.Protect(xmlString);

        var encryptedElement = new XElement("encryptedKey",
            new XComment("This key is encrypted with Shipit ISecretProtector (AES-256-GCM)."),
            new XElement("cipher", cipher));

        return new EncryptedXmlInfo(encryptedElement, typeof(AesGcmXmlDecryptor));
    }
}

/// <summary>
/// Decrypts XML key elements encrypted by <see cref="AesGcmXmlEncryptor"/>.
/// </summary>
public sealed class AesGcmXmlDecryptor : IXmlDecryptor
{
    private readonly ISecretProtector _protector;

    public AesGcmXmlDecryptor(ISecretProtector protector)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public AesGcmXmlDecryptor(IServiceProvider services)
        : this(services.GetRequiredService<ISecretProtector>())
    {
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var cipher = encryptedElement.Element("cipher")?.Value
            ?? throw new CryptographicException("The encrypted XML element does not contain a 'cipher' element.");

        var xmlString = _protector.Unprotect(cipher);
        return XElement.Parse(xmlString);
    }
}

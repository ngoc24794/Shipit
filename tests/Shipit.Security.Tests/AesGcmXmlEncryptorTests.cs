using System.Xml.Linq;
using Shipit.Security;

namespace Shipit.Security.Tests;

public class AesGcmXmlEncryptorTests
{
    [Fact]
    public void Encrypt_And_Decrypt_RoundTripsSuccessfully()
    {
        var key = AesGcmSecretProtector.GenerateKey();
        var protector = new AesGcmSecretProtector(key);
        var encryptor = new AesGcmXmlEncryptor(protector);
        var decryptor = new AesGcmXmlDecryptor(protector);

        var originalXml = new XElement("key",
            new XAttribute("id", Guid.NewGuid()),
            new XElement("creationDate", DateTime.UtcNow),
            new XElement("masterKey",
                new XElement("value", "secret-key-material-here")));

        var encryptedInfo = encryptor.Encrypt(originalXml);
        Assert.NotNull(encryptedInfo);
        Assert.Equal(typeof(AesGcmXmlDecryptor), encryptedInfo.DecryptorType);
        Assert.DoesNotContain("secret-key-material-here", encryptedInfo.EncryptedElement.ToString());
        Assert.DoesNotContain("masterKey", encryptedInfo.EncryptedElement.ToString());

        var decryptedXml = decryptor.Decrypt(encryptedInfo.EncryptedElement);
        Assert.Equal(originalXml.ToString(), decryptedXml.ToString());
    }
}

using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api;

/// <summary>
/// Persists Data Protection XML elements into ShipitDbContext, encrypted at rest with <see cref="ISecretProtector"/>.
/// This ensures cookie signing keys survive pod restarts while keeping keys secure in the database.
/// </summary>
public sealed class DbXmlRepository : IXmlRepository
{
    private readonly IDbContextFactory<ShipitDbContext> _dbFactory;
    private readonly ISecretProtector _protector;

    public DbXmlRepository(IDbContextFactory<ShipitDbContext> dbFactory, ISecretProtector protector)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var db = _dbFactory.CreateDbContext();
        var keys = db.DataProtectionKeys.AsNoTracking().ToList();
        var elements = new List<XElement>(keys.Count);

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key.Xml))
            {
                continue;
            }

            try
            {
                var decryptedXml = _protector.Unprotect(key.Xml);
                elements.Add(XElement.Parse(decryptedXml));
            }
            catch (CryptographicException)
            {
                // If a key cannot be decrypted (e.g. invalid or rotated master key), skip it.
                // Old cookies encrypted with this key will fail to decrypt, which is safe.
            }
        }

        return elements.AsReadOnly();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        var xmlString = element.ToString(SaveOptions.DisableFormatting);
        var encryptedXml = _protector.Protect(xmlString);

        using var db = _dbFactory.CreateDbContext();
        var entity = new DataProtectionKey
        {
            FriendlyName = friendlyName,
            Xml = encryptedXml
        };

        db.DataProtectionKeys.Add(entity);
        db.SaveChanges();
    }
}

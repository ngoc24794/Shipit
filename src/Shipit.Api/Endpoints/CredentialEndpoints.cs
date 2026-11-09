using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;

namespace Shipit.Api;

internal static class CredentialEndpoints
{
    private const int MaxValueLength = 16 * 1024;

    public static void MapCredentials(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/credentials");

        group.MapGet("", async (ShipitDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Credentials.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(c => c.ToDto())));

        group.MapPost("", async (CredentialInput input, ShipitDbContext db, ISecretProtector protector, CancellationToken ct) =>
        {
            var errors = new FieldErrors();
            var name = (input.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                errors.Add("name", "Name is required.");
            }
            else if (await db.Credentials.AnyAsync(c => c.Name == name, ct))
            {
                errors.Add("name", "A credential with this name already exists.");
            }

            if (!Wire.TryParseKind(input.Kind, out var kind))
            {
                errors.Add("kind", "Kind must be ssh_key, git_token or registry.");
            }

            var username = string.IsNullOrWhiteSpace(input.Username) ? null : input.Username.Trim();
            if (kind == CredentialKind.Registry && username is null)
            {
                errors.Add("username", "Username is required for a registry credential.");
            }

            ValidateValue(input.Value, kind, required: true, errors);
            if (errors.Any)
            {
                return errors.ToResult();
            }

            var now = DateTime.UtcNow;
            var credential = new Credential
            {
                Name = name,
                Kind = kind,
                Username = kind == CredentialKind.Registry ? username : null,
                ProtectedValue = protector.Protect(input.Value!),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Credentials.Add(credential);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/credentials/{credential.Id}", credential.ToDto());
        });

        group.MapPut("{id:long}", async (long id, CredentialUpdate input, ShipitDbContext db, ISecretProtector protector, CancellationToken ct) =>
        {
            var credential = await db.Credentials.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (credential is null)
            {
                return Results.NotFound();
            }

            var errors = new FieldErrors();
            if (input.Name is not null)
            {
                var name = input.Name.Trim();
                if (name.Length == 0)
                {
                    errors.Add("name", "Name cannot be empty.");
                }
                else if (await db.Credentials.AnyAsync(c => c.Name == name && c.Id != id, ct))
                {
                    errors.Add("name", "A credential with this name already exists.");
                }
                else
                {
                    credential.Name = name;
                }
            }

            if (credential.Kind == CredentialKind.Registry && input.Username is not null)
            {
                var username = input.Username.Trim();
                if (username.Length == 0)
                {
                    errors.Add("username", "Username cannot be empty for a registry credential.");
                }
                else
                {
                    credential.Username = username;
                }
            }

            if (!string.IsNullOrEmpty(input.Value))
            {
                ValidateValue(input.Value, credential.Kind, required: true, errors);
                if (!errors.Any)
                {
                    credential.ProtectedValue = protector.Protect(input.Value);
                }
            }

            if (errors.Any)
            {
                return errors.ToResult();
            }

            credential.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(credential.ToDto());
        });

        group.MapDelete("{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
        {
            var credential = await db.Credentials.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (credential is null)
            {
                return Results.NotFound();
            }

            var inUse = await db.Slaves.AnyAsync(s => s.SshCredentialId == id, ct)
                        || await db.Pipelines.AnyAsync(p => p.GitCredentialId == id || p.RegistryCredentialId == id, ct);
            if (inUse)
            {
                return Problems.Conflict("Credential is in use", "A slave or pipeline still uses this credential.");
            }

            db.Credentials.Remove(credential);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static void ValidateValue(string? value, CredentialKind kind, bool required, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add("value", "Value is required.");
            }

            return;
        }

        if (value.Length > MaxValueLength)
        {
            errors.Add("value", "Value is too long.");
            return;
        }

        switch (kind)
        {
            case CredentialKind.SshKey:
                if (!value.Contains("PRIVATE KEY", StringComparison.Ordinal))
                {
                    errors.Add("value", "An SSH credential must be a PEM private key.");
                }

                break;
            case CredentialKind.GitToken or CredentialKind.Registry:
                // Passed to the slave through stdin, one line.
                if (value.Contains('\n') || value.Contains('\r'))
                {
                    errors.Add("value", "Value must be a single line.");
                }

                break;
        }
    }
}

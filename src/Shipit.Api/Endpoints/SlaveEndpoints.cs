using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Shipit.Core;
using Shipit.Data;
using Shipit.Data.Entities;
using Shipit.Security;

namespace Shipit.Api;

internal static class SlaveEndpoints
{
    public static void MapSlaves(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/slaves");

        group.MapGet("", async (ShipitDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Slaves.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)).Select(s => s.ToDto())));

        group.MapGet("{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
            await db.Slaves.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) is { } slave
                ? Results.Ok(slave.ToDto())
                : Results.NotFound());

        group.MapPost("", async (SlaveInput input, ShipitDbContext db, CancellationToken ct) =>
        {
            var errors = await ValidateAsync(input, db, existingId: null, ct);
            if (errors.Any)
            {
                return errors.ToResult();
            }

            var now = DateTime.UtcNow;
            var slave = new Slave { CreatedAt = now, UpdatedAt = now };
            Apply(slave, input);
            db.Slaves.Add(slave);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/slaves/{slave.Id}", slave.ToDto());
        });

        group.MapPut("{id:long}", async (long id, SlaveInput input, ShipitDbContext db, CancellationToken ct) =>
        {
            var slave = await db.Slaves.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (slave is null)
            {
                return Results.NotFound();
            }

            var errors = await ValidateAsync(input, db, existingId: id, ct);
            if (errors.Any)
            {
                return errors.ToResult();
            }

            Apply(slave, input);
            slave.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(slave.ToDto());
        });

        group.MapDelete("{id:long}", async (long id, ShipitDbContext db, CancellationToken ct) =>
        {
            var slave = await db.Slaves.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (slave is null)
            {
                return Results.NotFound();
            }

            if (await db.Pipelines.AnyAsync(p => p.SlaveId == id, ct))
            {
                return Problems.Conflict("Slave is in use", "A pipeline still uses this slave.");
            }

            db.Slaves.Remove(slave);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("{id:long}/test", async (long id, ShipitDbContext db, ISshExecutor ssh, ISecretProtector protector, CancellationToken ct) =>
        {
            var slave = await db.Slaves.AsNoTracking().Include(s => s.SshCredential).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (slave?.SshCredential is null)
            {
                return Results.NotFound();
            }

            string key;
            try
            {
                key = protector.Unprotect(slave.SshCredential.ProtectedValue);
            }
            catch (CryptographicException)
            {
                return Results.Ok(new ConnectionTestResultDto(false, "The stored SSH key could not be decrypted (is the master key correct?)."));
            }

            var masker = new LogMasker([key]);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var (ok, message) = await ssh.TestConnectionAsync(new SshTarget(slave.Host, slave.Port, slave.User, key), timeout.Token);
                return Results.Ok(new ConnectionTestResultDto(ok, masker.Mask(message)));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.Ok(new ConnectionTestResultDto(false, "Connection test timed out after 30 seconds."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Ok(new ConnectionTestResultDto(false, masker.Mask($"{ex.GetType().Name}: {ex.Message}")));
            }
        });
    }

    private static void Apply(Slave slave, SlaveInput input)
    {
        slave.Name = input.Name!.Trim();
        slave.Host = input.Host!.Trim();
        slave.Port = input.Port ?? 22;
        slave.User = input.User!.Trim();
        slave.SshCredentialId = input.SshCredentialId!.Value;
    }

    private static async Task<FieldErrors> ValidateAsync(SlaveInput input, ShipitDbContext db, long? existingId, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            errors.Add("name", "Name is required.");
        }
        else if (await db.Slaves.AnyAsync(s => s.Name == name && s.Id != existingId, ct))
        {
            errors.Add("name", "A slave with this name already exists.");
        }

        var host = (input.Host ?? string.Empty).Trim();
        if (host.Length == 0)
        {
            errors.Add("host", "Host is required.");
        }
        else if (host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || host.Length > 253)
        {
            errors.Add("host", "Host is not valid.");
        }

        var port = input.Port ?? 22;
        if (port is < 1 or > 65535)
        {
            errors.Add("port", "Port must be between 1 and 65535.");
        }

        var user = (input.User ?? string.Empty).Trim();
        if (user.Length == 0)
        {
            errors.Add("user", "User is required.");
        }
        else if (user.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            errors.Add("user", "User is not valid.");
        }

        if (input.SshCredentialId is null)
        {
            errors.Add("sshCredentialId", "An SSH credential is required.");
        }
        else
        {
            var kind = await db.Credentials.Where(c => c.Id == input.SshCredentialId).Select(c => (CredentialKind?)c.Kind).FirstOrDefaultAsync(ct);
            if (kind is null)
            {
                errors.Add("sshCredentialId", "Credential does not exist.");
            }
            else if (kind != CredentialKind.SshKey)
            {
                errors.Add("sshCredentialId", "Credential must be of kind ssh_key.");
            }
        }

        return errors;
    }
}

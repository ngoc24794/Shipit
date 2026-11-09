using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Shipit.Data;

namespace Shipit.Api;

internal static class HealthAndAuthEndpoints
{
    public static void MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();

        app.MapGet("/readyz", async (IDbContextFactory<ShipitDbContext> dbFactory, CancellationToken ct) =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await using var db = await dbFactory.CreateDbContextAsync(timeout.Token);
                if (!await db.Database.CanConnectAsync(timeout.Token))
                {
                    return Results.Json(new ReadinessDto(false, "unreachable", 0), statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                // Only Postgres uses migrations; the dev SQLite database is created with EnsureCreated, so "pending" is 0 there.
                var pending = db.Database.IsNpgsql()
                    ? (await db.Database.GetPendingMigrationsAsync(timeout.Token)).Count()
                    : 0;
                var ready = pending == 0;
                return Results.Json(
                    new ReadinessDto(ready, "ok", pending),
                    statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Results.Json(new ReadinessDto(false, "unreachable", 0), statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();
    }

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest body, AdminLogin admin, HttpContext http) =>
        {
            if (!admin.Verify(body.Username, body.Password))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250)); // slows down guessing
                return Results.Unauthorized();
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, admin.Username)],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.NoContent();
        }).AllowAnonymous();

        app.MapPost("/api/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        app.MapGet("/api/auth/me", (ClaimsPrincipal user) => Results.Ok(new MeDto(user.Identity?.Name ?? string.Empty)));
    }
}

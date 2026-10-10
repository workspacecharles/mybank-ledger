using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MyBank.Core.Persistence;

namespace MyBank.Api.Endpoints;

internal static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        // live: o processo responde. ready: o banco responde. Nenhum dos dois depende do worker.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");

        // Atraso da consolidação diária: base para alerta. Não afeta a prontidão da API.
        app.MapGet("/health/projection", GetProjectionHealthAsync);
    }

    private static async Task<IResult> GetProjectionHealthAsync(MyBankDbContext db, CancellationToken ct)
    {
        var pending = db.OutboxMessages.Where(m => m.ProcessedAt == null);
        var count = await pending.CountAsync(ct);
        var oldest = await pending.MinAsync(m => (DateTimeOffset?)m.CreatedAt, ct);
        var ageSeconds = oldest is null ? 0 : Math.Round((DateTimeOffset.UtcNow - oldest.Value).TotalSeconds, 1);
        var status = ageSeconds switch
        {
            < 30 => "Healthy",
            < 300 => "Degraded",
            _ => "Unhealthy",
        };

        return Results.Json(
            new { status, pendingEvents = count, oldestPendingAgeSeconds = ageSeconds },
            statusCode: status == "Unhealthy" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
    }
}

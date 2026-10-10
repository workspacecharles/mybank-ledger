using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using MyBank.Api;
using MyBank.Core;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(MyBankDbContext.ConnectionStringName)
    ?? throw new InvalidOperationException($"Connection string '{MyBankDbContext.ConnectionStringName}' não configurada.");
builder.Services.AddDbContext<MyBankDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<LedgerService>();

builder.AddMyBankAuth();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<MyBankDbContext>();

// Corpos pequenos e sem campos desconhecidos (ex.: o cliente não informa recordedAt nem balanceAfter).
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.Configure<JsonOptions>(json => json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

// JSON malformado ou id inválido viram 400 problem+json, também em Development.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Localmente o schema é criado/atualizado na subida. Em produção, migrations rodam no pipeline de deploy.
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<MyBankDbContext>().Database.Migrate();
    app.MapOpenApi();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDevTokenEndpoint();
app.MapAccountEndpoints();

// live: o processo responde. ready: o banco responde. Nenhum dos dois depende do worker.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

// Atraso da consolidação diária: base para alerta. Não afeta a prontidão da API.
app.MapGet("/health/projection", async (MyBankDbContext db, CancellationToken ct) =>
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
});

app.Run();

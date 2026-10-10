using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using MyBank.Core.Persistence;

namespace MyBank.Api.Extensions;

internal static class HttpExtensions
{
    /// <summary>Erros em problem+json, OpenAPI, health checks e limites de entrada das requisições.</summary>
    public static void AddMyBankHttp(this WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ErrorHandler>();
        builder.Services.AddMyBankOpenApi();
        builder.Services.AddHealthChecks().AddDbContextCheck<MyBankDbContext>();

        // Corpos pequenos e sem campos desconhecidos (ex.: o cliente não informa recordedAt nem balanceAfter).
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 16 * 1024);
        builder.Services.Configure<JsonOptions>(json => json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

        // JSON malformado ou id inválido viram 400 problem+json, também em Development.
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
    }
}

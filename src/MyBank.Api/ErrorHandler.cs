using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyBank.Core;
using Npgsql;

namespace MyBank.Api;

/// <summary>
/// Converte erros em application/problem+json com um "code" estável, sem stack trace.
/// Banco indisponível vira 503: nada foi confirmado e o cliente pode repetir com a mesma Idempotency-Key.
/// </summary>
internal sealed class ErrorHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, detail) = exception switch
        {
            LedgerException e => (StatusFor(e.Type), e.Code, e.Message),
            _ when IsDatabaseUnavailable(exception) => (
                StatusCodes.Status503ServiceUnavailable,
                "database_unavailable",
                "A operação não foi confirmada. Repita a requisição com a mesma Idempotency-Key."),
            _ => (0, string.Empty, string.Empty),
        };

        if (status == 0)
        {
            return false; // erro inesperado: 500 genérico
        }

        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            http.Response.Headers.RetryAfter = "2";
        }

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Detail = detail,
                Type = $"urn:mybank:problem:{code}",
                Extensions = { ["code"] = code },
            },
        });
    }

    private static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    private static bool IsDatabaseUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true } or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}

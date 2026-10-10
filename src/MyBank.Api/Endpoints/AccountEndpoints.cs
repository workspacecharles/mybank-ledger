using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MyBank.Api.Contracts;
using MyBank.Api.Extensions;
using MyBank.Core;
using MyBank.Domain;

namespace MyBank.Api.Endpoints;

internal static partial class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var accounts = app.MapGroup("/api/v1/accounts").RequireAuthorization(AuthPolicies.AccountHolder);

        accounts.MapPost("/", CreateAccountAsync).RequireAuthorization(AuthPolicies.Admin);
        accounts.MapPost("/{accountId}/entries", RegisterEntryAsync);
        accounts.MapGet("/{accountId}/entries/{entryId}", GetEntryAsync);
        accounts.MapGet("/{accountId}/balance", GetBalanceAsync);
        accounts.MapGet("/{accountId}/daily-balances", GetDailyBalancesAsync);
    }

    private static async Task<IResult> CreateAccountAsync(CreateAccountRequest? request, LedgerService ledger, CancellationToken ct)
    {
        if (request?.InitialBalance is not { } initialBalance)
        {
            throw LedgerException.Validation("invalid_request", "Informe 'ownerId' e 'initialBalance'.");
        }

        var account = await ledger.CreateAccountAsync(request.OwnerId, initialBalance, ct);
        return Results.Created($"/api/v1/accounts/{account.Id}/balance", AccountResponse.From(account));
    }

    private static async Task<IResult> RegisterEntryAsync(
        Guid accountId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CreateEntryRequest? request,
        ClaimsPrincipal user,
        HttpResponse response,
        LedgerService ledger,
        CancellationToken ct)
    {
        if (request?.Amount is not { } amount)
        {
            throw LedgerException.Validation("invalid_request", "Informe 'type', 'amount' e 'currency'.");
        }

        var (entry, replayed) = await ledger.RegisterEntryAsync(
            accountId, user.SubjectId(), idempotencyKey, request.Type, amount, request.Currency, request.Description, ct);

        // A repetição devolve o mesmo status e corpo; o header só sinaliza que nada novo foi gravado.
        if (replayed)
        {
            response.Headers["Idempotent-Replayed"] = "true";
        }

        return Results.Created($"/api/v1/accounts/{accountId}/entries/{entry.Id}", EntryResponse.From(entry));
    }

    private static async Task<IResult> GetEntryAsync(Guid accountId, Guid entryId, ClaimsPrincipal user, LedgerService ledger, CancellationToken ct)
    {
        var entry = await ledger.GetEntryAsync(accountId, entryId, user.SubjectId(), ct);
        return Results.Ok(EntryResponse.From(entry));
    }

    private static async Task<IResult> GetBalanceAsync(Guid accountId, string? at, ClaimsPrincipal user, LedgerService ledger, CancellationToken ct)
    {
        DateTimeOffset? instant = at is null ? null : ParseInstant(at);
        var balance = await ledger.GetBalanceAsync(accountId, user.SubjectId(), instant, ct);
        return Results.Ok(new BalanceResponse(accountId, balance, LedgerRules.Currency, instant?.ToUniversalTime()));
    }

    private static async Task<IResult> GetDailyBalancesAsync(
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        LedgerService ledger,
        CancellationToken ct)
    {
        if (from is null || to is null)
        {
            throw LedgerException.Validation("invalid_date_range", "Informe 'from' e 'to' no formato YYYY-MM-DD.");
        }

        var result = await ledger.GetDailyBalancesAsync(accountId, user.SubjectId(), from.Value, to.Value, ct);
        var days = result.Days
            .Select(d => new DailyBalanceResponse(d.Date, d.OpeningBalance, d.TotalCredits, d.TotalDebits, d.ClosingBalance, d.EntryCount))
            .ToList();

        return Results.Ok(new DailyBalancesResponse(
            accountId,
            LedgerRules.Currency,
            from.Value,
            to.Value,
            new ProjectionStatus(result.PendingEntries, result.PendingEntries == 0),
            days));
    }

    /// <summary>
    /// Exige fuso explícito (Z ou ±hh:mm): sem fuso, o instante seria ambíguo.
    /// Um '+' não codificado na URL chega como espaço e é restaurado.
    /// </summary>
    internal static DateTimeOffset ParseInstant(string value)
    {
        var normalized = value.Trim().Replace(' ', '+');
        if (ExplicitOffset().IsMatch(normalized)
            && DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
        {
            return instant;
        }

        throw LedgerException.Validation(
            "invalid_instant",
            "'at' deve ser ISO-8601 com fuso explícito, ex.: 2026-10-09T15:30:00Z ou 2026-10-09T12:30:00-03:00.");
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffset();
}

using MyBank.Core;

namespace MyBank.Api;

// Contratos HTTP v1. Campos desconhecidos são rejeitados (ver Program.cs): o cliente
// não consegue informar instante, saldo, sequência nem titular de um lançamento.

public sealed record CreateAccountRequest(string? OwnerId, decimal? InitialBalance);

/// <param name="Type">"credit" ou "debit".</param>
/// <param name="Amount">Positivo, com no máximo duas casas decimais.</param>
/// <param name="Currency">Somente "BRL".</param>
public sealed record CreateEntryRequest(string? Type, decimal? Amount, string? Currency, string? Description);

public sealed record AccountResponse(Guid Id, string OwnerId, string Currency, decimal InitialBalance, decimal Balance, DateTimeOffset CreatedAt)
{
    public static AccountResponse From(Account a) => new(a.Id, a.OwnerId, LedgerRules.Currency, a.InitialBalance, a.Balance, a.CreatedAt);
}

public sealed record EntryResponse(
    Guid Id,
    Guid AccountId,
    long Sequence,
    string Type,
    decimal Amount,
    string Currency,
    decimal BalanceAfter,
    DateTimeOffset RecordedAt,
    string? Description)
{
    public static EntryResponse From(LedgerEntry e) => new(
        e.Id,
        e.AccountId,
        e.Sequence,
        e.Type == EntryType.Credit ? "credit" : "debit",
        e.Amount,
        LedgerRules.Currency,
        e.BalanceAfter,
        e.RecordedAt,
        e.Description);
}

/// <param name="At">Instante consultado (UTC); ausente no saldo atual.</param>
public sealed record BalanceResponse(Guid AccountId, decimal Balance, string Currency, DateTimeOffset? At);

public sealed record DailyBalanceResponse(
    DateOnly Date,
    decimal OpeningBalance,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal ClosingBalance,
    int EntryCount);

/// <param name="PendingEntries">Lançamentos confirmados que o worker ainda não consolidou.</param>
public sealed record ProjectionStatus(int PendingEntries, bool UpToDate);

public sealed record DailyBalancesResponse(
    Guid AccountId,
    string Currency,
    DateOnly From,
    DateOnly To,
    ProjectionStatus Projection,
    IReadOnlyList<DailyBalanceResponse> Days);

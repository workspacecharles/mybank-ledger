using MyBank.Domain;
using MyBank.Domain.Entities;
using MyBank.Domain.Enums;

namespace MyBank.Api.Contracts;

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

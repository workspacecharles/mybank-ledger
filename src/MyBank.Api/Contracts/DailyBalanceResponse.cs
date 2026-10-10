namespace MyBank.Api.Contracts;

public sealed record DailyBalanceResponse(
    DateOnly Date,
    decimal OpeningBalance,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal ClosingBalance,
    int EntryCount);

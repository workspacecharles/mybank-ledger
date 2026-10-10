namespace MyBank.Api.Contracts;

/// <param name="At">Instante consultado (UTC); ausente no saldo atual.</param>
public sealed record BalanceResponse(Guid AccountId, decimal Balance, string Currency, DateTimeOffset? At);

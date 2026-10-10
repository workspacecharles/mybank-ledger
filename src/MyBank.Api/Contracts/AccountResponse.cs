using MyBank.Domain;
using MyBank.Domain.Entities;

namespace MyBank.Api.Contracts;

public sealed record AccountResponse(Guid Id, string OwnerId, string Currency, decimal InitialBalance, decimal Balance, DateTimeOffset CreatedAt)
{
    public static AccountResponse From(Account a) => new(a.Id, a.OwnerId, LedgerRules.Currency, a.InitialBalance, a.Balance, a.CreatedAt);
}

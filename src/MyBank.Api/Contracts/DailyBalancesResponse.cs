namespace MyBank.Api.Contracts;

public sealed record DailyBalancesResponse(
    Guid AccountId,
    string Currency,
    DateOnly From,
    DateOnly To,
    ProjectionStatus Projection,
    IReadOnlyList<DailyBalanceResponse> Days);

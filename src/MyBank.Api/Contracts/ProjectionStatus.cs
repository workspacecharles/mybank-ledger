namespace MyBank.Api.Contracts;

/// <param name="PendingEntries">Lançamentos confirmados que o worker ainda não consolidou.</param>
public sealed record ProjectionStatus(int PendingEntries, bool UpToDate);

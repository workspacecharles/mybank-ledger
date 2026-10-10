namespace MyBank.Api.Contracts;

public sealed record CreateAccountRequest(string? OwnerId, decimal? InitialBalance);

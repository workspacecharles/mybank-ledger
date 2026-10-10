namespace MyBank.Api.Contracts;

public sealed record DevTokenRequest(string? Subject, IReadOnlyList<string>? Roles);

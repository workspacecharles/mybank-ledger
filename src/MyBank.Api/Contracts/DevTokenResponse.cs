namespace MyBank.Api.Contracts;

public sealed record DevTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

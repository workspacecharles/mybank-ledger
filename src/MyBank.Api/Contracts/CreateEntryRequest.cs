namespace MyBank.Api.Contracts;

/// <summary>
/// Corpo do lançamento. Campos desconhecidos são rejeitados (ver Program.cs): o cliente
/// não consegue informar instante, saldo, sequência nem titular de um lançamento.
/// </summary>
/// <param name="Type">"credit" ou "debit".</param>
/// <param name="Amount">Positivo, com no máximo duas casas decimais.</param>
/// <param name="Currency">Somente "BRL".</param>
public sealed record CreateEntryRequest(string? Type, decimal? Amount, string? Currency, string? Description);

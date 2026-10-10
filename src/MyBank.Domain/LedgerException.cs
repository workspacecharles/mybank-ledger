using MyBank.Domain.Enums;

namespace MyBank.Domain;

/// <summary>Requisição rejeitada por regra de negócio; a API a converte em problem+json com <see cref="Code"/>.</summary>
public sealed class LedgerException(ErrorType type, string code, string message) : Exception(message)
{
    public ErrorType Type { get; } = type;

    /// <summary>Código estável para o cliente, ex.: "insufficient_funds".</summary>
    public string Code { get; } = code;

    public static LedgerException Validation(string code, string message) => new(ErrorType.Validation, code, message);

    public static LedgerException NotFound(string code, string message) => new(ErrorType.NotFound, code, message);

    public static LedgerException Conflict(string code, string message) => new(ErrorType.Conflict, code, message);

    public static LedgerException BusinessRule(string code, string message) => new(ErrorType.BusinessRule, code, message);
}

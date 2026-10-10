namespace MyBank.Domain.Enums;

/// <summary>Categoria do erro de negócio; a API a converte no status HTTP.</summary>
public enum ErrorType
{
    Validation = 1,
    NotFound = 2,
    Forbidden = 3,
    Conflict = 4,
    BusinessRule = 5,
}

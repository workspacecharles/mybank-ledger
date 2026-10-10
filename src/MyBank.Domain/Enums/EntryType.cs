namespace MyBank.Domain.Enums;

/// <summary>Começa em 1: o valor padrão (0) não é um tipo válido. No banco é gravado como texto.</summary>
public enum EntryType
{
    Credit = 1,
    Debit = 2,
}

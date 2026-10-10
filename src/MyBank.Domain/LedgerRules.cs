using MyBank.Domain.Entities;
using MyBank.Domain.Enums;

namespace MyBank.Domain;

/// <summary>
/// Regras de negócio puras, sem banco de dados: validações de entrada, regra de saldo,
/// comparação de idempotência, autorização por titular e cálculo da consolidação de um dia.
/// O <see cref="LedgerService"/> e o <see cref="DailyBalanceProjector"/> as aplicam dentro das transações.
/// </summary>
public static class LedgerRules
{
    public const string Currency = "BRL";
    public const decimal MaxAmount = 1_000_000_000_000m;
    public const int MaxDailyRange = 366;
    public const int MaxOwnerLength = 128;
    public const int MaxDescriptionLength = 500;

    public static string ValidateOwner(string? ownerId) =>
        string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > MaxOwnerLength
            ? throw LedgerException.Validation("invalid_owner", $"O titular é obrigatório e deve ter no máximo {MaxOwnerLength} caracteres.")
            : ownerId;

    public static decimal ValidateInitialBalance(decimal initialBalance) =>
        initialBalance < 0 || initialBalance > MaxAmount
            ? throw LedgerException.Validation("invalid_amount", $"O saldo inicial deve estar entre 0 e {MaxAmount:F2}.")
            : ValidateScale(initialBalance);

    public static string ValidateIdempotencyKey(string? key)
    {
        var valid = !string.IsNullOrEmpty(key)
            && key.Length <= 128
            && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.');

        return valid
            ? key!
            : throw LedgerException.Validation(
                "invalid_idempotency_key",
                "O header Idempotency-Key é obrigatório: 1 a 128 caracteres entre letras, dígitos, '-', '_', ':' e '.'.");
    }

    public static EntryType ParseType(string? type) => type?.ToLowerInvariant() switch
    {
        "credit" => EntryType.Credit,
        "debit" => EntryType.Debit,
        _ => throw LedgerException.Validation("invalid_entry_type", "O tipo deve ser 'credit' ou 'debit'."),
    };

    public static decimal ValidateAmount(decimal amount, string? currency)
    {
        if (!string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw LedgerException.Validation("unsupported_currency", $"Somente {Currency} é suportado nesta versão.");
        }

        if (amount <= 0 || amount > MaxAmount)
        {
            throw LedgerException.Validation("invalid_amount", $"O valor deve ser positivo e até {MaxAmount:F2}.");
        }

        return ValidateScale(amount);
    }

    /// <summary>Exige no máximo duas casas e devolve o valor com exatamente duas (10 vira 10.00), como no banco.</summary>
    public static decimal ValidateScale(decimal amount) =>
        decimal.Round(amount, 2) == amount
            ? amount + 0.00m
            : throw LedgerException.Validation("invalid_scale", "O valor deve ter no máximo duas casas decimais.");

    /// <summary>Descrição sem espaços nas pontas; vazia vira null.</summary>
    public static string? NormalizeDescription(string? description)
    {
        var normalized = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return normalized?.Length > MaxDescriptionLength
            ? throw LedgerException.Validation("invalid_description", $"A descrição deve ter no máximo {MaxDescriptionLength} caracteres.")
            : normalized;
    }

    /// <summary>Saldo depois do lançamento. Débito que deixaria o saldo negativo é rejeitado.</summary>
    public static decimal BalanceAfter(decimal balance, EntryType type, decimal amount)
    {
        var balanceAfter = type == EntryType.Credit ? balance + amount : balance - amount;
        return balanceAfter < 0
            ? throw LedgerException.BusinessRule("insufficient_funds", "Saldo insuficiente para o débito.")
            : balanceAfter;
    }

    /// <summary>
    /// Repetição com a mesma chave só é aceita com o mesmo conteúdo (o serviço devolve o
    /// lançamento já gravado); conteúdo diferente é conflito.
    /// </summary>
    public static void EnsureSameRequest(LedgerEntry existing, EntryType type, decimal amount, string? description)
    {
        if (existing.Type != type || existing.Amount != amount || existing.Description != description)
        {
            throw LedgerException.Conflict("idempotency_key_reused", "A chave de idempotência já foi usada com outro conteúdo.");
        }
    }

    public static void EnsureOwner(Account account, string requesterId)
    {
        if (account.OwnerId != requesterId)
        {
            throw new LedgerException(ErrorType.Forbidden, "forbidden", "A conta não pertence ao usuário autenticado.");
        }
    }

    public static void ValidateDateRange(DateOnly from, DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber + 1 > MaxDailyRange)
        {
            throw LedgerException.Validation("invalid_date_range", $"'from' deve ser menor ou igual a 'to', em um período de até {MaxDailyRange} dias.");
        }
    }

    /// <summary>
    /// Consolidação de um dia a partir dos seus lançamentos. Abertura e fechamento vêm do
    /// lançamento de menor e maior sequência, então o resultado não depende da ordem da lista.
    /// </summary>
    public static DailyBalance SummarizeDay(Guid accountId, DateOnly date, IReadOnlyCollection<LedgerEntry> entries)
    {
        if (entries.Count == 0)
        {
            throw new ArgumentException("O dia precisa ter ao menos um lançamento.", nameof(entries));
        }

        var first = entries.MinBy(e => e.Sequence)!;
        var last = entries.MaxBy(e => e.Sequence)!;

        return new DailyBalance
        {
            AccountId = accountId,
            Date = date,
            OpeningBalance = first.Type == EntryType.Credit ? first.BalanceAfter - first.Amount : first.BalanceAfter + first.Amount,
            TotalCredits = entries.Where(e => e.Type == EntryType.Credit).Sum(e => e.Amount),
            TotalDebits = entries.Where(e => e.Type == EntryType.Debit).Sum(e => e.Amount),
            ClosingBalance = last.BalanceAfter,
            EntryCount = entries.Count,
            FirstSequence = first.Sequence,
            LastSequence = last.Sequence,
        };
    }
}

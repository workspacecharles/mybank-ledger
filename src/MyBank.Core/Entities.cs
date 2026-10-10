namespace MyBank.Core;

public enum EntryType
{
    Credit,
    Debit,
}

/// <summary>Conta em BRL. O saldo é atualizado na mesma transação de cada lançamento.</summary>
public sealed class Account
{
    public Guid Id { get; set; }

    /// <summary>Titular (claim "sub" do token); usado na autorização por conta.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public decimal InitialBalance { get; set; }

    public decimal Balance { get; set; }

    /// <summary>Sequência do último lançamento da conta; 0 quando não há lançamentos.</summary>
    public long LastSequence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Lançamento confirmado. Imutável: correções são novos lançamentos compensatórios
/// (UPDATE e DELETE são bloqueados por trigger no banco).
/// </summary>
public sealed class LedgerEntry
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>Ordem do lançamento na conta (1, 2, 3...).</summary>
    public long Sequence { get; set; }

    public EntryType Type { get; set; }

    /// <summary>Sempre positivo; o tipo define o sinal.</summary>
    public decimal Amount { get; set; }

    /// <summary>Saldo da conta logo após este lançamento; base da consulta de saldo histórico.</summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>Instante atribuído pelo banco de dados (UTC).</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public string? Description { get; set; }

    /// <summary>Chave enviada pelo cliente; única por conta.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Evento "lançamento registrado", gravado na mesma transação do lançamento
/// (Transactional Outbox) e consumido pelo worker da consolidação diária.
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }

    public Guid EntryId { get; set; }

    public Guid AccountId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>Consolidação diária (UTC) de uma conta. Visão derivada e eventualmente consistente.</summary>
public sealed class DailyBalance
{
    public Guid AccountId { get; set; }

    public DateOnly Date { get; set; }

    public decimal OpeningBalance { get; set; }

    public decimal TotalCredits { get; set; }

    public decimal TotalDebits { get; set; }

    public decimal ClosingBalance { get; set; }

    public int EntryCount { get; set; }

    public long FirstSequence { get; set; }

    public long LastSequence { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

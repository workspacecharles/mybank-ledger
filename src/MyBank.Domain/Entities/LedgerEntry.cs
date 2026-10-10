using MyBank.Domain.Enums;

namespace MyBank.Domain.Entities;

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

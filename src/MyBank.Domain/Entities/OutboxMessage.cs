namespace MyBank.Domain.Entities;

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

namespace MyBank.Domain.Entities;

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

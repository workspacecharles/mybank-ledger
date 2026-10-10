using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBank.Domain.Entities;

namespace MyBank.Core.Persistence.Configurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> entry)
    {
        entry.ToTable("ledger_entries", t =>
        {
            t.HasCheckConstraint("ck_ledger_entries_amount", "amount > 0");
            t.HasCheckConstraint("ck_ledger_entries_balance_after", "balance_after >= 0");
        });
        entry.Property(e => e.Type).HasConversion<string>().HasMaxLength(8);
        entry.Property(e => e.Description).HasMaxLength(500);
        entry.Property(e => e.IdempotencyKey).HasMaxLength(128);
        entry.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);

        // Garantias no banco, independentes do código: uma sequência e uma chave por conta.
        entry.HasIndex(e => new { e.AccountId, e.Sequence }).IsUnique();
        entry.HasIndex(e => new { e.AccountId, e.IdempotencyKey }).IsUnique();

        // Saldo histórico: último lançamento com recorded_at <= instante, lendo uma linha.
        entry.HasIndex(e => new { e.AccountId, e.RecordedAt, e.Sequence });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBank.Domain.Entities;

namespace MyBank.Core.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> message)
    {
        message.ToTable("outbox_messages");
        message.HasOne<LedgerEntry>().WithOne().HasForeignKey<OutboxMessage>(m => m.EntryId).OnDelete(DeleteBehavior.Restrict);

        // Somente pendentes: o índice fica pequeno mesmo com o histórico crescendo.
        message.HasIndex(m => m.Id).HasDatabaseName("ix_outbox_messages_pending").HasFilter("processed_at IS NULL");
        message.HasIndex(m => m.AccountId).HasDatabaseName("ix_outbox_messages_pending_account").HasFilter("processed_at IS NULL");
    }
}

using System.Text;
using Microsoft.EntityFrameworkCore;
using MyBank.Domain.Entities;

namespace MyBank.Core;

public sealed class MyBankDbContext(DbContextOptions<MyBankDbContext> options) : DbContext(options)
{
    public const string ConnectionStringName = "MyBankDb";

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<DailyBalance> DailyBalances => Set<DailyBalance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("mybank");

        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable("accounts", t =>
            {
                // Última barreira caso uma regra da aplicação falhe: saldo nunca negativo.
                t.HasCheckConstraint("ck_accounts_balance", "balance >= 0");
                t.HasCheckConstraint("ck_accounts_initial_balance", "initial_balance >= 0");
            });
            account.Property(a => a.OwnerId).HasMaxLength(128);
            account.HasIndex(a => a.OwnerId);
        });

        modelBuilder.Entity<LedgerEntry>(entry =>
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
        });

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable("outbox_messages");
            message.HasOne<LedgerEntry>().WithOne().HasForeignKey<OutboxMessage>(m => m.EntryId).OnDelete(DeleteBehavior.Restrict);

            // Somente pendentes: o índice fica pequeno mesmo com o histórico crescendo.
            message.HasIndex(m => m.Id).HasDatabaseName("ix_outbox_messages_pending").HasFilter("processed_at IS NULL");
            message.HasIndex(m => m.AccountId).HasDatabaseName("ix_outbox_messages_pending_account").HasFilter("processed_at IS NULL");
        });

        modelBuilder.Entity<DailyBalance>(daily =>
        {
            daily.ToTable("daily_balances");
            daily.HasKey(d => new { d.AccountId, d.Date });
        });

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            property.SetColumnName(ToSnakeCase(property.Name));
            if (property.ClrType == typeof(decimal))
            {
                property.SetPrecision(19);
                property.SetScale(2);
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && builder.Length > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBank.Domain.Entities;

namespace MyBank.Core.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> account)
    {
        account.ToTable("accounts", t =>
        {
            // Última barreira caso uma regra da aplicação falhe: saldo nunca negativo.
            t.HasCheckConstraint("ck_accounts_balance", "balance >= 0");
            t.HasCheckConstraint("ck_accounts_initial_balance", "initial_balance >= 0");
        });
        account.Property(a => a.OwnerId).HasMaxLength(128);
        account.HasIndex(a => a.OwnerId);
    }
}

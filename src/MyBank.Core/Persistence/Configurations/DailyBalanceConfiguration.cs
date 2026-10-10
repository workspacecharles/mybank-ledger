using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBank.Domain.Entities;

namespace MyBank.Core.Persistence.Configurations;

internal sealed class DailyBalanceConfiguration : IEntityTypeConfiguration<DailyBalance>
{
    public void Configure(EntityTypeBuilder<DailyBalance> daily)
    {
        daily.ToTable("daily_balances");
        daily.HasKey(d => new { d.AccountId, d.Date });
    }
}

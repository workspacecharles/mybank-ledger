using System.Text;
using Microsoft.EntityFrameworkCore;
using MyBank.Domain.Entities;

namespace MyBank.Core.Persistence;

/// <summary>Mapeamento de cada entidade em <c>Configurations/</c>; aqui ficam só as regras comuns a todas.</summary>
public sealed class MyBankDbContext(DbContextOptions<MyBankDbContext> options) : DbContext(options)
{
    public const string ConnectionStringName = "MyBankDb";

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<DailyBalance> DailyBalances => Set<DailyBalance>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Valores monetários: até 17 dígitos inteiros e 2 casas decimais.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("mybank");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MyBankDbContext).Assembly);

        // Depois das configurações: só assim o laço enxerga todas as entidades e propriedades.
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            property.SetColumnName(ToSnakeCase(property.Name));
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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyBank.Core;

/// <summary>Usada pelo dotnet-ef para gerar migrations sem iniciar a API.</summary>
public sealed class MyBankDbContextFactory : IDesignTimeDbContextFactory<MyBankDbContext>
{
    public MyBankDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MyBankDb")
            ?? "Host=localhost;Port=5432;Database=mybank;Username=postgres;Password=test123";

        return new MyBankDbContext(new DbContextOptionsBuilder<MyBankDbContext>().UseNpgsql(connectionString).Options);
    }
}

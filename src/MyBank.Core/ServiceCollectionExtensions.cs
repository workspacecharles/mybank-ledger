using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyBank.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registra o banco (PostgreSQL) e os serviços do Core. Usado pela API e pelo Worker.</summary>
    public static IServiceCollection AddMyBankCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(MyBankDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{MyBankDbContext.ConnectionStringName}' não configurada.");

        services.AddDbContext<MyBankDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<LedgerService>();
        services.AddScoped<DailyBalanceProjector>();
        return services;
    }
}

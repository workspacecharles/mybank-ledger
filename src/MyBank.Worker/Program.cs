using Microsoft.EntityFrameworkCore;
using MyBank.Core;
using MyBank.Worker;

// Processo separado da API: pode parar, reiniciar ou ter várias instâncias sem impedir lançamentos.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(MyBankDbContext.ConnectionStringName)
    ?? throw new InvalidOperationException($"Connection string '{MyBankDbContext.ConnectionStringName}' não configurada.");
builder.Services.AddDbContext<MyBankDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<DailyBalanceProjector>();
builder.Services.AddHostedService<ProjectionWorker>();

builder.Build().Run();

using MyBank.Core;
using MyBank.Worker;

// Processo separado da API: pode parar, reiniciar ou ter várias instâncias sem impedir lançamentos.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMyBankCore(builder.Configuration);
builder.Services.AddHostedService<ProjectionWorker>();

builder.Build().Run();

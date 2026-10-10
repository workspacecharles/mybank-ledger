using MyBank.Core.Ledger;

namespace MyBank.Worker;

/// <summary>
/// Processa a outbox em lotes. Fila vazia: espera o intervalo de polling.
/// Falha (ex.: banco fora): registra, espera e tenta de novo; o lote não confirmado volta para a fila.
/// </summary>
internal sealed partial class ProjectionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ProjectionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batchSize = configuration.GetValue("Projection:BatchSize", 500);
        var pollInterval = configuration.GetValue("Projection:PollInterval", TimeSpan.FromSeconds(1));

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = pollInterval;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var projector = scope.ServiceProvider.GetRequiredService<DailyBalanceProjector>();
                var processed = await projector.ProcessBatchAsync(batchSize, stoppingToken);
                if (processed > 0)
                {
                    LogBatch(logger, processed);
                    continue; // ainda pode haver fila: segue sem esperar
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailure(logger, ex, RetryDelay);
                delay = RetryDelay;
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consolidação: {Count} eventos processados")]
    private static partial void LogBatch(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao processar a outbox; nova tentativa em {Delay}")]
    private static partial void LogFailure(ILogger logger, Exception exception, TimeSpan delay);
}

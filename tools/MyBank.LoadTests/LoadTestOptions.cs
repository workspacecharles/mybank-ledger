using System.Globalization;

namespace MyBank.LoadTests;

internal sealed record LoadTestOptions(
    Uri BaseUrl,
    Scenario Scenario,
    int Rate,
    TimeSpan Duration,
    int Accounts,
    double DuplicateRatio,
    string? ConnectionString,
    string? OutputPath,
    bool WorkerStopped)
{
    public const string DefaultBaseUrl = "http://localhost:5013";
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=mybank;Username=postgres;Password=test123";

    public static LoadTestOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Argumento inválido: {args[i]}");
            }

            values[args[i][2..]] = args[++i];
        }

        string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);

        var scenario = Get("scenario", "mixed").ToLowerInvariant() switch
        {
            "mixed" => Scenario.Mixed,
            "hot-account" => Scenario.HotAccount,
            "daily-balance" => Scenario.DailyBalance,
            var other => throw new ArgumentException($"Cenário desconhecido: {other}"),
        };

        var workerStopped = Get("worker", "running").ToLowerInvariant() switch
        {
            "running" => false,
            "stopped" => true,
            var other => throw new ArgumentException($"Valor inválido para --worker: {other} (use running ou stopped)"),
        };

        var connectionString = Get("db", DefaultConnectionString);
        return new LoadTestOptions(
            new Uri(Get("base-url", DefaultBaseUrl)),
            scenario,
            int.Parse(Get("rate", "50"), CultureInfo.InvariantCulture),
            TimeSpan.FromSeconds(int.Parse(Get("duration", "60"), CultureInfo.InvariantCulture)),
            int.Parse(Get("accounts", "20"), CultureInfo.InvariantCulture),
            double.Parse(Get("duplicates", "0.05"), CultureInfo.InvariantCulture),
            string.Equals(connectionString, "none", StringComparison.OrdinalIgnoreCase) ? null : connectionString,
            values.GetValueOrDefault("output"),
            workerStopped);
    }

    /// <summary>
    /// Comando que repete esta execução, com todas as opções explícitas. Uma connection string
    /// diferente da local não é impressa (pode conter senha): vira um marcador.
    /// </summary>
    public string ToCommandLine()
    {
        var parts = new List<string>
        {
            "dotnet run --project tools/MyBank.LoadTests -c Release --",
            $"--scenario {ScenarioName}",
            string.Create(CultureInfo.InvariantCulture, $"--rate {Rate}"),
            string.Create(CultureInfo.InvariantCulture, $"--duration {Duration.TotalSeconds:F0}"),
        };

        if (Scenario != Scenario.HotAccount)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"--accounts {Accounts}"));
        }

        parts.Add(string.Create(CultureInfo.InvariantCulture, $"--duplicates {DuplicateRatio:0.##}"));

        if (WorkerStopped)
        {
            parts.Add("--worker stopped");
        }

        if (BaseUrl != new Uri(DefaultBaseUrl))
        {
            parts.Add($"--base-url {BaseUrl}");
        }

        if (ConnectionString is null)
        {
            parts.Add("--db none");
        }
        else if (ConnectionString != DefaultConnectionString)
        {
            parts.Add("--db \"<sua connection string>\"");
        }

        if (OutputPath is not null)
        {
            parts.Add($"--output {OutputPath.Replace('\\', '/')}");
        }

        return string.Join(' ', parts);
    }

    /// <summary>Espera máxima pela consolidação. Com o worker parado, inclui o tempo de iniciá-lo manualmente.</summary>
    public TimeSpan ProjectionTimeout => WorkerStopped ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(2);

    public string ScenarioName => Scenario switch
    {
        Scenario.HotAccount => "hot-account",
        Scenario.DailyBalance => "daily-balance",
        _ => "mixed",
    };

    public const string Usage = """
        Uso: dotnet run --project tools/MyBank.LoadTests -c Release -- [opções]
          --base-url <url>        API (padrão http://localhost:5013)
          --scenario <nome>       mixed | hot-account | daily-balance (padrão mixed)
          --rate <req/s>          taxa constante de chegada (padrão 50)
          --duration <segundos>   janela de carga (padrão 60)
          --accounts <n>          contas criadas para o cenário (padrão 20)
          --duplicates <0..1>     fração de lançamentos reenviados com a mesma chave (padrão 0.05)
          --worker <estado>       running | stopped (padrão running). Com stopped, a ferramenta confere
                                  que a consolidação ficou parada durante a carga e pede para iniciar o worker
          --db <conn|none>        PostgreSQL para verificar perda/duplicação (padrão: banco local)
          --output <arquivo.md>   grava o relatório em Markdown
        """;
}

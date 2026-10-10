using System.Globalization;

namespace MyBank.LoadTests;

internal enum Scenario
{
    /// <summary>50% lançamentos em contas distintas, 30% saldo, 20% consolidação diária.</summary>
    Mixed,

    /// <summary>100% lançamentos concorrentes na mesma conta (pior caso de serialização).</summary>
    HotAccount,

    /// <summary>100% consultas à consolidação diária.</summary>
    DailyBalance,
}

internal sealed record LoadTestOptions(
    Uri BaseUrl,
    Scenario Scenario,
    int Rate,
    TimeSpan Duration,
    int Accounts,
    double DuplicateRatio,
    string? ConnectionString,
    string? OutputPath)
{
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

        var connectionString = Get("db", DefaultConnectionString);
        return new LoadTestOptions(
            new Uri(Get("base-url", "http://localhost:5013")),
            scenario,
            int.Parse(Get("rate", "50"), CultureInfo.InvariantCulture),
            TimeSpan.FromSeconds(int.Parse(Get("duration", "60"), CultureInfo.InvariantCulture)),
            int.Parse(Get("accounts", "20"), CultureInfo.InvariantCulture),
            double.Parse(Get("duplicates", "0.05"), CultureInfo.InvariantCulture),
            string.Equals(connectionString, "none", StringComparison.OrdinalIgnoreCase) ? null : connectionString,
            values.GetValueOrDefault("output"));
    }

    public const string Usage = """
        Uso: dotnet run --project tools/MyBank.LoadTests -c Release -- [opções]
          --base-url <url>        API (padrão http://localhost:5013)
          --scenario <nome>       mixed | hot-account | daily-balance (padrão mixed)
          --rate <req/s>          taxa constante de chegada (padrão 50)
          --duration <segundos>   janela de carga (padrão 60)
          --accounts <n>          contas criadas para o cenário (padrão 20)
          --duplicates <0..1>     fração de lançamentos reenviados com a mesma chave (padrão 0.05)
          --db <conn|none>        PostgreSQL para verificar perda/duplicação (padrão: banco local)
          --output <arquivo.md>   grava o relatório em Markdown
        """;
}

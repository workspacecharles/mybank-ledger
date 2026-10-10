using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MyBank.LoadTests;

internal sealed record Report(string Markdown, bool Passed)
{
    public const double MaxErrorRate = 0.05;
    public const double MaxP95Ms = 500;

    public static Report Build(
        LoadTestOptions options,
        IReadOnlyList<Sample> samples,
        TimeSpan dispatchElapsed,
        int dispatched,
        Verification verification,
        int replaysVerified,
        int timeoutRetries,
        IReadOnlyList<string> anomalies)
    {
        var errors = samples.Count(s => IsApplicationError(s.Outcome));
        var errorRate = samples.Count == 0 ? 0 : (double)errors / samples.Count;
        var p95 = Percentile(samples.Select(s => s.LatencyMs), 0.95);
        var rejections = samples.Count(s => s.Outcome == Outcome.BusinessRejection);

        var checks = new List<(string Name, bool Ok, string Detail)>
        {
            ("Taxa de erro da aplicação < 5%", errorRate < MaxErrorRate, Percent(errorRate)),
            ("Latência p95 ≤ 500 ms (todas as operações)", p95 <= MaxP95Ms, Ms(p95)),
            ("Saldo atual = saldo inicial + lançamentos aceitos", verification.BalanceMismatches.Count == 0, $"{verification.BalanceMismatches.Count} divergências"),
            ("Zero lançamento confirmado perdido", verification.LedgerChecked && verification.Lost == 0, verification.LedgerChecked ? $"{verification.Lost} perdidos" : "não verificado (--db none)"),
            ("Zero lançamento duplicado", verification.LedgerChecked && verification.DuplicatedAcceptances == 0 && verification.ConfirmedWithoutResponse == 0,
                $"razão = lançamentos aceitos ({verification.LedgerEntries}/{verification.AcceptedEntries}); {replaysVerified} repetições devolveram o lançamento original"),
            ("Consolidação diária converge para o razão", verification.ProjectionConvergence is not null, verification.ProjectionConvergence is { } t ? string.Create(CultureInfo.InvariantCulture, $"em {t.TotalSeconds:F1} s após o fim da carga") : "não convergiu em 120 s"),
            ("Sem respostas inesperadas", anomalies.Count == 0, $"{anomalies.Count} anomalias"),
        };
        var passed = checks.All(c => c.Ok);

        var md = new StringBuilder();
        Line(md, $"### Cenário `{ScenarioName(options.Scenario)}` — {(passed ? "APROVADO" : "REPROVADO")}");
        md.AppendLine();
        Line(md, $"- Execução: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC; {RuntimeInformation.OSDescription}; {Environment.ProcessorCount} CPUs lógicas; {RuntimeInformation.FrameworkDescription}");
        Line(md, $"- Carga: {options.Rate} req/s alvo por {options.Duration.TotalSeconds:F0} s; {dispatched} requisições despachadas em {dispatchElapsed.TotalSeconds:F1} s (**{dispatched / dispatchElapsed.TotalSeconds:F1} req/s** alcançados)");
        Line(md, $"- Contas: {(options.Scenario == Scenario.HotAccount ? 1 : options.Accounts)}; repetições idempotentes: {options.DuplicateRatio * 100:F0}% dos lançamentos; retries após timeout: {timeoutRetries}");
        Line(md, $"- Respostas: {samples.Count}; erros da aplicação (5xx, timeout, falha de transporte, 4xx inesperado): {errors} ({Percent(errorRate)}); rejeições de negócio (422, saldo insuficiente): {rejections}");
        md.AppendLine();
        md.AppendLine("| Operação | Requisições | p50 | p95 | p99 | máx | Erros |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var group in samples.GroupBy(s => s.Operation).OrderBy(g => g.Key, StringComparer.Ordinal).Append(samples.GroupBy(_ => "**total**").Single()))
        {
            var latencies = group.Select(s => s.LatencyMs).ToList();
            Line(md, $"| {group.Key} | {group.Count()} | {Ms(Percentile(latencies, 0.5))} | {Ms(Percentile(latencies, 0.95))} | {Ms(Percentile(latencies, 0.99))} | {Ms(latencies.Max())} | {group.Count(s => IsApplicationError(s.Outcome))} |");
        }

        md.AppendLine();
        md.AppendLine("| Verificação | Resultado | Detalhe |");
        md.AppendLine("|---|---|---|");
        foreach (var (name, ok, detail) in checks)
        {
            Line(md, $"| {name} | {(ok ? "OK" : "FALHOU")} | {detail} |");
        }

        if (verification.LedgerChecked)
        {
            md.AppendLine();
            Line(md, $"Razão: {verification.AcceptedEntries} lançamentos confirmados ao cliente; {verification.LedgerEntries} no banco; {verification.ConfirmedWithoutResponse} confirmados sem resposta recebida pelo cliente.");
        }

        foreach (var anomaly in anomalies.Concat(verification.BalanceMismatches).Take(10))
        {
            Line(md, $"- {anomaly}");
        }

        return new Report(md.ToString(), passed);
    }

    private static bool IsApplicationError(Outcome outcome) =>
        outcome is Outcome.ServerError or Outcome.Timeout or Outcome.TransportError or Outcome.UnexpectedClientError;

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static string ScenarioName(Scenario scenario) => scenario switch
    {
        Scenario.HotAccount => "hot-account",
        Scenario.DailyBalance => "daily-balance",
        _ => "mixed",
    };

    private static string Ms(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:F1} ms");

    private static string Percent(double value) => string.Create(CultureInfo.InvariantCulture, $"{value * 100:F2}%");

    private static void Line(StringBuilder builder, FormattableString text) => builder.AppendLine(text.ToString(CultureInfo.InvariantCulture));
}

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
            ("Consolidação diária converge para o razão", verification.ProjectionConvergence is not null, verification.ProjectionConvergence is { } t ? string.Create(CultureInfo.InvariantCulture, $"em {t.TotalSeconds:F1} s após o fim da carga") : $"não convergiu em {options.ProjectionTimeout.TotalSeconds:F0} s"),
            ("Sem respostas inesperadas", anomalies.Count == 0, $"{anomalies.Count} anomalias"),
        };

        if (options.WorkerStopped)
        {
            var pending = verification.PendingWithWorkerStopped ?? 0;
            checks.Insert(0, ("Worker parado durante a carga (lançamentos acumulados sem consolidar)", pending > 0, $"{pending} pendentes ao fim da carga"));
        }

        var passed = checks.All(c => c.Ok);
        var version = CodeVersion.Detect();

        var md = new StringBuilder();
        Line(md, $"### Cenário `{options.ScenarioName}`{(options.WorkerStopped ? " com worker parado" : string.Empty)} — {(passed ? "APROVADO" : "REPROVADO")}");
        md.AppendLine();
        Line(md, $"- Execução: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC; {RuntimeInformation.OSDescription}; {Environment.ProcessorCount} CPUs lógicas; {RuntimeInformation.FrameworkDescription}");
        Line(md, $"- Código: {version.Describe()}");
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

        md.AppendLine();
        md.Append(HowToReproduce(options, version));
        return new Report(md.ToString(), passed);
    }

    /// <summary>Passo a passo para repetir exatamente esta execução, gravado no próprio relatório.</summary>
    public static string HowToReproduce(LoadTestOptions options, CodeVersion version)
    {
        var md = new StringBuilder();
        md.AppendLine("#### Como reproduzir");
        md.AppendLine();
        md.AppendLine("As latências variam com a máquina; as verificações da tabela acima devem passar em qualquer ambiente. Na raiz do repositório, com .NET SDK 10 e Docker em execução:");
        md.AppendLine();

        var step = 1;
        if (version.Commit is { } commit)
        {
            var note = version.HasLocalChanges ? " (esta execução tinha alterações locais não commitadas; o resultado pode diferir)" : string.Empty;
            Line(md, $"{step++}. Use a mesma versão do código: `git checkout {commit}`{note}.");
        }

        Line(md, $"{step++}. Suba o PostgreSQL: `docker compose up -d`.");
        Line(md, $"{step++}. Terminal 1, API: `dotnet run --project src/MyBank.Api -c Release`.");
        if (options.WorkerStopped)
        {
            Line(md, $"{step++}. Terminal 2, Worker: **não inicie ainda**. Este cenário mede a API com a consolidação parada.");
        }
        else
        {
            Line(md, $"{step++}. Terminal 2, Worker: `dotnet run --project src/MyBank.Worker -c Release`.");
        }
        Line(md, $"{step++}. Terminal 3, carga: `{options.ToCommandLine()}`.");
        if (options.WorkerStopped)
        {
            Line(md, $"{step++}. Quando a ferramenta mostrar \"Inicie o worker agora\", rode no terminal 2 `dotnet run --project src/MyBank.Worker -c Release`. A ferramenta espera até {options.ProjectionTimeout.TotalMinutes:F0} min a consolidação alcançar o razão.");
        }

        Line(md, $"{step}. A ferramenta termina com código 0 se todas as verificações passarem (1 se alguma falhar) e grava um relatório neste mesmo formato{(options.OutputPath is null ? " quando recebe `--output <arquivo.md>`" : " no arquivo do `--output`")}.");
        return md.ToString();
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

    private static string Ms(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:F1} ms");

    private static string Percent(double value) => string.Create(CultureInfo.InvariantCulture, $"{value * 100:F2}%");

    private static void Line(StringBuilder builder, FormattableString text) => builder.AppendLine(text.ToString(CultureInfo.InvariantCulture));
}

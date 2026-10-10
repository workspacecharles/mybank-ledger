using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace MyBank.LoadTests;

internal enum Outcome
{
    Success,
    BusinessRejection,
    UnexpectedClientError,
    ServerError,
    Timeout,
    TransportError,
}

internal sealed record Sample(string Operation, double LatencyMs, Outcome Outcome);

internal sealed record AcceptedEntry(Guid AccountId, string Key, Guid EntryId, decimal SignedAmount, object Body);

internal sealed record AccountState(Guid Id, decimal InitialBalance);

/// <summary>
/// Gerador de carga em modelo aberto: dispara requisições em taxa constante,
/// independentemente da latência das respostas, e verifica ao final que nenhum
/// lançamento confirmado foi perdido ou duplicado.
/// </summary>
internal sealed class LoadTestRunner(LoadTestOptions options) : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new(new SocketsHttpHandler { MaxConnectionsPerServer = 512, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        BaseAddress = options.BaseUrl,
        Timeout = RequestTimeout,
    };

    private readonly ConcurrentBag<Sample> _samples = [];
    private readonly ConcurrentDictionary<string, AcceptedEntry> _accepted = new();
    private readonly ConcurrentBag<string> _anomalies = [];
    private int _replaysVerified;
    private int _timeoutRetries;
    private List<AccountState> _accounts = [];

    public async Task<int> RunAsync()
    {
        Console.WriteLine($"Preparando cenário {options.Scenario} em {options.BaseUrl}...");
        await AuthenticateAsync();
        _accounts = await CreateAccountsAsync();

        if (options.Scenario == Scenario.DailyBalance)
        {
            await SeedEntriesAsync();
        }

        Console.WriteLine($"Carga: {options.Rate} req/s por {options.Duration.TotalSeconds:F0}s ({options.Rate * (int)options.Duration.TotalSeconds} requisições)...");
        var (elapsed, dispatched) = await RunScheduleAsync();

        Console.WriteLine("Verificando consistência...");
        var verification = await VerifyAsync();

        var report = Report.Build(options, _samples.ToList(), elapsed, dispatched, verification, _replaysVerified, _timeoutRetries, [.. _anomalies]);
        Console.WriteLine();
        Console.WriteLine(report.Markdown);

        if (options.OutputPath is { } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path, report.Markdown);
            Console.WriteLine($"Relatório gravado em {path}");
        }

        return report.Passed ? 0 : 1;
    }

    public void Dispose() => _http.Dispose();

    private async Task AuthenticateAsync()
    {
        var token = await IssueTokenAsync("load-test-user", "accounts-admin");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<string> IssueTokenAsync(string subject, params string[] roles)
    {
        var response = await _http.PostAsJsonAsync("/dev/token", new { subject, roles });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task<List<AccountState>> CreateAccountsAsync()
    {
        var count = options.Scenario == Scenario.HotAccount ? 1 : options.Accounts;
        var initial = options.Scenario == Scenario.HotAccount ? 5_000m : 100_000m;
        var accounts = new List<AccountState>(count);
        for (var i = 0; i < count; i++)
        {
            var response = await _http.PostAsJsonAsync("/api/v1/accounts", new { ownerId = "load-test-user", initialBalance = initial });
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            accounts.Add(new AccountState(body.RootElement.GetProperty("id").GetGuid(), initial));
        }

        return accounts;
    }

    private async Task SeedEntriesAsync()
    {
        foreach (var account in _accounts)
        {
            for (var i = 0; i < 5; i++)
            {
                await PostEntryAsync(account.Id, Guid.NewGuid().ToString("N"), NewBody(), "seed");
            }
        }

        await WaitForProjectionAsync(TimeSpan.FromSeconds(60));
    }

    private async Task<(TimeSpan Elapsed, int Dispatched)> RunScheduleAsync()
    {
        var total = (int)(options.Rate * options.Duration.TotalSeconds);
        var interval = TimeSpan.FromSeconds(1.0 / options.Rate);
        var tasks = new List<Task>(total);
        var clock = Stopwatch.StartNew();

        for (var i = 0; i < total; i++)
        {
            var wait = (interval * i) - clock.Elapsed;
            if (wait > TimeSpan.FromMilliseconds(1))
            {
                await Task.Delay(wait);
            }

            tasks.Add(Task.Run(ExecuteOneAsync));
        }

        var dispatchElapsed = clock.Elapsed;
        await Task.WhenAll(tasks);
        return (dispatchElapsed, total);
    }

    private Task ExecuteOneAsync()
    {
        var roll = Random.Shared.NextDouble();
        var account = _accounts[Random.Shared.Next(_accounts.Count)];

        return options.Scenario switch
        {
            Scenario.HotAccount => WriteAsync(account),
            Scenario.DailyBalance => GetAsync("daily-balance", DailyBalanceUri(account.Id)),
            _ when roll < 0.5 => WriteAsync(account),
            _ when roll < 0.8 => GetAsync("balance", new Uri($"/api/v1/accounts/{account.Id}/balance", UriKind.Relative)),
            _ => GetAsync("daily-balance", DailyBalanceUri(account.Id)),
        };
    }

    private Task WriteAsync(AccountState account)
    {
        // Parte dos lançamentos é reenviada com a mesma chave e corpo (retry do cliente).
        if (Random.Shared.NextDouble() < options.DuplicateRatio && !_accepted.IsEmpty)
        {
            var previous = _accepted.Values.ElementAt(Random.Shared.Next(_accepted.Count));
            return PostEntryAsync(previous.AccountId, previous.Key, previous.Body, "entry-replay");
        }

        return PostEntryAsync(account.Id, Guid.NewGuid().ToString("N"), NewBody(), "entry");
    }

    private object NewBody()
    {
        // No cenário de conta concorrida, débitos disputam o mesmo saldo.
        var debitShare = options.Scenario == Scenario.HotAccount ? 0.45 : 0.3;
        var type = Random.Shared.NextDouble() < debitShare ? "debit" : "credit";
        var amount = Random.Shared.Next(1, 10_000) / 100m;
        return new { type, amount, currency = "BRL" };
    }

    private async Task PostEntryAsync(Guid accountId, string key, object body, string operation)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{accountId}/entries")
            {
                Content = JsonContent.Create(body, options: Json),
            };
            request.Headers.Add("Idempotency-Key", key);

            var (outcome, response) = await SendAsync(operation, request);
            using (response)
            {
                if (outcome is Outcome.Timeout or Outcome.TransportError && attempt == 1)
                {
                    // Resposta perdida: o cliente repete com a mesma chave.
                    Interlocked.Increment(ref _timeoutRetries);
                    continue;
                }

                if (outcome == Outcome.Success && response is not null)
                {
                    await RecordAcceptedAsync(accountId, key, body, response);
                }

                return;
            }
        }
    }

    private async Task RecordAcceptedAsync(Guid accountId, string key, object body, HttpResponseMessage response)
    {
        using var entry = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = entry.RootElement;
        var entryId = root.GetProperty("id").GetGuid();
        var amount = root.GetProperty("amount").GetDecimal();
        var signed = root.GetProperty("type").GetString() == "debit" ? -amount : amount;
        var accepted = new AcceptedEntry(accountId, key, entryId, signed, body);

        var stored = _accepted.GetOrAdd($"{accountId}:{key}", accepted);
        if (stored.EntryId != entryId)
        {
            _anomalies.Add($"Chave {key} devolveu lançamentos diferentes ({stored.EntryId} e {entryId}).");
        }
        else if (!ReferenceEquals(stored, accepted))
        {
            Interlocked.Increment(ref _replaysVerified);
        }
    }

    private async Task GetAsync(string operation, Uri uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var (_, response) = await SendAsync(operation, request);
        response?.Dispose();
    }

    private async Task<(Outcome Outcome, HttpResponseMessage? Response)> SendAsync(string operation, HttpRequestMessage request)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await _http.SendAsync(request);
            var outcome = (int)response.StatusCode switch
            {
                < 400 => Outcome.Success,
                422 => Outcome.BusinessRejection,
                < 500 => Outcome.UnexpectedClientError,
                _ => Outcome.ServerError,
            };

            if (outcome == Outcome.UnexpectedClientError)
            {
                _anomalies.Add($"{operation}: HTTP {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            }

            _samples.Add(new Sample(operation, clock.Elapsed.TotalMilliseconds, outcome));
            return (outcome, response);
        }
        catch (TaskCanceledException)
        {
            _samples.Add(new Sample(operation, clock.Elapsed.TotalMilliseconds, Outcome.Timeout));
            return (Outcome.Timeout, null);
        }
        catch (HttpRequestException)
        {
            _samples.Add(new Sample(operation, clock.Elapsed.TotalMilliseconds, Outcome.TransportError));
            return (Outcome.TransportError, null);
        }
    }

    private static Uri DailyBalanceUri(Guid accountId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new Uri($"/api/v1/accounts/{accountId}/daily-balances?from={from}&to={to}", UriKind.Relative);
    }

    private async Task<Verification> VerifyAsync()
    {
        var verification = new Verification { AcceptedEntries = _accepted.Count };

        // 1. Saldo atual (API) = saldo inicial + lançamentos aceitos.
        foreach (var account in _accounts)
        {
            var expected = account.InitialBalance + _accepted.Values.Where(e => e.AccountId == account.Id).Sum(e => e.SignedAmount);
            using var balance = JsonDocument.Parse(await _http.GetStringAsync(new Uri($"/api/v1/accounts/{account.Id}/balance", UriKind.Relative)));
            var actual = balance.RootElement.GetProperty("balance").GetDecimal();
            if (actual != expected)
            {
                verification.BalanceMismatches.Add($"{account.Id}: esperado {expected:F2}, API {actual:F2}");
            }
        }

        // 2. Razão (banco): todo lançamento confirmado ao cliente existe uma única vez.
        if (options.ConnectionString is { } connectionString)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT id FROM mybank.ledger_entries WHERE account_id = ANY(@accounts)", connection);
            command.Parameters.AddWithValue("accounts", _accounts.Select(a => a.Id).ToArray());

            var ledgerIds = new HashSet<Guid>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    ledgerIds.Add(reader.GetGuid(0));
                }
            }

            var acceptedIds = _accepted.Values.Select(e => e.EntryId).ToHashSet();
            verification.LedgerChecked = true;
            verification.LedgerEntries = ledgerIds.Count;
            verification.Lost = acceptedIds.Count(id => !ledgerIds.Contains(id));
            verification.DuplicatedAcceptances = _accepted.Count - acceptedIds.Count;
            verification.ConfirmedWithoutResponse = ledgerIds.Count(id => !acceptedIds.Contains(id));
        }

        // 3. Consolidação diária converge para o saldo do razão.
        verification.ProjectionConvergence = await WaitForProjectionAsync(TimeSpan.FromSeconds(120));
        return verification;
    }

    private async Task<TimeSpan?> WaitForProjectionAsync(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            var allUpToDate = true;
            foreach (var account in _accounts)
            {
                using var daily = JsonDocument.Parse(await _http.GetStringAsync(DailyBalanceUri(account.Id)));
                var projection = daily.RootElement.GetProperty("projection");
                if (!projection.GetProperty("upToDate").GetBoolean())
                {
                    allUpToDate = false;
                    break;
                }

                var days = daily.RootElement.GetProperty("days");
                using var balance = JsonDocument.Parse(await _http.GetStringAsync(new Uri($"/api/v1/accounts/{account.Id}/balance", UriKind.Relative)));
                var current = balance.RootElement.GetProperty("balance").GetDecimal();
                var closing = days.GetArrayLength() == 0 ? account.InitialBalance : days[days.GetArrayLength() - 1].GetProperty("closingBalance").GetDecimal();
                if (closing != current)
                {
                    _anomalies.Add($"Projeção da conta {account.Id} em dia, mas fechamento {closing:F2} difere do saldo {current:F2}.");
                    return null;
                }
            }

            if (allUpToDate)
            {
                return clock.Elapsed;
            }

            await Task.Delay(500);
        }

        return null;
    }
}

internal sealed class Verification
{
    public int AcceptedEntries { get; init; }

    public List<string> BalanceMismatches { get; } = [];

    public bool LedgerChecked { get; set; }

    public int LedgerEntries { get; set; }

    public int Lost { get; set; }

    public int DuplicatedAcceptances { get; set; }

    /// <summary>Lançamentos confirmados cuja resposta o cliente não recebeu (timeout sem retry bem-sucedido).</summary>
    public int ConfirmedWithoutResponse { get; set; }

    public TimeSpan? ProjectionConvergence { get; set; }
}

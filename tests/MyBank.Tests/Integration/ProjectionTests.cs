using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MyBank.Api.Contracts;
using MyBank.Domain.Enums;

namespace MyBank.Tests.Integration;

[Collection(SharedApi.Name)]
public sealed class ProjectionTests(ApiFixture api)
{
    [Fact]
    public async Task Workers_concorrentes_consolidam_cada_lancamento_exatamente_uma_vez()
    {
        // 4 contas com 30 lançamentos cada; 4 workers simultâneos em lotes pequenos, que
        // chegam fora de ordem para a mesma conta.
        var accounts = new List<(Guid Id, HttpClient Client)>();
        for (var a = 0; a < 4; a++)
        {
            var owner = $"proj-{a}";
            accounts.Add((await api.CreateAccountAsync(owner, 1000.00m), await api.ClientForAsync(owner)));
        }

        foreach (var (id, client) in accounts)
        {
            for (var i = 0; i < 30; i++)
            {
                var response = await client.PostEntryAsync(id, $"k{i}", i % 3 == 0 ? "debit" : "credit", 7.35m + i);
                response.EnsureSuccessStatusCode();
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => api.RunWorkerUntilIdleAsync(batchSize: 7)));

        foreach (var (id, client) in accounts)
        {
            var ledger = await api.QueryDbAsync(db => db.LedgerEntries.Where(e => e.AccountId == id).ToListAsync());
            var days = await api.QueryDbAsync(db => db.DailyBalances.Where(d => d.AccountId == id).OrderBy(d => d.Date).ToListAsync());

            Assert.Equal(30, days.Sum(d => d.EntryCount));
            Assert.Equal(ledger.Where(e => e.Type == EntryType.Credit).Sum(e => e.Amount), days.Sum(d => d.TotalCredits));
            Assert.Equal(ledger.Where(e => e.Type == EntryType.Debit).Sum(e => e.Amount), days.Sum(d => d.TotalDebits));
            Assert.Equal(1000.00m, days[0].OpeningBalance);
            Assert.Equal(await client.GetBalanceAsync(id), days[^1].ClosingBalance);
            client.Dispose();
        }
    }

    [Fact]
    public async Task Consolidacao_pode_ser_reconstruida_do_zero_a_partir_da_outbox()
    {
        var account = await api.CreateAccountAsync("rebuild", 100.00m);
        using var client = await api.ClientForAsync("rebuild");
        await client.PostEntryAsync(account, "k1", "credit", 40m);
        await client.PostEntryAsync(account, "k2", "debit", 15m);
        await api.RunWorkerUntilIdleAsync();
        var before = await api.QueryDbAsync(db => db.DailyBalances.AsNoTracking().SingleAsync(d => d.AccountId == account));

        // Procedimento documentado em Architecture.md (Operação): o razão nunca é alterado.
        await api.QueryDbAsync(db => db.Database.ExecuteSqlRawAsync("""
            BEGIN;
            TRUNCATE mybank.daily_balances;
            UPDATE mybank.outbox_messages SET processed_at = NULL;
            COMMIT;
            """));
        await api.RunWorkerUntilIdleAsync();
        var after = await api.QueryDbAsync(db => db.DailyBalances.AsNoTracking().SingleAsync(d => d.AccountId == account));

        Assert.Equal(
            (before.OpeningBalance, before.TotalCredits, before.TotalDebits, before.ClosingBalance, before.EntryCount),
            (after.OpeningBalance, after.TotalCredits, after.TotalDebits, after.ClosingBalance, after.EntryCount));
        Assert.Equal(125.00m, after.ClosingBalance);
    }

    [Fact]
    public async Task Health_da_consolidacao_mostra_eventos_pendentes()
    {
        var account = await api.CreateAccountAsync("health", 100.00m);
        using var client = await api.ClientForAsync("health");
        await client.PostEntryAsync(account, "k1", "credit", 1m);

        var pending = await client.GetFromJsonAsync<Dictionary<string, object>>("/health/projection");
        await api.RunWorkerUntilIdleAsync();
        var drained = await client.GetFromJsonAsync<Dictionary<string, object>>("/health/projection");

        Assert.NotEqual("0", pending!["pendingEvents"].ToString());
        Assert.Equal("0", drained!["pendingEvents"].ToString());
        Assert.Equal("Healthy", drained["status"].ToString());
    }

    [Fact]
    public async Task Consulta_da_consolidacao_valida_o_periodo()
    {
        var account = await api.CreateAccountAsync("range", 100.00m);
        using var client = await api.ClientForAsync("range");

        var inverted = await client.GetAsync(new Uri($"/api/v1/accounts/{account}/daily-balances?from=2026-02-01&to=2026-01-01", UriKind.Relative));
        var tooLong = await client.GetAsync(new Uri($"/api/v1/accounts/{account}/daily-balances?from=2024-01-01&to=2026-01-01", UriKind.Relative));

        Assert.Equal("invalid_date_range", await inverted.ErrorCodeAsync());
        Assert.Equal("invalid_date_range", await tooLong.ErrorCodeAsync());
    }

    [Fact]
    public async Task Consolidacao_sem_lancamentos_retorna_lista_vazia_em_dia()
    {
        var account = await api.CreateAccountAsync("empty", 100.00m);
        using var client = await api.ClientForAsync("empty");

        var result = await client.GetFromJsonAsync<DailyBalancesResponse>($"/api/v1/accounts/{account}/daily-balances?from=2026-01-01&to=2026-12-31");

        Assert.Empty(result!.Days);
        Assert.True(result.Projection.UpToDate);
    }
}

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using MyBank.Api;

namespace MyBank.Tests.Integration;

/// <summary>
/// Um teste por critério de aceite da demanda (numeração igual à de docs/Arquitetura.md).
/// O critério 10 (carga) é coberto por tools/MyBank.LoadTests e o 11 (build sem warnings + CI) pelo pipeline.
/// </summary>
[Collection(SharedApi.Name)]
public sealed class AcceptanceCriteriaTests(ApiFixture api)
{
    [Fact]
    public async Task C01_Credito_aumenta_o_saldo_exatamente_pelo_valor()
    {
        var account = await api.CreateAccountAsync("c01", 100.00m);
        using var client = await api.ClientForAsync("c01");

        var response = await client.PostEntryAsync(account, "k1", "credit", 50.25m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<EntryResponse>();
        Assert.Equal(150.25m, entry!.BalanceAfter);
        Assert.Equal(150.25m, await client.GetBalanceAsync(account));
        Assert.Equal(1, await api.CountEntriesAsync(account));
    }

    [Fact]
    public async Task C02_Debito_reduz_o_saldo_exatamente_pelo_valor()
    {
        var account = await api.CreateAccountAsync("c02", 100.00m);
        using var client = await api.ClientForAsync("c02");

        var response = await client.PostEntryAsync(account, "k1", "debit", 30.10m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(69.90m, await client.GetBalanceAsync(account));
    }

    [Theory]
    [InlineData("credit", "0", "BRL", "invalid_amount")]
    [InlineData("credit", "-5", "BRL", "invalid_amount")]
    [InlineData("credit", "10.001", "BRL", "invalid_scale")]
    [InlineData("credit", "10", "USD", "unsupported_currency")]
    [InlineData("transfer", "10", "BRL", "invalid_entry_type")]
    public async Task C03_Entrada_invalida_e_rejeitada_sem_gravar_nada(string type, string amount, string currency, string expectedCode)
    {
        var account = await api.CreateAccountAsync("c03", 100.00m);
        using var client = await api.ClientForAsync("c03");

        var body = new CreateEntryRequest(type, decimal.Parse(amount, CultureInfo.InvariantCulture), currency, null);
        var response = await client.PostEntryAsync(account, "k1", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, await response.ErrorCodeAsync());
        Assert.Equal(0, await api.CountEntriesAsync(account));
        Assert.Equal(100.00m, await client.GetBalanceAsync(account));
    }

    [Fact]
    public async Task C03_Conta_inexistente_e_chave_ausente_sao_rejeitadas()
    {
        var account = await api.CreateAccountAsync("c03b", 100.00m);
        using var client = await api.ClientForAsync("c03b");

        var unknownAccount = await client.PostEntryAsync(Guid.NewGuid(), "k1", "credit", 10m);
        var missingKey = await client.PostEntryAsync(account, null, new CreateEntryRequest("credit", 10m, "BRL", null));

        Assert.Equal(HttpStatusCode.NotFound, unknownAccount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        Assert.Equal("invalid_idempotency_key", await missingKey.ErrorCodeAsync());
        Assert.Equal(0, await api.CountEntriesAsync(account));
    }

    [Fact]
    public async Task C04_Debito_acima_do_saldo_e_rejeitado_sem_alterar_nada()
    {
        var account = await api.CreateAccountAsync("c04", 100.00m);
        using var client = await api.ClientForAsync("c04");

        var response = await client.PostEntryAsync(account, "k1", "debit", 100.01m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("insufficient_funds", await response.ErrorCodeAsync());
        Assert.Equal(100.00m, await client.GetBalanceAsync(account));
        Assert.Equal(0, await api.CountEntriesAsync(account));
    }

    [Fact]
    public async Task C05_Debitos_concorrentes_nunca_deixam_o_saldo_negativo()
    {
        var account = await api.CreateAccountAsync("c05", 200.00m);
        using var client = await api.ClientForAsync("c05");

        // 50 débitos de 10 ao mesmo tempo contra saldo 200: exatamente 20 cabem.
        var responses = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(i => client.PostEntryAsync(account, $"k{i}", "debit", 10m)));

        Assert.Equal(20, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(30, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0.00m, await client.GetBalanceAsync(account));
        Assert.Equal(20, await api.CountEntriesAsync(account));
    }

    [Fact]
    public async Task C06_Mesma_chave_e_mesmo_conteudo_devolvem_o_mesmo_lancamento()
    {
        var account = await api.CreateAccountAsync("c06", 100.00m);
        using var client = await api.ClientForAsync("c06");

        var first = await client.PostEntryAsync(account, "pedido-1", "credit", 10m);
        var retry = await client.PostEntryAsync(account, "pedido-1", "credit", 10.00m);

        // Repetições simultâneas da mesma chave também geram um único lançamento.
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => client.PostEntryAsync(account, "pedido-2", "credit", 5m)));

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.True(retry.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<EntryResponse>())!.Id,
            (await retry.Content.ReadFromJsonAsync<EntryResponse>())!.Id);
        Assert.All(concurrent, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(2, await api.CountEntriesAsync(account));
        Assert.Equal(115.00m, await client.GetBalanceAsync(account));
    }

    [Fact]
    public async Task C07_Mesma_chave_com_outro_conteudo_retorna_conflito()
    {
        var account = await api.CreateAccountAsync("c07", 100.00m);
        using var client = await api.ClientForAsync("c07");

        await client.PostEntryAsync(account, "pedido-1", "credit", 10m);
        var response = await client.PostEntryAsync(account, "pedido-1", "credit", 99m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("idempotency_key_reused", await response.ErrorCodeAsync());
        Assert.Equal(1, await api.CountEntriesAsync(account));
        Assert.Equal(110.00m, await client.GetBalanceAsync(account));
    }

    [Fact]
    public async Task C08_Worker_parado_nao_impede_lancamentos_e_a_consolidacao_alcanca_o_razao()
    {
        var account = await api.CreateAccountAsync("c08", 100.00m);
        using var client = await api.ClientForAsync("c08");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var url = $"/api/v1/accounts/{account}/daily-balances?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}";

        // Worker parado: os lançamentos são aceitos e a consolidação informa o atraso.
        Assert.Equal(HttpStatusCode.Created, (await client.PostEntryAsync(account, "k1", "credit", 50m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostEntryAsync(account, "k2", "debit", 20m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostEntryAsync(account, "k3", "credit", 5m)).StatusCode);
        var stale = await client.GetFromJsonAsync<DailyBalancesResponse>(url);
        Assert.Equal(3, stale!.Projection.PendingEntries);
        Assert.False(stale.Projection.UpToDate);

        // Worker volta: a consolidação alcança o razão. Rodar de novo não duplica nada.
        await api.RunWorkerUntilIdleAsync();
        await api.RunWorkerUntilIdleAsync();
        var current = await client.GetFromJsonAsync<DailyBalancesResponse>(url);

        Assert.True(current!.Projection.UpToDate);
        var day = Assert.Single(current.Days);
        Assert.Equal(100.00m, day.OpeningBalance);
        Assert.Equal(55.00m, day.TotalCredits);
        Assert.Equal(20.00m, day.TotalDebits);
        Assert.Equal(3, day.EntryCount);
        Assert.Equal(await client.GetBalanceAsync(account), day.ClosingBalance);
    }

    [Fact]
    public async Task C09_Saldo_historico_reflete_exatamente_os_lancamentos_ate_o_instante()
    {
        var account = await api.CreateAccountAsync("c09", 100.00m);
        using var client = await api.ClientForAsync("c09");

        var credit = await (await client.PostEntryAsync(account, "k1", "credit", 50m)).Content.ReadFromJsonAsync<EntryResponse>();
        var debit = await (await client.PostEntryAsync(account, "k2", "debit", 30m)).Content.ReadFromJsonAsync<EntryResponse>();
        static string Iso(DateTimeOffset instant) => instant.ToString("O", CultureInfo.InvariantCulture);

        // Não depende da consolidação diária (o worker não rodou).
        Assert.Equal(100.00m, await client.GetBalanceAsync(account, Iso(credit!.RecordedAt.AddMicroseconds(-1))));
        Assert.Equal(150.00m, await client.GetBalanceAsync(account, Iso(credit.RecordedAt)));
        Assert.Equal(150.00m, await client.GetBalanceAsync(account, Iso(debit!.RecordedAt.AddMicroseconds(-1))));
        Assert.Equal(120.00m, await client.GetBalanceAsync(account, Iso(debit.RecordedAt)));
        Assert.Equal(120.00m, await client.GetBalanceAsync(account, Iso(debit.RecordedAt.AddDays(1))));
    }
}

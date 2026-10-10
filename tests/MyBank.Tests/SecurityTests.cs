using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MyBank.Api;

namespace MyBank.Tests;

[Collection(SharedApi.Name)]
public sealed class SecurityTests(ApiFixture api)
{
    [Fact]
    public async Task Sem_token_retorna_401()
    {
        using var anonymous = api.CreateClient();

        var response = await anonymous.GetAsync(new Uri($"/api/v1/accounts/{Guid.NewGuid()}/balance", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Conta_de_outro_titular_retorna_403_em_leitura_e_escrita()
    {
        var account = await api.CreateAccountAsync("alice", 100.00m);
        using var mallory = await api.ClientForAsync("mallory");

        var read = await mallory.GetAsync(new Uri($"/api/v1/accounts/{account}/balance", UriKind.Relative));
        var write = await mallory.PostEntryAsync(account, "k1", "debit", 10m);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(0, await api.CountEntriesAsync(account));
    }

    [Fact]
    public async Task Abrir_conta_exige_papel_de_administrador()
    {
        using var client = await api.ClientForAsync("alice");

        var response = await client.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest("alice", 1_000_000m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Abertura_de_conta_valida_titular_e_saldo_inicial()
    {
        using var admin = await api.ClientForAsync("admin", "accounts-admin");

        var noOwner = await admin.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest(" ", 10m));
        var negative = await admin.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest("bob", -1m));
        var tooLarge = await admin.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest("bob", 1e15m));

        Assert.Equal("invalid_owner", await noOwner.ErrorCodeAsync());
        Assert.Equal("invalid_amount", await negative.ErrorCodeAsync());
        Assert.Equal("invalid_amount", await tooLarge.ErrorCodeAsync());
    }

    [Fact]
    public async Task Cliente_nao_consegue_informar_saldo_nem_instante_do_lancamento()
    {
        var account = await api.CreateAccountAsync("strict", 100.00m);
        using var client = await api.ClientForAsync("strict");

        var body = new { type = "credit", amount = 10m, currency = "BRL", balanceAfter = 1_000_000m, recordedAt = "2020-01-01T00:00:00Z" };
        var response = await client.PostEntryAsync(account, "k1", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await api.CountEntriesAsync(account));
    }

    [Fact]
    public async Task Instante_sem_fuso_e_rejeitado()
    {
        var account = await api.CreateAccountAsync("tz", 100.00m);
        using var client = await api.ClientForAsync("tz");

        var response = await client.GetAsync(new Uri($"/api/v1/accounts/{account}/balance?at=2026-10-09T12:00:00", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_instant", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Razao_e_imutavel_no_proprio_banco()
    {
        var account = await api.CreateAccountAsync("immutable", 100.00m);
        using var client = await api.ClientForAsync("immutable");
        await client.PostEntryAsync(account, "k1", "credit", 10m);

        var update = await Record.ExceptionAsync(() => api.QueryDbAsync(db =>
            db.LedgerEntries.Where(e => e.AccountId == account).ExecuteUpdateAsync(s => s.SetProperty(e => e.Amount, 999m))));
        var delete = await Record.ExceptionAsync(() => api.QueryDbAsync(db =>
            db.LedgerEntries.Where(e => e.AccountId == account).ExecuteDeleteAsync()));

        Assert.Contains("imutável", update?.Message, StringComparison.Ordinal);
        Assert.Contains("imutável", delete?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_checks_nao_exigem_token()
    {
        using var anonymous = api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(new Uri("/health/live", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode);
    }
}

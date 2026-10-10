using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyBank.Api.Contracts;
using MyBank.Core;
using Testcontainers.PostgreSql;

namespace MyBank.Tests.Integration;

/// <summary>
/// API completa (Development) sobre um PostgreSQL real e descartável. Locks, transações e
/// constraints só são demonstrados de verdade no banco real. Cada teste cria suas próprias contas.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Services; // sobe a API, que aplica as migrations em Development
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{MyBankDbContext.ConnectionStringName}", _postgres.GetConnectionString());
    }

    public async Task<HttpClient> ClientForAsync(string subject, params string[] roles)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/dev/token", new DevTokenRequest(subject, roles));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<DevTokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    public async Task<Guid> CreateAccountAsync(string owner, decimal initialBalance)
    {
        using var admin = await ClientForAsync("admin", "accounts-admin");
        var response = await admin.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest(owner, initialBalance));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!.Id;
    }

    /// <summary>Executa o mesmo código do worker até a outbox esvaziar.</summary>
    public async Task RunWorkerUntilIdleAsync(int batchSize = 500)
    {
        while (true)
        {
            using var scope = Services.CreateScope();
            var projector = new DailyBalanceProjector(scope.ServiceProvider.GetRequiredService<MyBankDbContext>());
            if (await projector.ProcessBatchAsync(batchSize, CancellationToken.None) == 0)
            {
                return;
            }
        }
    }

    public async Task<T> QueryDbAsync<T>(Func<MyBankDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<MyBankDbContext>());
    }

    public Task<int> CountEntriesAsync(Guid accountId) =>
        QueryDbAsync(db => db.LedgerEntries.CountAsync(e => e.AccountId == accountId));
}

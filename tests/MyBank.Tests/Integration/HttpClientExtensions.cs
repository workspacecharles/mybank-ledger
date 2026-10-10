using System.Net.Http.Json;
using MyBank.Api.Contracts;

namespace MyBank.Tests.Integration;

internal static class HttpClientExtensions
{
    public static Task<HttpResponseMessage> PostEntryAsync(
        this HttpClient client,
        Guid accountId,
        string? key,
        object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{accountId}/entries")
        {
            Content = JsonContent.Create(body),
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PostEntryAsync(this HttpClient client, Guid accountId, string key, string type, decimal amount) =>
        client.PostEntryAsync(accountId, key, new CreateEntryRequest(type, amount, "BRL", null));

    public static async Task<decimal> GetBalanceAsync(this HttpClient client, Guid accountId, string? at = null)
    {
        var url = $"/api/v1/accounts/{accountId}/balance" + (at is null ? string.Empty : $"?at={Uri.EscapeDataString(at)}");
        var response = await client.GetFromJsonAsync<BalanceResponse>(url);
        return response!.Balance;
    }

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem?.GetValueOrDefault("code")?.ToString();
    }
}

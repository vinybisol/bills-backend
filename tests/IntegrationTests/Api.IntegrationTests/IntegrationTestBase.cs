using System.Diagnostics.CodeAnalysis;
using Api.IntegrationTests.Factories;

namespace Api.IntegrationTests;

[ExcludeFromCodeCoverage]
public class IntegrationTestBase : IAsyncLifetime
{
    private CustomWebApplicationFactory _factory = null!;
    internal HttpClient Client => CreateClient(null);
    internal HttpClient ClientWithoutToken => CreateClientUnauthorized();
    internal HttpClient ClientWithUid(string uid) => CreateClient(uid);

    public async ValueTask InitializeAsync()
    {
        _factory = new CustomWebApplicationFactory();
        await _factory.InitializeAsync();
    }

    public async Task ResetDatabase()
        => await _factory.ResetDatabaseAsync();

    public async ValueTask DisposeAsync()
    {
        await _factory.FinalizeAsync();
    }

    private HttpClient CreateClient(string? uid)
    {
        var client = _factory.CreateClient();
        var firebaseId = uid ?? "firebase-bill-123";
        var email = uid is null ? $"{uid}@example.com" : "user@example.com";
        var name = uid ?? "Test User";
        var token = TestTokens.CreateValidToken(firebaseId, email, name);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return client;
    }

    private HttpClient CreateClientUnauthorized()
    {
        var client = _factory.CreateClient();
        return client;
    }
}
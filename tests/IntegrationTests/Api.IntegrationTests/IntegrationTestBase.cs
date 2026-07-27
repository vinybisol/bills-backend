using System.Diagnostics.CodeAnalysis;
using Api.IntegrationTests.Factories;

namespace Api.IntegrationTests;

[ExcludeFromCodeCoverage]
public class IntegrationTestBase : IAsyncLifetime
{
    private CustomWebApplicationFactory _factory = null!;
    internal HttpClient Client => CreateClient();
    internal HttpClient ClientWithoutToken => CreateClientUnauthorized();

    public async ValueTask InitializeAsync()
    {
        _factory = new CustomWebApplicationFactory();
        await _factory.InitializeAsync();
        await _factory.ResetDatabaseAsync();

    }

    public async ValueTask DisposeAsync()
    {
        await _factory.FinalizeAsync();
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        var firebaseId = "firebase-bill-123";
        var token = TestTokens.CreateValidToken(firebaseId);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return client;
    }
    private HttpClient CreateClientUnauthorized()
    {
        var client = _factory.CreateClient();
        return client;
    }
}
using System.Net.Http.Headers;
using Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;

namespace Api.IntegrationTests.Infrastructure;

/// <summary>
/// Base fixture for endpoint integration tests. Hosts the API once per fixture against the
/// real test database (Neon, <c>bills_test</c>) and resets the data <b>once</b>, at fixture
/// start.
/// </summary>
/// <remarks>
/// Per-test isolation is achieved by giving each test a distinct Firebase uid — and therefore
/// a distinct <c>owner_id</c> — so the global owner query filter keeps each test's data
/// separate without resetting the database between tests. Resetting once per fixture (instead
/// of once per test) removes dozens of round-trips to the remote database and keeps the suite
/// fast. Derived fixtures must keep using a unique uid per test; they must <b>not</b> add a
/// <c>[SetUp]</c> that resets the database.
/// </remarks>
public abstract class IntegrationTestBase
{
    /// <summary>The in-memory host wired to the real test database.</summary>
    protected CustomWebApplicationFactory Factory { get; private set; } = null!;

    /// <summary>An unauthenticated <see cref="HttpClient"/> bound to the hosted API.</summary>
    protected HttpClient Client { get; private set; } = null!;

    private Respawner _respawner = null!;
    private NpgsqlConnection _dbConnection = null!;

    [OneTimeSetUp]
    public async Task BaseOneTimeSetUp()
    {
        Factory = new CustomWebApplicationFactory();
        Client = Factory.CreateClient();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        _dbConnection = new NpgsqlConnection(Factory.TestConnectionString);
        await _dbConnection.OpenAsync();
        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = ["__EFMigrationsHistory"]
        });

        // Reset once per fixture (not per test) to clear leftovers from previous runs.
        // Within a fixture, each test isolates its data through a unique uid/owner.
        await _respawner.ResetAsync(_dbConnection);
    }

    /// <summary>
    /// Creates a unique Firebase uid, so each test gets its own <c>app_user</c> / <c>owner_id</c>.
    /// </summary>
    protected static string NewFirebaseUid() => $"firebase-{Guid.NewGuid():N}";

    /// <summary>
    /// Creates an <see cref="HttpClient"/> whose requests carry a valid bearer token for the
    /// given Firebase identity. Defaults to a fresh, unique uid (see <see cref="NewFirebaseUid"/>).
    /// </summary>
    /// <param name="firebaseUid">The Firebase uid; <see langword="null"/> generates a unique one.</param>
    /// <param name="email">
    /// The e-mail claim; <see langword="null"/> derives <c>{uid}@example.com</c>, keeping it unique
    /// (<c>app_user.email</c> has a unique index).
    /// </param>
    /// <param name="name">The name claim, or <see langword="null"/> to omit it.</param>
    /// <returns>A client the caller must dispose.</returns>
    protected HttpClient CreateAuthenticatedClient(
        string? firebaseUid = null,
        string? email = null,
        string? name = "Test User")
    {
        var uid = firebaseUid ?? NewFirebaseUid();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.CreateValidToken(uid, email ?? $"{uid}@example.com", name));
        return client;
    }

    [OneTimeTearDown]
    public async Task BaseOneTimeTearDown()
    {
        await _dbConnection.DisposeAsync();
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}

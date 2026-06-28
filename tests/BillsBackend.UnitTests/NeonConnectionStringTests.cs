using BillsBackend.Api.Data;
using Npgsql;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="NeonConnectionString"/>, covering URI-to-key-value conversion.
/// </summary>
[TestFixture]
public class NeonConnectionStringTests
{
    [Test]
    public void Normalize_NeonUri_ProducesKeyValueWithSslRequire()
    {
        const string uri =
            "postgresql://neon_owner:secretpw@ep-test-pooler.sa-east-1.aws.neon.tech/neondb?sslmode=require";

        var result = NeonConnectionString.Normalize(uri);

        var builder = new NpgsqlConnectionStringBuilder(result);
        Assert.That(builder.Host, Is.EqualTo("ep-test-pooler.sa-east-1.aws.neon.tech"));
        Assert.That(builder.Database, Is.EqualTo("neondb"));
        Assert.That(builder.Username, Is.EqualTo("neon_owner"));
        Assert.That(builder.Password, Is.EqualTo("secretpw"));
        Assert.That(builder.SslMode, Is.EqualTo(SslMode.Require));
    }

    [Test]
    public void Normalize_PostgresScheme_IsAlsoConverted()
    {
        const string uri = "postgres://user:pass@host.example.com:6543/db";

        var result = NeonConnectionString.Normalize(uri);

        var builder = new NpgsqlConnectionStringBuilder(result);
        Assert.That(builder.Host, Is.EqualTo("host.example.com"));
        Assert.That(builder.Port, Is.EqualTo(6543));
        Assert.That(builder.SslMode, Is.EqualTo(SslMode.Require));
    }

    [Test]
    public void Normalize_PercentEncodedCredentials_AreDecoded()
    {
        const string uri = "postgresql://us%40er:p%40ss@host/db";

        var result = NeonConnectionString.Normalize(uri);

        var builder = new NpgsqlConnectionStringBuilder(result);
        Assert.That(builder.Username, Is.EqualTo("us@er"));
        Assert.That(builder.Password, Is.EqualTo("p@ss"));
    }

    [Test]
    public void Normalize_KeyValueString_IsReturnedUnchanged()
    {
        const string keyValue = "Host=localhost;Database=bills;Username=postgres;Password=postgres";

        var result = NeonConnectionString.Normalize(keyValue);

        Assert.That(result, Is.EqualTo(keyValue));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Normalize_NullOrWhitespace_IsReturnedUnchanged(string? input)
    {
        Assert.That(NeonConnectionString.Normalize(input), Is.EqualTo(input));
    }
}

using Data.Contexts;
using Npgsql;

namespace Data.UnitTests.Contexts;

/// <remarks>All credentials below are fake, test-only values.</remarks>
[TestFixture]
public sealed class NeonConnectionStringTests
{
    private static NpgsqlConnectionStringBuilder Parse(string? connectionString) => new(connectionString);

    [Test]
    public void Normalize_NeonPostgresqlUri_ProducesKeyValueWithSslRequire()
    {
        // Arrange
        const string uri = "postgresql://neon_owner:secretpw@ep-test-pooler.sa-east-1.aws.neon.tech/neondb?sslmode=require";

        // Act
        var builder = Parse(NeonConnectionString.Normalize(uri));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(builder.Host, Is.EqualTo("ep-test-pooler.sa-east-1.aws.neon.tech"));
            Assert.That(builder.Port, Is.EqualTo(5432), "default port when the URI omits it");
            Assert.That(builder.Database, Is.EqualTo("neondb"));
            Assert.That(builder.Username, Is.EqualTo("neon_owner"));
            Assert.That(builder.Password, Is.EqualTo("secretpw"));
            Assert.That(builder.SslMode, Is.EqualTo(SslMode.Require));
        }
    }

    [TestCase("postgres://user:pass@host.example.com:6543/db")]
    [TestCase("POSTGRESQL://user:pass@host.example.com:6543/db")]
    public void Normalize_PostgresSchemeAnyCase_IsConvertedWithExplicitPort(string uri)
    {
        // Act
        var builder = Parse(NeonConnectionString.Normalize(uri));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(builder.Host, Is.EqualTo("host.example.com"));
            Assert.That(builder.Port, Is.EqualTo(6543));
            Assert.That(builder.Database, Is.EqualTo("db"));
            Assert.That(builder.SslMode, Is.EqualTo(SslMode.Require));
        }
    }

    [Test]
    public void Normalize_PercentEncodedCredentials_AreDecoded()
    {
        // Arrange
        const string uri = "postgresql://us%40er:p%40ss@host/db";

        // Act
        var builder = Parse(NeonConnectionString.Normalize(uri));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(builder.Username, Is.EqualTo("us@er"));
            Assert.That(builder.Password, Is.EqualTo("p@ss"));
        }
    }

    [Test]
    public void Normalize_UriWithoutPassword_LeavesPasswordEmpty()
    {
        // Arrange
        const string uri = "postgresql://only_user@host/db";

        // Act
        var builder = Parse(NeonConnectionString.Normalize(uri));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(builder.Username, Is.EqualTo("only_user"));
            Assert.That(builder.Password, Is.Null.Or.Empty);
        }
    }

    [Test]
    public void Normalize_KeyValueString_IsReturnedUnchanged()
    {
        // Arrange — local Postgres must not be forced into SSL Mode=Require
        const string keyValue = "Host=localhost;Database=bills;Username=postgres;Password=postgres";

        // Act
        var result = NeonConnectionString.Normalize(keyValue);

        // Assert
        Assert.That(result, Is.EqualTo(keyValue));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Normalize_NullOrWhitespace_IsReturnedUnchanged(string? input) =>
        Assert.That(NeonConnectionString.Normalize(input), Is.EqualTo(input));
}

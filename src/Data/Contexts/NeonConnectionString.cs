using Npgsql;

namespace Data.Contexts;

/// <summary>
/// Normalizes a PostgreSQL connection string into the key-value form expected by Npgsql.
/// </summary>
/// <remarks>
/// Neon's dashboard hands out URI-style connection strings
/// (<c>postgresql://user:pass@host/db?sslmode=require</c>), which Npgsql cannot parse.
/// This helper converts those URIs into the ADO.NET key-value format and enforces
/// <c>SSL Mode=Require</c>, while leaving already key-value strings untouched.
/// </remarks>
public static class NeonConnectionString
{
    /// <summary>
    /// Converts a URI-style PostgreSQL connection string to Npgsql key-value form.
    /// </summary>
    /// <param name="connectionString">The configured connection string, in either URI or key-value form.</param>
    /// <returns>
    /// An Npgsql-compatible key-value connection string. The input is returned unchanged when it is
    /// <see langword="null"/>, empty, or already in key-value form.
    /// </returns>
    public static string? Normalize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var isUri =
            connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
        if (!isUri)
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Require,
        };

        return builder.ConnectionString;
    }
}

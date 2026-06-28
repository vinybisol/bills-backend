using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BillsBackend.IntegrationTests;

/// <summary>
/// Mints and describes the JWTs used by the integration tests.
/// </summary>
/// <remarks>
/// The tokens mirror the shape of Firebase secure tokens (issuer, audience and the
/// <c>user_id</c>/<c>email</c> claims) but are signed with a local symmetric key so the
/// suite never depends on Firebase. The test host is configured to validate against this
/// same key in <see cref="CustomWebApplicationFactory"/>.
/// </remarks>
public static class TestTokens
{
    /// <summary>The project id used as both issuer suffix and audience in tests.</summary>
    public const string ProjectId = "bills-test";

    /// <summary>The issuer expected by the test host.</summary>
    public const string Issuer = $"https://securetoken.google.com/{ProjectId}";

    private const string SigningSecret = "integration-tests-signing-secret-key-0123456789";

    /// <summary>
    /// Gets the symmetric key the test host uses to validate tokens.
    /// </summary>
    public static SymmetricSecurityKey SigningKey { get; } =
        new(Encoding.UTF8.GetBytes(SigningSecret));

    /// <summary>
    /// Creates a token signed with the valid test key for the given Firebase identity.
    /// </summary>
    /// <param name="firebaseUid">The Firebase uid to embed.</param>
    /// <param name="email">The e-mail to embed, or <see langword="null"/> to omit it.</param>
    /// <returns>A signed, currently-valid JWT string.</returns>
    public static string CreateValidToken(string firebaseUid, string? email = "user@example.com")
    {
        var claims = new Dictionary<string, object>
        {
            ["user_id"] = firebaseUid,
            ["sub"] = firebaseUid,
        };
        if (email is not null)
        {
            claims["email"] = email;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ProjectId,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(30),
            Claims = claims,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// Creates a token signed with a key that the test host does not trust.
    /// </summary>
    /// <returns>A signed but invalid JWT string.</returns>
    public static string CreateTokenWithUntrustedSignature()
    {
        var wrongKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("a-completely-different-untrusted-secret-key-9876"));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ProjectId,
            Expires = DateTime.UtcNow.AddMinutes(30),
            Claims = new Dictionary<string, object> { ["user_id"] = "intruder" },
            SigningCredentials = new SigningCredentials(wrongKey, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

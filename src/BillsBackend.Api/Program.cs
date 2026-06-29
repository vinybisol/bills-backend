using BillsBackend.Api.Data;
using BillsBackend.Api.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration: Firebase (strongly-typed, validated on startup) ---
builder.Services
    .AddOptions<FirebaseAuthOptions>()
    .Bind(builder.Configuration.GetSection(FirebaseAuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// --- Database: PostgreSQL (Neon). Connection string is supplied via configuration
// (user-secrets locally, environment / GitHub Secrets in CI/CD) and must use the
// pooler endpoint with "SSL Mode=Require". ---
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(NeonConnectionString.Normalize(builder.Configuration.GetConnectionString("Neon"))));

// --- Identity services ---
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();

// --- Authentication: validate Firebase-issued JWTs ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Bind the JWT validation parameters to the configured Firebase project. Kept as a
// separate configuration step so tests can post-configure the handler with a local
// signing key without touching production wiring.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<FirebaseAuthOptions>>((jwt, firebase) =>
    {
        var firebaseOptions = firebase.Value;
        jwt.Authority = firebaseOptions.Issuer;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = firebaseOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = firebaseOptions.ProjectId,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Apply migrations on startup for relational providers so the deployed environment is
// self-bootstrapping. Skipped for the in-memory provider used by integration tests.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
}

app.UseAuthentication();
app.UseAuthorization();

// Authenticated liveness endpoint: resolves (and just-in-time provisions) the internal
// app_user from the Firebase token and returns its internal id.
app.MapGet("/health", async (
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    CancellationToken cancellationToken) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
    {
        return Results.Unauthorized();
    }

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), cancellationToken);
    return Results.Ok(new HealthResponse(appUser.Id, "healthy"));
})
.RequireAuthorization();

// Returns the logged-in user's internal profile, resolving (and just-in-time provisioning)
// the app_user from the Firebase token.
app.MapGet("/me", async (
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    CancellationToken cancellationToken) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
    {
        return Results.Unauthorized();
    }

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), cancellationToken);
    return Results.Ok(new MeResponse(appUser.Id, appUser.Name, appUser.Email));
})
.RequireAuthorization();

await app.RunAsync();

/// <summary>
/// The payload returned by the authenticated <c>GET /health</c> endpoint.
/// </summary>
/// <param name="UserId">The internal <c>app_user.id</c> resolved from the token.</param>
/// <param name="Status">A constant liveness indicator.</param>
internal sealed record HealthResponse(long UserId, string Status);

/// <summary>
/// The payload returned by the authenticated <c>GET /me</c> endpoint.
/// </summary>
/// <param name="Id">The internal <c>app_user.id</c> resolved from the token.</param>
/// <param name="Name">The user's display name; <see cref="string.Empty"/> when no name claim was present.</param>
/// <param name="Email">The user's e-mail address, or <see langword="null"/> when the token carries no e-mail claim.</param>
internal sealed record MeResponse(long Id, string Name, string? Email);

/// <summary>
/// Program entry-point marker, made discoverable so integration tests can host the API
/// through <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;

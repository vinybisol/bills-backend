using System.Text.Json.Serialization;
using BillsBackend.Api.Data;
using BillsBackend.Api.Endpoints;
using BillsBackend.Api.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Serialize enums as snake_case strings (e.g. IncomeKind.OneOff → "one_off") in all endpoints.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower)));

// --- Configuration: Firebase (strongly-typed, validated on startup) ---
builder.Services
    .AddOptions<FirebaseAuthOptions>()
    .Bind(builder.Configuration.GetSection(FirebaseAuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// --- Database: PostgreSQL (Neon). Connection string is supplied via configuration
// (user-secrets locally, environment / GitHub Secrets in CI/CD) and must use the
// pooler endpoint with "SSL Mode=Require". "App:UseProdConnection" lets a local launch
// profile point at the "NeonProd" connection string while staying in the Development
// environment (so user-secrets keep loading); see the "prod-data" launch profile.
var useProdConnection = builder.Configuration.GetValue<bool>("App:UseProdConnection");
var neonConnectionStringKey = useProdConnection ? "NeonProd" : "Neon";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(NeonConnectionString.Normalize(builder.Configuration.GetConnectionString(neonConnectionStringKey))));

// --- Identity services ---
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();
builder.Services.AddScoped<ICurrentOwner, CurrentOwner>();

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
builder.Services.AddOpenApi();
builder.Services.AddCors();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(builder => builder
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());

    // Never auto-migrate when pointed at the production connection string — schema
    // changes against prod go through the deploy pipeline only.
    if (!useProdConnection)
    {
        var appDbContext = app.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
        if (appDbContext.Database.IsRelational())
            appDbContext.Database.Migrate();
    }
}
app.UseAuthentication();
app.UseAuthorization();

// All endpoints are versioned under /api/v1. See docs/decisoes.md for the versioning decision.
var v1 = app.MapGroup("/api/v1").RequireAuthorization();

v1.MapUserEndpoints()
    .MapCategoryEndpoints()
    .MapPersonEndpoints()
    .MapIncomeEndpoints()
    .MapBillEndpoints()
    .MapProjectionEndpoints()
    .MapEntryEndpoints()
    .MapDashboardEndpoints()
    .MapReceivablesEndpoints();

await app.RunAsync();

public partial class Program;

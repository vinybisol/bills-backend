using System.Text.Json.Serialization;
using BillsBackend.Api.Endpoints;
using BillsBackend.Api.Identity;
using Microsoft.IdentityModel.Tokens;
using Application.DependencyInjection;
using Data.DependencyInjection;
using Domain.Abstractions.Filters;
using Api.Filters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Data.Contexts;
using Domain.Infrastructures;


var builder = WebApplication.CreateBuilder(args);

// Serialize enums as snake_case strings (e.g. IncomeKindEnum.OneOff → "one_off") in all endpoints.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower)));

// --- Configuration: Firebase (strongly-typed, validated on startup) ---
builder.Services
    .AddOptions<FirebaseAuthOptions>()
    .Bind(builder.Configuration.GetSection(FirebaseAuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// // --- Database: PostgreSQL (Neon). Connection string is supplied via configuration
// // (user-secrets locally, environment / GitHub Secrets in CI/CD) and must use the
// // pooler endpoint with "SSL Mode=Require". "App:UseProdConnection" lets a local launch
// // profile point at the "NeonProd" connection string while staying in the Development
// // environment (so user-secrets keep loading); see the "prod-data" launch profile.
// var useProdConnection = builder.Configuration.GetValue<bool>("App:UseProdConnection");
// var neonConnectionStringKey = useProdConnection ? "NeonProd" : "Neon";
// builder.Services.AddDbContext<AppDbContext>(options =>
//     options.UseNpgsql(NeonConnectionString.Normalize(builder.Configuration.GetConnectionString(neonConnectionStringKey))));

// --- Identity services ---
builder.Services.AddSingleton(TimeProvider.System);
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

var options = builder.Configuration.Get<AppOptions>()
    ?? throw new Exception();

RegisterApplications.Register(builder.Services);
RegisterData.Register(builder.Services, options);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(builder => builder
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());

    var migrationService = app.Services.CreateScope()
        .ServiceProvider.GetRequiredService<IMigrationService>();
    migrationService.RunMigration(options);
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

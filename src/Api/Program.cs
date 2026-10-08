using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using Application.DependencyInjection;
using Data.DependencyInjection;
using Domain.Abstractions.Filters;
using Api.Filters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Data.Contexts;
using Domain.Infrastructures;
using Api.Endpoints;
using Api.Identity;


var builder = WebApplication.CreateBuilder(args);

// Serialize enums as snake_case strings (e.g. IncomeKindEnum.OneOff → "one_off") in all endpoints.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower)));

// --- Errors: every error response is RFC 9457 ProblemDetails (application/problem+json) ---
// Applies to Results.Problem/ValidationProblem, the exception handler and status code pages.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    var request = ctx.HttpContext.Request;
    // RFC 9457: "instance" is a URI reference identifying this occurrence → the request path.
    ctx.ProblemDetails.Instance ??= $"{request.PathBase}{request.Path}";
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
});

// --- Configuration: Firebase (strongly-typed, validated on startup) ---
builder.Services
    .AddOptions<FirebaseAuthOptions>()
    .Bind(builder.Configuration.GetSection(FirebaseAuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<SharedPagesOptions>()
    .Bind(builder.Configuration.GetSection(SharedPagesOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

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

var options = builder.Configuration.Get<AppSettings>()
    ?? throw new Exception();

RegisterApplications.Register(builder.Services);
RegisterData.Register(builder.Services, options);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                "https://bills-261c7.web.app"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // somente se usar cookies/autenticação
    });
});

var app = builder.Build();

// Unhandled exceptions → 500 ProblemDetails (no stack trace). In Development the default
// developer exception page stays in place to aid debugging.
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();

// Empty-body 4xx/5xx (401 challenge, route 404, 405, 415, 400 from binding) → ProblemDetails.
// Must run before authentication so the JwtBearer challenge also gets a body.
app.UseStatusCodePages();

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
app.UseCors("Frontend");

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
    .MapReceivablesEndpoints()
    .MapSharedBillsEndpoint()
    .MapAccessLinkEndpoints();

await app.RunAsync();

[ExcludeFromDescription]
public partial class Program;

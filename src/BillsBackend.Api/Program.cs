using System.Text.Json.Serialization;
using BillsBackend.Api.Data;
using BillsBackend.Api.Domain;
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
// pooler endpoint with "SSL Mode=Require". ---
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(NeonConnectionString.Normalize(builder.Configuration.GetConnectionString("Neon"))));

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

// --- Category endpoints ---

app.MapPost("/categories", async (
    CreateCategoryRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var trimmedName = req.Name.Trim();
    if (await db.Categories.AnyAsync(c => c.Name == trimmedName, ct))
        return Results.Conflict("A category with that name already exists.");

    var category = Category.Create(appUser.Id, trimmedName, timeProvider.GetUtcNow());
    db.Categories.Add(category);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/categories/{category.Id}", new CategoryDto(category.Id, category.Name));
})
.RequireAuthorization();

app.MapGet("/categories", async (
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var categories = await db.Categories
        .OrderBy(c => c.Name)
        .Select(c => new CategoryDto(c.Id, c.Name))
        .ToListAsync(ct);

    return Results.Ok(categories);
})
.RequireAuthorization();

app.MapPut("/categories/{id:long}", async (
    long id,
    UpdateCategoryRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
    if (category is null)
        return Results.NotFound();

    var trimmedName = req.Name.Trim();
    if (category.Name != trimmedName &&
        await db.Categories.AnyAsync(c => c.Id != id && c.Name == trimmedName, ct))
        return Results.Conflict("A category with that name already exists.");

    category.Rename(trimmedName);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new CategoryDto(category.Id, category.Name));
})
.RequireAuthorization();

app.MapDelete("/categories/{id:long}", async (
    long id,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
    if (category is null)
        return Results.NotFound();

    category.Deactivate();
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
})
.RequireAuthorization();

// --- Person endpoints ---

app.MapPost("/persons", async (
    CreatePersonRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var person = Person.Create(appUser.Id, req.Name, timeProvider.GetUtcNow());
    db.Persons.Add(person);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/persons/{person.Id}", new PersonDto(person.Id, person.Name));
})
.RequireAuthorization();

app.MapGet("/persons", async (
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var persons = await db.Persons
        .OrderBy(p => p.Name)
        .Select(p => new PersonDto(p.Id, p.Name))
        .ToListAsync(ct);

    return Results.Ok(persons);
})
.RequireAuthorization();

app.MapPut("/persons/{id:long}", async (
    long id,
    UpdatePersonRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == id, ct);
    if (person is null)
        return Results.NotFound();

    person.Rename(req.Name);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new PersonDto(person.Id, person.Name));
})
.RequireAuthorization();

app.MapDelete("/persons/{id:long}", async (
    long id,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == id, ct);
    if (person is null)
        return Results.NotFound();

    person.Deactivate();
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
})
.RequireAuthorization();

// --- Income endpoints ---

app.MapPost("/incomes", async (
    CreateIncomeRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    if (req.DefaultAmount < 0)
        return Results.BadRequest("Default amount must be zero or greater.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var income = Income.Create(appUser.Id, req.Name, req.Kind, req.DefaultAmount, timeProvider.GetUtcNow());
    db.Incomes.Add(income);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/incomes/{income.Id}", new IncomeDto(income.Id, income.Name, income.Kind, income.DefaultAmount));
})
.RequireAuthorization();

app.MapGet("/incomes", async (
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var incomes = await db.Incomes
        .OrderBy(i => i.Name)
        .Select(i => new IncomeDto(i.Id, i.Name, i.Kind, i.DefaultAmount))
        .ToListAsync(ct);

    return Results.Ok(incomes);
})
.RequireAuthorization();

app.MapPut("/incomes/{id:long}", async (
    long id,
    UpdateIncomeRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    if (req.DefaultAmount < 0)
        return Results.BadRequest("Default amount must be zero or greater.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var income = await db.Incomes.FirstOrDefaultAsync(i => i.Id == id, ct);
    if (income is null)
        return Results.NotFound();

    income.Update(req.Name, req.Kind, req.DefaultAmount);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new IncomeDto(income.Id, income.Name, income.Kind, income.DefaultAmount));
})
.RequireAuthorization();

app.MapDelete("/incomes/{id:long}", async (
    long id,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var income = await db.Incomes.FirstOrDefaultAsync(i => i.Id == id, ct);
    if (income is null)
        return Results.NotFound();

    income.Deactivate();
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
})
.RequireAuthorization();

// --- Bill endpoints ---

app.MapPost("/bills", async (
    CreateBillRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    if (req.DefaultAmount < 0)
        return Results.BadRequest("Default amount must be zero or greater.");

    if (req.SplitRatio < 0m || req.SplitRatio > 1m)
        return Results.BadRequest("SplitRatio must be between 0 and 1.");

    if (req.SplitRatio < 1m && req.PersonId is null)
        return Results.BadRequest("PersonId is required when SplitRatio is less than 1.");

    if (req.SplitRatio == 1m && req.PersonId is not null)
        return Results.BadRequest("PersonId must be null when SplitRatio is 1.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
        return Results.NotFound("Category not found.");

    if (req.PersonId is not null && !await db.Persons.AnyAsync(p => p.Id == req.PersonId.Value, ct))
        return Results.NotFound("Person not found.");

    var bill = Bill.Create(appUser.Id, req.Name, req.CategoryId, req.Kind, req.DefaultAmount, req.SplitRatio, req.PersonId, timeProvider.GetUtcNow());
    db.Bills.Add(bill);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/bills/{bill.Id}", new BillDto(bill.Id, bill.Name, bill.CategoryId, bill.Kind, bill.DefaultAmount, bill.SplitRatio, bill.PersonId));
})
.RequireAuthorization();

app.MapGet("/bills", async (
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var bills = await db.Bills
        .OrderBy(b => b.Name)
        .Select(b => new BillDto(b.Id, b.Name, b.CategoryId, b.Kind, b.DefaultAmount, b.SplitRatio, b.PersonId))
        .ToListAsync(ct);

    return Results.Ok(bills);
})
.RequireAuthorization();

app.MapPut("/bills/{id:long}", async (
    long id,
    UpdateBillRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    if (req.DefaultAmount < 0)
        return Results.BadRequest("Default amount must be zero or greater.");

    if (req.SplitRatio < 0m || req.SplitRatio > 1m)
        return Results.BadRequest("SplitRatio must be between 0 and 1.");

    if (req.SplitRatio < 1m && req.PersonId is null)
        return Results.BadRequest("PersonId is required when SplitRatio is less than 1.");

    if (req.SplitRatio == 1m && req.PersonId is not null)
        return Results.BadRequest("PersonId must be null when SplitRatio is 1.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, ct);
    if (bill is null)
        return Results.NotFound();

    if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
        return Results.NotFound("Category not found.");

    if (req.PersonId is not null && !await db.Persons.AnyAsync(p => p.Id == req.PersonId.Value, ct))
        return Results.NotFound("Person not found.");

    bill.Update(req.Name, req.CategoryId, req.Kind, req.DefaultAmount, req.SplitRatio, req.PersonId);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new BillDto(bill.Id, bill.Name, bill.CategoryId, bill.Kind, bill.DefaultAmount, bill.SplitRatio, bill.PersonId));
})
.RequireAuthorization();

app.MapDelete("/bills/{id:long}", async (
    long id,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, ct);
    if (bill is null)
        return Results.NotFound();

    bill.Deactivate();
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
})
.RequireAuthorization();

// --- Projection endpoint ---

// Generates annual projected entries for every active recurring bill and income template
// owned by the authenticated user. Idempotent: existing entries (identified by bill/income
// id + year + month) are skipped, so calling the endpoint twice for the same year is safe.
app.MapPost("/api/projection/{year:int}", async (
    int year,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    if (year < 2000 || year > 2100)
        return Results.BadRequest("Year must be between 2000 and 2100.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    // Fetch only recurring active bill/income templates (global query filter applies active + owner_id).
    var recurringBills = await db.Bills
        .Where(b => b.Kind == BillKind.Recurring)
        .ToListAsync(ct);

    var recurringIncomes = await db.Incomes
        .Where(i => i.Kind == IncomeKind.Recurring)
        .ToListAsync(ct);

    // Fetch already-created entries for this year so subsequent calls are idempotent.
    var existingBillEntries = await db.BillEntries
        .Where(e => e.RefYear == year)
        .Select(e => new { e.BillId, e.RefMonth })
        .ToListAsync(ct);

    var existingIncomeEntries = await db.IncomeEntries
        .Where(e => e.RefYear == year)
        .Select(e => new { e.IncomeId, e.RefMonth })
        .ToListAsync(ct);

    // ValueTuple has structural equality, so Contains checks are O(1) with HashSet.
    var existingBillSet = existingBillEntries
        .Select(e => (e.BillId, e.RefMonth))
        .ToHashSet();

    var existingIncomeSet = existingIncomeEntries
        .Select(e => (e.IncomeId, e.RefMonth))
        .ToHashSet();

    var now = timeProvider.GetUtcNow();
    int billEntriesCreated = 0, incomeEntriesCreated = 0, skipped = 0;

    await using var tx = await db.Database.BeginTransactionAsync(ct);

    foreach (var bill in recurringBills)
    {
        for (int month = 1; month <= 12; month++)
        {
            if (existingBillSet.Contains((bill.Id, month))) { skipped++; continue; }
            db.BillEntries.Add(BillEntry.Create(appUser.Id, bill.Id, year, month, bill.DefaultAmount, bill.SplitRatio, bill.PersonId, now));
            billEntriesCreated++;
        }
    }

    foreach (var income in recurringIncomes)
    {
        for (int month = 1; month <= 12; month++)
        {
            if (existingIncomeSet.Contains((income.Id, month))) { skipped++; continue; }
            db.IncomeEntries.Add(IncomeEntry.Create(appUser.Id, income.Id, year, month, income.DefaultAmount, now));
            incomeEntriesCreated++;
        }
    }

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return Results.Ok(new ProjectionResult(year, billEntriesCreated, incomeEntriesCreated, skipped));
})
.RequireAuthorization();

await app.RunAsync();

/// <summary>The payload returned by the authenticated <c>GET /health</c> endpoint.</summary>
/// <param name="UserId">The internal <c>app_user.id</c> resolved from the token.</param>
/// <param name="Status">A constant liveness indicator.</param>
internal sealed record HealthResponse(long UserId, string Status);

/// <summary>The payload returned by the authenticated <c>GET /me</c> endpoint.</summary>
/// <param name="Id">The internal <c>app_user.id</c> resolved from the token.</param>
/// <param name="Name">The user's display name; <see cref="string.Empty"/> when no name claim was present.</param>
/// <param name="Email">The user's e-mail address, or <see langword="null"/> when the token carries no e-mail claim.</param>
internal sealed record MeResponse(long Id, string Name, string? Email);

/// <summary>The payload returned by category read operations.</summary>
/// <param name="Id">The internal category id.</param>
/// <param name="Name">The category display name.</param>
internal sealed record CategoryDto(long Id, string Name);

/// <summary>The request body for <c>POST /categories</c>.</summary>
/// <param name="Name">The desired category name.</param>
internal sealed record CreateCategoryRequest(string Name);

/// <summary>The request body for <c>PUT /categories/{id}</c>.</summary>
/// <param name="Name">The new name for the category.</param>
internal sealed record UpdateCategoryRequest(string Name);

/// <summary>The payload returned by person read operations.</summary>
/// <param name="Id">The internal person id.</param>
/// <param name="Name">The person's display name.</param>
internal sealed record PersonDto(long Id, string Name);

/// <summary>The request body for <c>POST /persons</c>.</summary>
/// <param name="Name">The desired person name.</param>
internal sealed record CreatePersonRequest(string Name);

/// <summary>The request body for <c>PUT /persons/{id}</c>.</summary>
/// <param name="Name">The new name for the person.</param>
internal sealed record UpdatePersonRequest(string Name);

/// <summary>The payload returned by income read operations.</summary>
/// <param name="Id">The internal income id.</param>
/// <param name="Name">The income template display name.</param>
/// <param name="Kind">The income kind.</param>
/// <param name="DefaultAmount">The default planned amount.</param>
internal sealed record IncomeDto(long Id, string Name, IncomeKind Kind, decimal DefaultAmount);

/// <summary>The request body for <c>POST /incomes</c>.</summary>
/// <param name="Name">The income template name.</param>
/// <param name="Kind">The income kind.</param>
/// <param name="DefaultAmount">The default planned amount; must be zero or greater.</param>
internal sealed record CreateIncomeRequest(string Name, IncomeKind Kind, decimal DefaultAmount);

/// <summary>The request body for <c>PUT /incomes/{id}</c>.</summary>
/// <param name="Name">The new income template name.</param>
/// <param name="Kind">The new income kind.</param>
/// <param name="DefaultAmount">The new default planned amount; must be zero or greater.</param>
internal sealed record UpdateIncomeRequest(string Name, IncomeKind Kind, decimal DefaultAmount);

/// <summary>The payload returned by bill read operations.</summary>
/// <param name="Id">The internal bill id.</param>
/// <param name="Name">The bill template display name.</param>
/// <param name="CategoryId">The category this bill belongs to.</param>
/// <param name="Kind">The bill kind.</param>
/// <param name="DefaultAmount">The default planned amount.</param>
/// <param name="SplitRatio">The owner's fraction of the expense (0 to 1).</param>
/// <param name="PersonId">The person who owes the remaining fraction, or <see langword="null"/> when SplitRatio is 1.</param>
internal sealed record BillDto(long Id, string Name, long CategoryId, BillKind Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);

/// <summary>The request body for <c>POST /bills</c>.</summary>
/// <param name="Name">The bill template name.</param>
/// <param name="CategoryId">The category this bill belongs to.</param>
/// <param name="Kind">The bill kind.</param>
/// <param name="DefaultAmount">The default planned amount; must be zero or greater.</param>
/// <param name="SplitRatio">The owner's fraction of the expense; must be in [0, 1].</param>
/// <param name="PersonId">Required when SplitRatio is less than 1; must be null when SplitRatio is 1.</param>
internal sealed record CreateBillRequest(string Name, long CategoryId, BillKind Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);

/// <summary>The request body for <c>PUT /bills/{id}</c>.</summary>
/// <param name="Name">The new bill template name.</param>
/// <param name="CategoryId">The new category.</param>
/// <param name="Kind">The new bill kind.</param>
/// <param name="DefaultAmount">The new default planned amount; must be zero or greater.</param>
/// <param name="SplitRatio">The new owner fraction; must be in [0, 1].</param>
/// <param name="PersonId">Required when SplitRatio is less than 1; must be null when SplitRatio is 1.</param>
internal sealed record UpdateBillRequest(string Name, long CategoryId, BillKind Kind, decimal DefaultAmount, decimal SplitRatio, long? PersonId);

/// <summary>The payload returned by <c>POST /api/projection/{year}</c>.</summary>
/// <param name="Year">The year for which the projection was generated.</param>
/// <param name="BillEntriesCreated">The number of new bill entries created.</param>
/// <param name="IncomeEntriesCreated">The number of new income entries created.</param>
/// <param name="Skipped">The number of entries that already existed and were skipped.</param>
internal sealed record ProjectionResult(int Year, int BillEntriesCreated, int IncomeEntriesCreated, int Skipped);

/// <summary>
/// Program entry-point marker, made discoverable so integration tests can host the API
/// through <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;

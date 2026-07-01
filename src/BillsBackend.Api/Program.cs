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

// Recalculates bill default amount and propagates to unpaid future entries.
// Paid entries in range are skipped (immutability); entries before fromMonth are untouched.
app.MapPost("/api/bills/{billId:long}/recalculate", async (
    long billId,
    RecalculateRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (req.FromMonth < 1 || req.FromMonth > 12)
        return Results.BadRequest("FromMonth must be between 1 and 12.");

    if (req.NewAmount < 0)
        return Results.BadRequest("NewAmount must be zero or greater.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == billId, ct);
    if (bill is null)
        return Results.NotFound();

    // Capture locals so EF Core can translate the predicate to SQL.
    var fromYear = req.FromYear;
    var fromMonth = req.FromMonth;

    var entriesInRange = await db.BillEntries
        .Where(e => e.BillId == billId &&
                    (e.RefYear > fromYear || (e.RefYear == fromYear && e.RefMonth >= fromMonth)))
        .ToListAsync(ct);

    await using var tx = await db.Database.BeginTransactionAsync(ct);

    bill.Recalculate(req.NewAmount);

    int updatedEntries = 0, skippedPaid = 0;
    foreach (var entry in entriesInRange)
    {
        if (entry.Paid)
        {
            skippedPaid++;
            continue;
        }
        entry.UpdatePlanned(req.NewAmount);
        updatedEntries++;
    }

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return Results.Ok(new RecalculateResponse(billId, updatedEntries, skippedPaid, req.NewAmount));
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

// --- Entries endpoint ---

// Returns all bill and income entries for the requested year/month,
// enriched with bill/category/person names and derived calculated amounts.
// Reference tables (bill, category, person) are fetched with IgnoreQueryFilters so
// entries that were linked to now-inactive templates still resolve their names.
app.MapGet("/api/entries", async (
    int? year,
    int? month,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (year is null || month is null || month < 1 || month > 12)
        return Results.BadRequest("year and month are required; month must be between 1 and 12.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    // Fetch entries for the requested month. The global query filter already scopes
    // BillEntries and IncomeEntries to the current owner (owner_id only, no active flag).
    var billEntries = await db.BillEntries
        .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value)
        .ToListAsync(ct);

    var incomeEntries = await db.IncomeEntries
        .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value)
        .ToListAsync(ct);

    // Fetch reference data with IgnoreQueryFilters so inactive bills/categories/persons
    // that were snapshotted at projection time still resolve. Filter by OwnerId manually.
    var billIds = billEntries.Select(e => e.BillId).ToHashSet();
    var billsById = billIds.Count > 0
        ? await db.Bills
            .IgnoreQueryFilters()
            .Where(b => b.OwnerId == appUser.Id && billIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct)
        : new Dictionary<long, Bill>();

    var categoryIds = billsById.Values.Select(b => b.CategoryId).ToHashSet();
    var categoriesById = categoryIds.Count > 0
        ? await db.Categories
            .IgnoreQueryFilters()
            .Where(c => c.OwnerId == appUser.Id && categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct)
        : new Dictionary<long, Category>();

    // PersonId on the BillEntry is the snapshotted value at projection time.
    var personIds = billEntries
        .Where(e => e.PersonId.HasValue)
        .Select(e => e.PersonId!.Value)
        .ToHashSet();
    var personsById = personIds.Count > 0
        ? await db.Persons
            .IgnoreQueryFilters()
            .Where(p => p.OwnerId == appUser.Id && personIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct)
        : new Dictionary<long, Person>();

    var incomeIds = incomeEntries.Select(e => e.IncomeId).ToHashSet();
    var incomesById = incomeIds.Count > 0
        ? await db.Incomes
            .IgnoreQueryFilters()
            .Where(i => i.OwnerId == appUser.Id && incomeIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct)
        : new Dictionary<long, Income>();

    // Build bill DTOs with derived values; sort by category then name in-memory.
    var billDtos = billEntries
        .Select(e =>
        {
            var bill = billsById[e.BillId];
            var category = categoriesById[bill.CategoryId];
            var personName = e.PersonId.HasValue && personsById.TryGetValue(e.PersonId.Value, out var p)
                ? p.Name
                : null;
            var effective = EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount);
            return new BillEntryDto(
                e.Id, e.BillId, bill.Name, category.Name,
                e.PlannedAmount, e.ActualAmount, e.SplitRatioSnapshot, personName,
                effective,
                EntryCalculations.MyShare(effective, e.SplitRatioSnapshot),
                EntryCalculations.Receivable(effective, e.SplitRatioSnapshot),
                e.Paid, e.PaidDate, e.Received, e.ReceivedDate);
        })
        .OrderBy(d => d.Category)
        .ThenBy(d => d.Name)
        .ToList();

    // Build income DTOs with derived values.
    var incomeDtos = incomeEntries
        .Select(e =>
        {
            var income = incomesById[e.IncomeId];
            var effective = EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount);
            return new IncomeEntryDto(
                e.Id, e.IncomeId, income.Name,
                e.PlannedAmount, e.ActualAmount,
                effective, e.Received, e.ReceivedDate);
        })
        .ToList();

    // --- Totals ---
    var billsPlanned = billDtos.Sum(d => d.PlannedAmount);
    var billsEffective = billDtos.Sum(d => d.EffectiveAmount);
    var totalMyShare = billDtos.Sum(d => d.MyShare);
    var totalReceivable = billDtos.Sum(d => d.Receivable);
    var incomesPlanned = incomeDtos.Sum(d => d.PlannedAmount);
    var incomesEffective = incomeDtos.Sum(d => d.EffectiveAmount);

    // saldoPrevisto: how much I expect to net — planned income minus my planned share of each bill.
    var saldoPrevisto = incomesPlanned - billDtos.Sum(d => d.PlannedAmount * d.SplitRatio);

    // saldoReal: received income minus my share of already-paid bills.
    var saldoReal = incomeDtos.Where(d => d.Received).Sum(d => d.EffectiveAmount)
        - billDtos.Where(d => d.Paid).Sum(d => d.MyShare);

    var totals = new MonthTotalsDto(
        billsPlanned, billsEffective, totalMyShare, totalReceivable,
        incomesPlanned, incomesEffective, saldoPrevisto, saldoReal);

    return Results.Ok(new MonthEntriesDto(year.Value, month.Value, billDtos, incomeDtos, totals));
})
.RequireAuthorization();

// --- One-off entry endpoints ---

// Creates a bill_entry for a one_off bill template in the requested month.
// Snapshots planned_amount, split_ratio and person_id from the template at creation time.
// Returns 409 if an entry already exists for the same template and month (UNIQUE constraint).
app.MapPost("/api/entries/bill", async (
    CreateBillEntryRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    if (req.Month < 1 || req.Month > 12)
        return Results.BadRequest("Month must be between 1 and 12.");

    if (req.PlannedAmount.HasValue && req.PlannedAmount.Value < 0)
        return Results.BadRequest("PlannedAmount must be zero or greater.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == req.BillId, ct);
    if (bill is null)
        return Results.NotFound("Bill not found.");

    if (bill.Kind != BillKind.OneOff)
        return Results.BadRequest("Only one_off bill templates can be used to create entries via this endpoint.");

    var plannedAmount = req.PlannedAmount ?? bill.DefaultAmount;
    var entry = BillEntry.Create(appUser.Id, bill.Id, req.Year, req.Month, plannedAmount, bill.SplitRatio, bill.PersonId, timeProvider.GetUtcNow());
    db.BillEntries.Add(entry);

    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
    {
        return Results.Conflict("A one-off entry for this bill and month already exists.");
    }

    return Results.Created($"/api/entries/bill/{entry.Id}", new BillEntryCreatedDto(
        entry.Id, entry.BillId, entry.RefYear, entry.RefMonth,
        entry.PlannedAmount, entry.ActualAmount, entry.SplitRatioSnapshot, entry.PersonId,
        entry.Paid, entry.PaidDate, entry.Received, entry.ReceivedDate));
})
.RequireAuthorization();

// Creates an income_entry for a one_off income template in the requested month.
// Returns 409 if an entry already exists for the same template and month (UNIQUE constraint).
app.MapPost("/api/entries/income", async (
    CreateIncomeEntryRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    if (req.Month < 1 || req.Month > 12)
        return Results.BadRequest("Month must be between 1 and 12.");

    if (req.PlannedAmount.HasValue && req.PlannedAmount.Value < 0)
        return Results.BadRequest("PlannedAmount must be zero or greater.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var income = await db.Incomes.FirstOrDefaultAsync(i => i.Id == req.IncomeId, ct);
    if (income is null)
        return Results.NotFound("Income not found.");

    if (income.Kind != IncomeKind.OneOff)
        return Results.BadRequest("Only one_off income templates can be used to create entries via this endpoint.");

    var plannedAmount = req.PlannedAmount ?? income.DefaultAmount;
    var entry = IncomeEntry.Create(appUser.Id, income.Id, req.Year, req.Month, plannedAmount, timeProvider.GetUtcNow());
    db.IncomeEntries.Add(entry);

    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
    {
        return Results.Conflict("A one-off entry for this income and month already exists.");
    }

    return Results.Created($"/api/entries/income/{entry.Id}", new IncomeEntryCreatedDto(
        entry.Id, entry.IncomeId, entry.RefYear, entry.RefMonth,
        entry.PlannedAmount, entry.ActualAmount, entry.Received, entry.ReceivedDate));
})
.RequireAuthorization();

// Deletes an unpaid one-off bill_entry (hard delete — unpaid entries have no history to preserve).
// Returns 409 if the entry has been paid (immutability: paid entries are frozen).
app.MapDelete("/api/entries/bill/{id:long}", async (
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

    var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
    if (entry is null)
        return Results.NotFound();

    if (entry.Paid)
        return Results.Conflict("Cannot delete a paid bill entry.");

    db.BillEntries.Remove(entry);
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
})
.RequireAuthorization();

// Deletes an unreceived one-off income_entry (hard delete — unreceived entries have no history to preserve).
// Returns 409 if the entry has been received (immutability: received entries are frozen).
app.MapDelete("/api/entries/income/{id:long}", async (
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

    var entry = await db.IncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
    if (entry is null)
        return Results.NotFound();

    if (entry.Received)
        return Results.Conflict("Cannot delete a received income entry.");

    db.IncomeEntries.Remove(entry);
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
})
.RequireAuthorization();

// --- Pay / unpay / patch endpoints ---

// Updates plannedAmount and/or actualAmount on an unfrozen bill entry.
// Returns 409 if the entry is paid (frozen).
app.MapMethods("/api/entries/bill/{id:long}", ["PATCH"], async (
    long id,
    PatchBillEntryRequest req,
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

    var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
    if (entry is null)
        return Results.NotFound();

    if (entry.Paid)
        return Results.Conflict("Cannot edit a frozen (paid) bill entry. Unpay it first.");

    try
    {
        entry.UpdateAmounts(req.PlannedAmount, req.ActualAmount);
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(ex.Message);
    }

    await db.SaveChangesAsync(ct);
    return Results.Ok(ToBillEntryDto(entry));
})
.RequireAuthorization();

// Marks a bill entry as paid. Freezes it and records the actual amount (defaults to planned).
app.MapPost("/api/entries/bill/{id:long}/pay", async (
    long id,
    PayBillEntryRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
    if (entry is null)
        return Results.NotFound();

    var paidAt = req.PaidDate.HasValue
        ? new DateTimeOffset(req.PaidDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        : timeProvider.GetUtcNow();

    entry.MarkPaid(paidAt, req.ActualAmount);
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToBillEntryDto(entry));
})
.RequireAuthorization();

// Unfreezes a paid bill entry so it can be edited again.
app.MapPost("/api/entries/bill/{id:long}/unpay", async (
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

    var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
    if (entry is null)
        return Results.NotFound();

    entry.Unfreeze();
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToBillEntryDto(entry));
})
.RequireAuthorization();

// Updates plannedAmount and/or actualAmount on an unfrozen income entry.
// Returns 409 if the entry is received (frozen).
app.MapMethods("/api/entries/income/{id:long}", ["PATCH"], async (
    long id,
    PatchIncomeEntryRequest req,
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

    var entry = await db.IncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
    if (entry is null)
        return Results.NotFound();

    if (entry.Received)
        return Results.Conflict("Cannot edit a frozen (received) income entry. Unreceive it first.");

    try
    {
        entry.UpdateAmounts(req.PlannedAmount, req.ActualAmount);
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(ex.Message);
    }

    await db.SaveChangesAsync(ct);
    return Results.Ok(ToIncomeEntryDto(entry));
})
.RequireAuthorization();

// Marks an income entry as received. Freezes it and records the actual amount (defaults to planned).
app.MapPost("/api/entries/income/{id:long}/receive", async (
    long id,
    ReceiveIncomeEntryRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var entry = await db.IncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
    if (entry is null)
        return Results.NotFound();

    var receivedAt = req.ReceivedDate.HasValue
        ? new DateTimeOffset(req.ReceivedDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        : timeProvider.GetUtcNow();

    entry.MarkReceived(receivedAt, req.ActualAmount);
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToIncomeEntryDto(entry));
})
.RequireAuthorization();

// Unfreezes a received income entry so it can be edited again.
app.MapPost("/api/entries/income/{id:long}/unreceive", async (
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

    var entry = await db.IncomeEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
    if (entry is null)
        return Results.NotFound();

    entry.Unfreeze();
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToIncomeEntryDto(entry));
})
.RequireAuthorization();

// --- Dashboard endpoints ---

// Returns a month-level dashboard: aggregated summary totals plus a per-category breakdown of
// the owner's share. Mirrors GET /api/entries's enrichment approach — bill/category lookups use
// IgnoreQueryFilters (scoped manually by OwnerId) so entries whose bill/category template has
// since been deactivated still resolve.
app.MapGet("/api/dashboard/month", async (
    int? year,
    int? month,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (year is null || month is null || month < 1 || month > 12)
        return Results.BadRequest("year and month are required; month must be between 1 and 12.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    // Fetch entries for the requested month. The global query filter already scopes
    // BillEntries/IncomeEntries to the current owner.
    var billEntries = await db.BillEntries
        .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value)
        .ToListAsync(ct);

    var incomeEntries = await db.IncomeEntries
        .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value)
        .ToListAsync(ct);

    // Resolve bill -> category via IgnoreQueryFilters lookups, scoped manually by OwnerId,
    // since a bill/category template referenced by an entry may since have been deactivated.
    var billIds = billEntries.Select(e => e.BillId).ToHashSet();
    var billsById = billIds.Count > 0
        ? await db.Bills
            .IgnoreQueryFilters()
            .Where(b => b.OwnerId == appUser.Id && billIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct)
        : new Dictionary<long, Bill>();

    var categoryIds = billsById.Values.Select(b => b.CategoryId).ToHashSet();
    var categoriesById = categoryIds.Count > 0
        ? await db.Categories
            .IgnoreQueryFilters()
            .Where(c => c.OwnerId == appUser.Id && categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct)
        : new Dictionary<long, Category>();

    // Group bill entries by category: plannedMyShare over all entries, actualMyShare over paid
    // entries only. Ordered by plannedMyShare descending; categories with no entries are absent.
    var byCategory = billEntries
        .GroupBy(e => billsById[e.BillId].CategoryId)
        .Select(g =>
        {
            var plannedMyShare = g.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));
            var actualMyShare = g
                .Where(e => e.Paid)
                .Sum(e => EntryCalculations.MyShare(
                    EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount),
                    e.SplitRatioSnapshot));

            return new DashboardCategoryDto(
                g.Key, categoriesById[g.Key].Name,
                plannedMyShare, actualMyShare, actualMyShare - plannedMyShare);
        })
        .OrderByDescending(d => d.PlannedMyShare)
        .ToList();

    var plannedExpense = byCategory.Sum(d => d.PlannedMyShare);
    var actualExpense = byCategory.Sum(d => d.ActualMyShare);

    var plannedIncome = incomeEntries.Sum(e => e.PlannedAmount);
    var actualIncome = incomeEntries
        .Where(e => e.Received)
        .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));

    var summary = new DashboardSummaryDto(
        plannedExpense, actualExpense,
        plannedIncome, actualIncome,
        plannedIncome - plannedExpense, actualIncome - actualExpense,
        billEntries.Count(e => e.Paid), billEntries.Count,
        incomeEntries.Count(e => e.Received), incomeEntries.Count);

    return Results.Ok(new DashboardMonthDto(year.Value, month.Value, summary, byCategory));
})
.RequireAuthorization();

// --- Dashboard year endpoint ---

// Returns a year-level dashboard: 12 always-present month summaries, a whole-year per-category
// breakdown of the owner's share, and grand totals. Mirrors GET /api/dashboard/month's
// enrichment approach (IgnoreQueryFilters scoped by OwnerId), but fetches the whole year once
// and aggregates per month and per category from the same in-memory sets.
app.MapGet("/api/dashboard/year", async (
    int? year,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (year is null || year < 2000 || year > 2100)
        return Results.BadRequest("year is required and must be between 2000 and 2100.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    // Fetch the whole year once; the global query filter already scopes BillEntries/IncomeEntries
    // to the current owner.
    var billEntries = await db.BillEntries
        .Where(e => e.RefYear == year.Value)
        .ToListAsync(ct);

    var incomeEntries = await db.IncomeEntries
        .Where(e => e.RefYear == year.Value)
        .ToListAsync(ct);

    // Resolve bill -> category via IgnoreQueryFilters lookups, scoped manually by OwnerId,
    // since a bill/category template referenced by an entry may since have been deactivated.
    var billIds = billEntries.Select(e => e.BillId).ToHashSet();
    var billsById = billIds.Count > 0
        ? await db.Bills
            .IgnoreQueryFilters()
            .Where(b => b.OwnerId == appUser.Id && billIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct)
        : new Dictionary<long, Bill>();

    var categoryIds = billsById.Values.Select(b => b.CategoryId).ToHashSet();
    var categoriesById = categoryIds.Count > 0
        ? await db.Categories
            .IgnoreQueryFilters()
            .Where(c => c.OwnerId == appUser.Id && categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct)
        : new Dictionary<long, Category>();

    // Build the 12 always-present month summaries (month 1..12, zeroed when no data).
    var billEntriesByMonth = billEntries.ToLookup(e => e.RefMonth);
    var incomeEntriesByMonth = incomeEntries.ToLookup(e => e.RefMonth);

    var months = Enumerable.Range(1, 12)
        .Select(m =>
        {
            var monthBills = billEntriesByMonth[m];
            var monthIncomes = incomeEntriesByMonth[m];

            var plannedExpense = monthBills.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));
            var actualExpense = monthBills
                .Where(e => e.Paid)
                .Sum(e => EntryCalculations.MyShare(
                    EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount),
                    e.SplitRatioSnapshot));

            var plannedIncome = monthIncomes.Sum(e => e.PlannedAmount);
            var actualIncome = monthIncomes
                .Where(e => e.Received)
                .Sum(e => EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount));

            return new DashboardMonthSummaryDto(
                m, plannedExpense, actualExpense, plannedIncome, actualIncome,
                plannedIncome - plannedExpense, actualIncome - actualExpense);
        })
        .ToList();

    // Per-category totals across the whole year; categories with no bill entries are omitted.
    var byCategory = billEntries
        .GroupBy(e => billsById[e.BillId].CategoryId)
        .Select(g =>
        {
            var plannedMyShare = g.Sum(e => EntryCalculations.MyShare(e.PlannedAmount, e.SplitRatioSnapshot));
            var actualMyShare = g
                .Where(e => e.Paid)
                .Sum(e => EntryCalculations.MyShare(
                    EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount),
                    e.SplitRatioSnapshot));

            return new DashboardCategoryYearDto(g.Key, categoriesById[g.Key].Name, plannedMyShare, actualMyShare);
        })
        .OrderByDescending(d => d.PlannedMyShare)
        .ToList();

    var totals = new DashboardYearTotalsDto(
        months.Sum(m => m.PlannedExpense), months.Sum(m => m.ActualExpense),
        months.Sum(m => m.PlannedIncome), months.Sum(m => m.ActualIncome),
        months.Sum(m => m.SaldoPrevisto), months.Sum(m => m.SaldoReal));

    return Results.Ok(new DashboardYearDto(year.Value, months, byCategory, totals));
})
.RequireAuthorization();

// --- Receivables month endpoint ---

// Returns a per-person panel of "a receber" (receivable) amounts for the given month: only
// BillEntry rows with a split (SplitRatioSnapshot < 1) and an assigned person are relevant.
// Mirrors GET /api/entries's enrichment approach for bill/person name resolution.
app.MapGet("/api/receivables/month", async (
    int? year,
    int? month,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (year is null || month is null || month < 1 || month > 12)
        return Results.BadRequest("year and month are required; month must be between 1 and 12.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var entries = await db.BillEntries
        .Where(e => e.RefYear == year.Value && e.RefMonth == month.Value &&
                    e.SplitRatioSnapshot < 1 && e.PersonId != null)
        .ToListAsync(ct);

    var billIds = entries.Select(e => e.BillId).ToHashSet();
    var billsById = billIds.Count > 0
        ? await db.Bills
            .IgnoreQueryFilters()
            .Where(b => b.OwnerId == appUser.Id && billIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct)
        : new Dictionary<long, Bill>();

    var personIds = entries.Select(e => e.PersonId!.Value).ToHashSet();
    var personsById = personIds.Count > 0
        ? await db.Persons
            .IgnoreQueryFilters()
            .Where(p => p.OwnerId == appUser.Id && personIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct)
        : new Dictionary<long, Person>();

    var byPerson = entries
        .GroupBy(e => e.PersonId!.Value)
        .Select(g =>
        {
            var items = g
                .OrderBy(e => e.Id)
                .Select(e => new ReceivableItemDto(
                    e.Id, billsById[e.BillId].Name,
                    EntryCalculations.Receivable(
                        EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot),
                    e.Received))
                .ToList();

            var totalDevido = items.Sum(i => i.Receivable);
            var jaRecebido = items.Where(i => i.Received).Sum(i => i.Receivable);
            var pendente = items.Where(i => !i.Received).Sum(i => i.Receivable);

            return new PersonReceivablesDto(g.Key, personsById[g.Key].Name, totalDevido, jaRecebido, pendente, items);
        })
        .OrderBy(p => p.Name)
        .ToList();

    var totalPendenteGeral = byPerson.Sum(p => p.Pendente);

    return Results.Ok(new ReceivablesMonthDto(year.Value, month.Value, byPerson, totalPendenteGeral));
})
.RequireAuthorization();

// Marks the split portion of a bill entry as received from the other person. Idempotent —
// marking an already-received entry again simply re-applies the same received date. Never
// touches Paid/PaidDate, which track the independent fact that the owner paid the bill.
app.MapPost("/api/receivables/{entryId:long}/mark", async (
    long entryId,
    MarkReceivableRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);
    if (entry is null)
        return Results.NotFound();

    if (entry.SplitRatioSnapshot == 1)
        return Results.BadRequest("This entry has no split; it is not a receivable.");

    var receivedAt = req.ReceivedDate.HasValue
        ? new DateTimeOffset(req.ReceivedDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        : timeProvider.GetUtcNow();

    entry.MarkReceived(receivedAt);
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToBillEntryDto(entry));
})
.RequireAuthorization();

// Reverses a prior mark-as-received. Never touches Paid/PaidDate.
app.MapPost("/api/receivables/{entryId:long}/unmark", async (
    long entryId,
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

    var entry = await db.BillEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);
    if (entry is null)
        return Results.NotFound();

    entry.UnmarkReceived();
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToBillEntryDto(entry));
})
.RequireAuthorization();

// Marks several bill entries as received in one transaction. All-or-nothing: every id must
// exist, belong to the caller (the BillEntries query filter already scopes reads to the
// current owner, so a foreign/unknown id is simply absent from the fetched set), and be an
// actual receivable (SplitRatioSnapshot < 1). If any id fails these checks, nothing is marked.
app.MapPost("/api/receivables/mark-batch", async (
    MarkBatchRequest req,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    TimeProvider timeProvider,
    CancellationToken ct) =>
{
    if (req.EntryIds is null || req.EntryIds.Count == 0)
        return Results.BadRequest("EntryIds must contain at least one id.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var entryIds = req.EntryIds.ToHashSet();
    var entries = await db.BillEntries
        .Where(e => entryIds.Contains(e.Id))
        .ToListAsync(ct);

    if (entries.Count != entryIds.Count || entries.Any(e => e.SplitRatioSnapshot == 1))
        return Results.BadRequest("One or more entries are invalid, not owned by you, or not a receivable.");

    var receivedAt = req.ReceivedDate.HasValue
        ? new DateTimeOffset(req.ReceivedDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        : timeProvider.GetUtcNow();

    await using var tx = await db.Database.BeginTransactionAsync(ct);

    foreach (var entry in entries)
        entry.MarkReceived(receivedAt);

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return Results.Ok(new MarkBatchResponse(entries.Count));
})
.RequireAuthorization();

// --- Receivables history endpoint ---

// Returns the receivable history for a single person: item-level rows plus aggregates computed
// over whatever period/status filter was applied. personId must belong to the caller — Persons
// is already owner-filtered, so a null lookup naturally covers "not found" and "not yours" alike.
app.MapGet("/api/receivables/history", async (
    long? personId,
    int? fromYear,
    int? fromMonth,
    int? toYear,
    int? toMonth,
    string? status,
    System.Security.Claims.ClaimsPrincipal user,
    IUserProvisioningService provisioning,
    ICurrentOwner currentOwner,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (personId is null)
        return Results.BadRequest("personId is required.");

    var firebaseUid = user.GetFirebaseUid();
    if (string.IsNullOrWhiteSpace(firebaseUid))
        return Results.Unauthorized();

    var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
    currentOwner.Id = appUser.Id;

    var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == personId.Value, ct);
    if (person is null)
        return Results.NotFound();

    var entries = await db.BillEntries
        .Where(e => e.PersonId == personId.Value && e.SplitRatioSnapshot < 1)
        .ToListAsync(ct);

    if (fromYear.HasValue && fromMonth.HasValue)
    {
        entries = entries
            .Where(e => EntryCalculations.IsInForwardRange(e.RefYear, e.RefMonth, fromYear.Value, fromMonth.Value))
            .ToList();
    }

    if (toYear.HasValue && toMonth.HasValue)
    {
        // Reuses IsInForwardRange the other way round: "entry is at or before (toYear, toMonth)".
        entries = entries
            .Where(e => EntryCalculations.IsInForwardRange(toYear.Value, toMonth.Value, e.RefYear, e.RefMonth))
            .ToList();
    }

    // Anything other than "received"/"pending" (including a missing or unrecognized value) is
    // treated as "all" — the simplest, most defensive default that never rejects a valid request.
    entries = status switch
    {
        "received" => entries.Where(e => e.Received).ToList(),
        "pending" => entries.Where(e => !e.Received).ToList(),
        _ => entries,
    };

    var billIds = entries.Select(e => e.BillId).ToHashSet();
    var billsById = billIds.Count > 0
        ? await db.Bills
            .IgnoreQueryFilters()
            .Where(b => b.OwnerId == appUser.Id && billIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct)
        : new Dictionary<long, Bill>();

    var items = entries
        .Select(e => new ReceivablesHistoryItemDto(
            e.Id, billsById[e.BillId].Name, e.RefYear, e.RefMonth,
            EntryCalculations.Receivable(
                EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount), e.SplitRatioSnapshot),
            e.Received, e.ReceivedDate))
        .OrderByDescending(i => i.Year)
        .ThenByDescending(i => i.Month)
        .ToList();

    var totalDevido = items.Sum(i => i.Receivable);
    var totalRecebido = items.Where(i => i.Received).Sum(i => i.Receivable);
    var totalPendente = items.Where(i => !i.Received).Sum(i => i.Receivable);

    var totals = new ReceivablesHistoryTotalsDto(totalDevido, totalRecebido, totalPendente);

    return Results.Ok(new ReceivablesHistoryDto(person.Id, person.Name, totals, items));
})
.RequireAuthorization();

app.MapGet("/api/bills/{billId:long}/history", async (
    long billId,
    int? fromYear,
    int? fromMonth,
    int? toYear,
    int? toMonth,
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

    // IgnoreQueryFilters + manual OwnerId check: the bill template may have been deactivated
    // since some of its entries were created, but its history must still resolve.
    var bill = await db.Bills
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(b => b.Id == billId && b.OwnerId == appUser.Id, ct);
    if (bill is null)
        return Results.NotFound();

    var category = await db.Categories
        .IgnoreQueryFilters()
        .FirstAsync(c => c.Id == bill.CategoryId && c.OwnerId == appUser.Id, ct);

    var person = bill.PersonId.HasValue
        ? await db.Persons
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == bill.PersonId.Value && p.OwnerId == appUser.Id, ct)
        : null;

    var entries = await db.BillEntries
        .Where(e => e.BillId == billId)
        .ToListAsync(ct);

    if (fromYear.HasValue && fromMonth.HasValue)
    {
        entries = entries
            .Where(e => EntryCalculations.IsInForwardRange(e.RefYear, e.RefMonth, fromYear.Value, fromMonth.Value))
            .ToList();
    }

    if (toYear.HasValue && toMonth.HasValue)
    {
        // Reuses IsInForwardRange the other way round: "entry at or before (toYear, toMonth)".
        entries = entries
            .Where(e => EntryCalculations.IsInForwardRange(toYear.Value, toMonth.Value, e.RefYear, e.RefMonth))
            .ToList();
    }

    var ordered = entries.OrderBy(e => e.RefYear).ThenBy(e => e.RefMonth).ToList();

    var items = new List<BillHistoryItemDto>(ordered.Count);
    decimal? previousEffective = null;
    foreach (var e in ordered)
    {
        var effective = EntryCalculations.EffectiveAmount(e.PlannedAmount, e.ActualAmount);
        var myShare = EntryCalculations.MyShare(effective, e.SplitRatioSnapshot);
        var variation = EntryCalculations.ComputeVariation(effective, previousEffective);

        items.Add(new BillHistoryItemDto(
            e.RefYear, e.RefMonth, e.PlannedAmount, e.ActualAmount, effective, myShare,
            e.Paid, e.PaidDate,
            variation is null ? null : new BillHistoryVariationDto(variation.Value.Abs, variation.Value.Pct)));

        previousEffective = effective;
    }

    var summary = new BillHistorySummaryDto(
        items.Count > 0 ? items.Average(i => i.Effective) : 0m,
        items.Count > 0 ? items.Min(i => i.Effective) : 0m,
        items.Count > 0 ? items.Max(i => i.Effective) : 0m,
        items.Where(i => i.Paid).Sum(i => i.MyShare));

    return Results.Ok(new BillHistoryDto(
        bill.Id, bill.Name, category.Name, bill.SplitRatio, person?.Name, summary, items));
})
.RequireAuthorization();

await app.RunAsync();

// Maps a BillEntry domain object to the shared entry DTO used by pay/unpay/patch endpoints.
static BillEntryCreatedDto ToBillEntryDto(BillEntry e) => new(
    e.Id, e.BillId, e.RefYear, e.RefMonth,
    e.PlannedAmount, e.ActualAmount, e.SplitRatioSnapshot, e.PersonId,
    e.Paid, e.PaidDate, e.Received, e.ReceivedDate);

// Maps an IncomeEntry domain object to the shared entry DTO used by receive/unreceive/patch endpoints.
static IncomeEntryCreatedDto ToIncomeEntryDto(IncomeEntry e) => new(
    e.Id, e.IncomeId, e.RefYear, e.RefMonth,
    e.PlannedAmount, e.ActualAmount, e.Received, e.ReceivedDate);

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

/// <summary>The payload for a single bill entry returned by <c>GET /api/entries</c>.</summary>
/// <param name="Id">The bill entry id.</param>
/// <param name="BillId">The source bill template id.</param>
/// <param name="Name">The bill name (from the template at projection time).</param>
/// <param name="Category">The category name (from the template at projection time).</param>
/// <param name="PlannedAmount">The snapshotted planned amount.</param>
/// <param name="ActualAmount">The confirmed actual amount, or <see langword="null"/> when not yet set.</param>
/// <param name="SplitRatio">The snapshotted owner split ratio.</param>
/// <param name="Person">The name of the person who owes the remaining fraction, or <see langword="null"/> when SplitRatio is 1.</param>
/// <param name="EffectiveAmount">Actual when present; otherwise planned.</param>
/// <param name="MyShare">Effective amount multiplied by the split ratio.</param>
/// <param name="Receivable">Effective amount multiplied by (1 − split ratio).</param>
/// <param name="Paid">Whether the owner has paid this bill.</param>
/// <param name="PaidDate">The UTC instant of payment, or <see langword="null"/>.</param>
/// <param name="Received">Whether the split portion has been received from the other person.</param>
/// <param name="ReceivedDate">The UTC instant the split was received, or <see langword="null"/>.</param>
internal sealed record BillEntryDto(long Id, long BillId, string Name, string Category,
    decimal PlannedAmount, decimal? ActualAmount, decimal SplitRatio, string? Person,
    decimal EffectiveAmount, decimal MyShare, decimal Receivable,
    bool Paid, DateTimeOffset? PaidDate, bool Received, DateTimeOffset? ReceivedDate);

/// <summary>The payload for a single income entry returned by <c>GET /api/entries</c>.</summary>
/// <param name="Id">The income entry id.</param>
/// <param name="IncomeId">The source income template id.</param>
/// <param name="Name">The income name (from the template at projection time).</param>
/// <param name="PlannedAmount">The snapshotted planned amount.</param>
/// <param name="ActualAmount">The confirmed actual amount, or <see langword="null"/> when not yet set.</param>
/// <param name="EffectiveAmount">Actual when present; otherwise planned.</param>
/// <param name="Received">Whether this income has been received.</param>
/// <param name="ReceivedDate">The UTC instant the income was received, or <see langword="null"/>.</param>
internal sealed record IncomeEntryDto(long Id, long IncomeId, string Name,
    decimal PlannedAmount, decimal? ActualAmount,
    decimal EffectiveAmount, bool Received, DateTimeOffset? ReceivedDate);

/// <summary>Aggregated totals for the requested month, returned by <c>GET /api/entries</c>.</summary>
/// <param name="BillsPlanned">Sum of all bill entry planned amounts.</param>
/// <param name="BillsEffective">Sum of all bill entry effective amounts.</param>
/// <param name="MyShare">Sum of the owner's share across all bill entries.</param>
/// <param name="Receivable">Sum of the other person's share across all bill entries.</param>
/// <param name="IncomesPlanned">Sum of all income entry planned amounts.</param>
/// <param name="IncomesEffective">Sum of all income entry effective amounts.</param>
/// <param name="SaldoPrevisto">
/// Planned net balance: Σ(income planned) − Σ(bill planned × split ratio).
/// </param>
/// <param name="SaldoReal">
/// Realised net balance: Σ(effective amount for received incomes) − Σ(my share for paid bills).
/// </param>
internal sealed record MonthTotalsDto(decimal BillsPlanned, decimal BillsEffective,
    decimal MyShare, decimal Receivable,
    decimal IncomesPlanned, decimal IncomesEffective,
    decimal SaldoPrevisto, decimal SaldoReal);

/// <summary>The complete response returned by <c>GET /api/entries</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Month">The requested month (1–12).</param>
/// <param name="Bills">Bill entries for the month, sorted by category then name.</param>
/// <param name="Incomes">Income entries for the month.</param>
/// <param name="Totals">Aggregated totals for the month.</param>
internal sealed record MonthEntriesDto(int Year, int Month,
    IReadOnlyList<BillEntryDto> Bills, IReadOnlyList<IncomeEntryDto> Incomes,
    MonthTotalsDto Totals);

/// <summary>The request body for <c>PATCH /api/entries/bill/{id}</c>.</summary>
internal sealed record PatchBillEntryRequest(decimal? PlannedAmount, decimal? ActualAmount);

/// <summary>The request body for <c>POST /api/entries/bill/{id}/pay</c>.</summary>
internal sealed record PayBillEntryRequest(decimal? ActualAmount, DateOnly? PaidDate);

/// <summary>The request body for <c>PATCH /api/entries/income/{id}</c>.</summary>
internal sealed record PatchIncomeEntryRequest(decimal? PlannedAmount, decimal? ActualAmount);

/// <summary>The request body for <c>POST /api/entries/income/{id}/receive</c>.</summary>
internal sealed record ReceiveIncomeEntryRequest(decimal? ActualAmount, DateOnly? ReceivedDate);

/// <summary>The request body for <c>POST /api/entries/bill</c>.</summary>
/// <param name="BillId">The one_off bill template to create an entry from.</param>
/// <param name="Year">The reference year.</param>
/// <param name="Month">The reference month (1–12).</param>
/// <param name="PlannedAmount">The planned amount; falls back to the template's DefaultAmount when null.</param>
internal sealed record CreateBillEntryRequest(long BillId, int Year, int Month, decimal? PlannedAmount);

/// <summary>The request body for <c>POST /api/entries/income</c>.</summary>
/// <param name="IncomeId">The one_off income template to create an entry from.</param>
/// <param name="Year">The reference year.</param>
/// <param name="Month">The reference month (1–12).</param>
/// <param name="PlannedAmount">The planned amount; falls back to the template's DefaultAmount when null.</param>
internal sealed record CreateIncomeEntryRequest(long IncomeId, int Year, int Month, decimal? PlannedAmount);

/// <summary>The payload returned by <c>POST /api/entries/bill</c>.</summary>
internal sealed record BillEntryCreatedDto(
    long Id, long BillId, int RefYear, int RefMonth,
    decimal PlannedAmount, decimal? ActualAmount,
    decimal SplitRatioSnapshot, long? PersonId,
    bool Paid, DateTimeOffset? PaidDate,
    bool Received, DateTimeOffset? ReceivedDate);

/// <summary>The payload returned by <c>POST /api/entries/income</c>.</summary>
internal sealed record IncomeEntryCreatedDto(
    long Id, long IncomeId, int RefYear, int RefMonth,
    decimal PlannedAmount, decimal? ActualAmount,
    bool Received, DateTimeOffset? ReceivedDate);

/// <summary>The request body for <c>POST /api/bills/{billId}/recalculate</c>.</summary>
/// <param name="FromYear">The reference year from which to start recalculation (inclusive).</param>
/// <param name="FromMonth">The reference month from which to start recalculation (1–12, inclusive).</param>
/// <param name="NewAmount">The new planned amount to apply; must be zero or greater.</param>
internal sealed record RecalculateRequest(int FromYear, int FromMonth, decimal NewAmount);

/// <summary>The response returned by <c>POST /api/bills/{billId}/recalculate</c>.</summary>
/// <param name="BillId">The recalculated bill's id.</param>
/// <param name="UpdatedEntries">The number of unpaid entries whose planned amount was updated.</param>
/// <param name="SkippedPaid">The number of paid entries in range that were left untouched.</param>
/// <param name="NewDefaultAmount">The new default amount now set on the bill template.</param>
internal sealed record RecalculateResponse(long BillId, int UpdatedEntries, int SkippedPaid, decimal NewDefaultAmount);

/// <summary>The per-category breakdown row returned by <c>GET /api/dashboard/month</c>.</summary>
/// <param name="CategoryId">The category id.</param>
/// <param name="Category">The category display name.</param>
/// <param name="PlannedMyShare">Sum of (planned amount × split ratio) over all bill entries in the category, regardless of paid status.</param>
/// <param name="ActualMyShare">Sum of (effective amount × split ratio) over only the paid bill entries in the category.</param>
/// <param name="Diff">
/// <see cref="ActualMyShare"/> minus <see cref="PlannedMyShare"/>. Positive means the category overspent relative to plan.
/// </param>
internal sealed record DashboardCategoryDto(long CategoryId, string Category, decimal PlannedMyShare, decimal ActualMyShare, decimal Diff);

/// <summary>Aggregated month-level totals returned by <c>GET /api/dashboard/month</c>.</summary>
/// <param name="PlannedExpense">Sum of the owner's share of planned amounts across all bill entries.</param>
/// <param name="ActualExpense">Sum of the owner's share of effective amounts across paid bill entries only.</param>
/// <param name="PlannedIncome">Sum of planned amounts across all income entries.</param>
/// <param name="ActualIncome">Sum of effective amounts across received income entries only.</param>
/// <param name="SaldoPrevisto"><see cref="PlannedIncome"/> minus <see cref="PlannedExpense"/>.</param>
/// <param name="SaldoReal"><see cref="ActualIncome"/> minus <see cref="ActualExpense"/>.</param>
/// <param name="BillsPaid">The number of bill entries in the month with <c>Paid</c> set to <see langword="true"/>.</param>
/// <param name="BillsTotal">The total number of bill entries in the month.</param>
/// <param name="IncomesReceived">The number of income entries in the month with <c>Received</c> set to <see langword="true"/>.</param>
/// <param name="IncomesTotal">The total number of income entries in the month.</param>
internal sealed record DashboardSummaryDto(
    decimal PlannedExpense, decimal ActualExpense,
    decimal PlannedIncome, decimal ActualIncome,
    decimal SaldoPrevisto, decimal SaldoReal,
    int BillsPaid, int BillsTotal,
    int IncomesReceived, int IncomesTotal);

/// <summary>The complete response returned by <c>GET /api/dashboard/month</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Month">The requested month (1–12).</param>
/// <param name="Summary">Aggregated totals for the month.</param>
/// <param name="ByCategory">Per-category breakdown, ordered by <see cref="DashboardCategoryDto.PlannedMyShare"/> descending. Categories with no bill entries in the month are omitted.</param>
internal sealed record DashboardMonthDto(int Year, int Month, DashboardSummaryDto Summary, IReadOnlyList<DashboardCategoryDto> ByCategory);

/// <summary>A single month's summary row within <c>GET /api/dashboard/year</c>.</summary>
/// <param name="Month">The month (1–12).</param>
/// <param name="PlannedExpense">Sum of the owner's share of planned amounts across all bill entries in the month.</param>
/// <param name="ActualExpense">Sum of the owner's share of effective amounts across paid bill entries only.</param>
/// <param name="PlannedIncome">Sum of planned amounts across all income entries in the month.</param>
/// <param name="ActualIncome">Sum of effective amounts across received income entries only.</param>
/// <param name="SaldoPrevisto"><see cref="PlannedIncome"/> minus <see cref="PlannedExpense"/>.</param>
/// <param name="SaldoReal"><see cref="ActualIncome"/> minus <see cref="ActualExpense"/>.</param>
internal sealed record DashboardMonthSummaryDto(
    int Month, decimal PlannedExpense, decimal ActualExpense,
    decimal PlannedIncome, decimal ActualIncome, decimal SaldoPrevisto, decimal SaldoReal);

/// <summary>The per-category breakdown row returned by <c>GET /api/dashboard/year</c>, totalled over the whole year.</summary>
/// <param name="CategoryId">The category id.</param>
/// <param name="Category">The category display name.</param>
/// <param name="PlannedMyShare">Sum of (planned amount × split ratio) over the whole year, regardless of paid status.</param>
/// <param name="ActualMyShare">Sum of (effective amount × split ratio) over only the paid bill entries in the year.</param>
internal sealed record DashboardCategoryYearDto(long CategoryId, string Category, decimal PlannedMyShare, decimal ActualMyShare);

/// <summary>Aggregated year-level totals returned by <c>GET /api/dashboard/year</c> — the sum of the 12 months.</summary>
/// <param name="PlannedExpense">Sum of <see cref="DashboardMonthSummaryDto.PlannedExpense"/> across the 12 months.</param>
/// <param name="ActualExpense">Sum of <see cref="DashboardMonthSummaryDto.ActualExpense"/> across the 12 months.</param>
/// <param name="PlannedIncome">Sum of <see cref="DashboardMonthSummaryDto.PlannedIncome"/> across the 12 months.</param>
/// <param name="ActualIncome">Sum of <see cref="DashboardMonthSummaryDto.ActualIncome"/> across the 12 months.</param>
/// <param name="SaldoPrevisto"><see cref="PlannedIncome"/> minus <see cref="PlannedExpense"/>.</param>
/// <param name="SaldoReal"><see cref="ActualIncome"/> minus <see cref="ActualExpense"/>.</param>
internal sealed record DashboardYearTotalsDto(
    decimal PlannedExpense, decimal ActualExpense,
    decimal PlannedIncome, decimal ActualIncome, decimal SaldoPrevisto, decimal SaldoReal);

/// <summary>The complete response returned by <c>GET /api/dashboard/year</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Months">Always exactly 12 entries (month 1–12); months with no data are zeroed rather than omitted.</param>
/// <param name="ByCategory">
/// Whole-year per-category totals, ordered by <see cref="DashboardCategoryYearDto.PlannedMyShare"/> descending.
/// Categories with no bill entries in the year are omitted.
/// </param>
/// <param name="Totals">Grand totals for the year — the sum of the 12 months.</param>
internal sealed record DashboardYearDto(
    int Year, IReadOnlyList<DashboardMonthSummaryDto> Months,
    IReadOnlyList<DashboardCategoryYearDto> ByCategory, DashboardYearTotalsDto Totals);

/// <summary>A single bill entry row within a person's panel in <c>GET /api/receivables/month</c>.</summary>
/// <param name="EntryId">The bill entry id.</param>
/// <param name="Bill">The bill's display name (resolved even if the bill template was since deactivated).</param>
/// <param name="Receivable">The amount owed to the owner: effective amount × (1 − split ratio).</param>
/// <param name="Received">Whether this split portion has already been received.</param>
internal sealed record ReceivableItemDto(long EntryId, string Bill, decimal Receivable, bool Received);

/// <summary>A single person's row in the <c>GET /api/receivables/month</c> panel.</summary>
/// <param name="PersonId">The person id.</param>
/// <param name="Name">The person's display name.</param>
/// <param name="TotalDevido">Sum of <see cref="Receivable"/> across all of this person's entries in the month.</param>
/// <param name="JaRecebido">Sum of <see cref="Receivable"/> across entries already marked received.</param>
/// <param name="Pendente">Sum of <see cref="Receivable"/> across entries not yet received.</param>
/// <param name="Items">The individual bill entries owed by this person.</param>
internal sealed record PersonReceivablesDto(
    long PersonId, string Name, decimal TotalDevido, decimal JaRecebido, decimal Pendente,
    IReadOnlyList<ReceivableItemDto> Items);

/// <summary>The complete response returned by <c>GET /api/receivables/month</c>.</summary>
/// <param name="Year">The requested year.</param>
/// <param name="Month">The requested month (1–12).</param>
/// <param name="ByPerson">One row per person with at least one receivable entry in the month, ordered by name.</param>
/// <param name="TotalPendenteGeral">Sum of <see cref="PersonReceivablesDto.Pendente"/> across all people.</param>
internal sealed record ReceivablesMonthDto(
    int Year, int Month, IReadOnlyList<PersonReceivablesDto> ByPerson, decimal TotalPendenteGeral);

/// <summary>The request body for <c>POST /api/receivables/{entryId}/mark</c>.</summary>
/// <param name="ReceivedDate">The date the split was received, or <see langword="null"/> to use the current instant.</param>
internal sealed record MarkReceivableRequest(DateOnly? ReceivedDate);

/// <summary>The request body for <c>POST /api/receivables/mark-batch</c>.</summary>
/// <param name="EntryIds">The bill entry ids to mark as received; must all be valid receivables owned by the caller.</param>
/// <param name="ReceivedDate">The date the split was received, or <see langword="null"/> to use the current instant.</param>
internal sealed record MarkBatchRequest(IReadOnlyList<long> EntryIds, DateOnly? ReceivedDate);

/// <summary>The response returned by <c>POST /api/receivables/mark-batch</c>.</summary>
/// <param name="Marked">The number of entries marked as received.</param>
internal sealed record MarkBatchResponse(int Marked);

/// <summary>A single item row returned by <c>GET /api/receivables/history</c>.</summary>
/// <param name="EntryId">The bill entry id.</param>
/// <param name="Bill">The bill's display name (resolved even if the bill template was since deactivated).</param>
/// <param name="Year">The entry's reference year.</param>
/// <param name="Month">The entry's reference month (1–12).</param>
/// <param name="Receivable">The amount owed to the owner: effective amount × (1 − split ratio).</param>
/// <param name="Received">Whether this split portion has already been received.</param>
/// <param name="ReceivedDate">The UTC instant the split was received, or <see langword="null"/>.</param>
internal sealed record ReceivablesHistoryItemDto(
    long EntryId, string Bill, int Year, int Month, decimal Receivable, bool Received, DateTimeOffset? ReceivedDate);

/// <summary>Aggregated totals returned by <c>GET /api/receivables/history</c>, computed over the filtered slice.</summary>
/// <param name="TotalDevido">Sum of <see cref="ReceivablesHistoryItemDto.Receivable"/> across the filtered items.</param>
/// <param name="TotalRecebido">Sum of <see cref="ReceivablesHistoryItemDto.Receivable"/> across received items only.</param>
/// <param name="TotalPendente">Sum of <see cref="ReceivablesHistoryItemDto.Receivable"/> across pending items only.</param>
internal sealed record ReceivablesHistoryTotalsDto(decimal TotalDevido, decimal TotalRecebido, decimal TotalPendente);

/// <summary>The complete response returned by <c>GET /api/receivables/history</c>.</summary>
/// <param name="PersonId">The requested person's id.</param>
/// <param name="Name">The person's display name.</param>
/// <param name="Totals">Aggregates computed over whatever period/status filter was applied.</param>
/// <param name="Items">Item-level rows, ordered by year then month descending (most recent first).</param>
internal sealed record ReceivablesHistoryDto(
    long PersonId, string Name, ReceivablesHistoryTotalsDto Totals, IReadOnlyList<ReceivablesHistoryItemDto> Items);

/// <summary>The period-over-period variation for a single item in <c>GET /api/bills/{billId}/history</c>.</summary>
/// <param name="Abs">Absolute change in <see cref="BillHistoryItemDto.Effective"/> vs. the previous item.</param>
/// <param name="Pct">
/// Percentage change vs. the previous item, or <see langword="null"/> when the previous
/// effective amount was zero.
/// </param>
internal sealed record BillHistoryVariationDto(decimal Abs, decimal? Pct);

/// <summary>A single monthly item returned by <c>GET /api/bills/{billId}/history</c>.</summary>
/// <param name="Year">The entry's reference year.</param>
/// <param name="Month">The entry's reference month (1–12).</param>
/// <param name="PlannedAmount">The planned amount snapshotted for this month.</param>
/// <param name="ActualAmount">The actual amount, or <see langword="null"/> if not yet confirmed.</param>
/// <param name="Effective">Actual amount when present, otherwise planned.</param>
/// <param name="MyShare">The owner's share of <see cref="Effective"/>, using the split ratio snapshotted at projection time.</param>
/// <param name="Paid">Whether the owner has paid this expense.</param>
/// <param name="PaidDate">The UTC instant the expense was paid, or <see langword="null"/>.</param>
/// <param name="Variation">The change vs. the previous item in chronological order, or <see langword="null"/> for the first item.</param>
internal sealed record BillHistoryItemDto(
    int Year, int Month, decimal PlannedAmount, decimal? ActualAmount, decimal Effective, decimal MyShare,
    bool Paid, DateTimeOffset? PaidDate, BillHistoryVariationDto? Variation);

/// <summary>Aggregated totals returned by <c>GET /api/bills/{billId}/history</c>, computed over the filtered slice.</summary>
/// <param name="AvgEffective">The average <see cref="BillHistoryItemDto.Effective"/> across items; zero when there are none.</param>
/// <param name="MinEffective">The minimum <see cref="BillHistoryItemDto.Effective"/> across items; zero when there are none.</param>
/// <param name="MaxEffective">The maximum <see cref="BillHistoryItemDto.Effective"/> across items; zero when there are none.</param>
/// <param name="TotalPaidMyShare">The sum of <see cref="BillHistoryItemDto.MyShare"/> across paid items only.</param>
internal sealed record BillHistorySummaryDto(
    decimal AvgEffective, decimal MinEffective, decimal MaxEffective, decimal TotalPaidMyShare);

/// <summary>The complete response returned by <c>GET /api/bills/{billId}/history</c>.</summary>
/// <param name="BillId">The requested bill template's id.</param>
/// <param name="Name">The bill template's display name.</param>
/// <param name="Category">The bill's category display name.</param>
/// <param name="SplitRatio">The bill template's current split ratio (not a per-entry snapshot).</param>
/// <param name="Person">The name of the person who owes the split, or <see langword="null"/> when the bill is not shared.</param>
/// <param name="Summary">Aggregates computed over whatever period filter was applied.</param>
/// <param name="Items">Item-level rows, ordered by year then month ascending (chronological).</param>
internal sealed record BillHistoryDto(
    long BillId, string Name, string Category, decimal SplitRatio, string? Person,
    BillHistorySummaryDto Summary, IReadOnlyList<BillHistoryItemDto> Items);

/// <summary>
/// Program entry-point marker, made discoverable so integration tests can host the API
/// through <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;

using BillsBackend.Api.Contracts;
using BillsBackend.Api.Data;
using BillsBackend.Api.Domain;
using BillsBackend.Api.Identity;
using Microsoft.EntityFrameworkCore;

namespace BillsBackend.Api.Endpoints;

internal static class CategoryEndpoints
{
    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/categories", CreateCategory);
        group.MapGet("/categories", ListCategories);
        group.MapPut("/categories/{id:long}", UpdateCategory);
        group.MapDelete("/categories/{id:long}", DeleteCategory);
        return group;
    }

    private static async Task<IResult> CreateCategory(
        CreateCategoryRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
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

        return Results.Created($"/api/v1/categories/{category.Id}", new CategoryDto(category.Id, category.Name));
    }

    private static async Task<IResult> ListCategories(
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
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
    }

    private static async Task<IResult> UpdateCategory(
        long id,
        UpdateCategoryRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
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
    }

    private static async Task<IResult> DeleteCategory(
        long id,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
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
    }
}

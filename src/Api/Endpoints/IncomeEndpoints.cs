using Api.Identity;
using Application.Abstractions.Services;
using BillsBackend.Api.Contracts;
using Data.Contexts;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

internal static class IncomeEndpoints
{
    public static RouteGroupBuilder MapIncomeEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/incomes", CreateIncome);
        group.MapGet("/incomes", ListIncomes);
        group.MapPut("/incomes/{id:long}", UpdateIncome);
        group.MapDelete("/incomes/{id:long}", DeleteIncome);
        return group;
    }

    private static async Task<IResult> CreateIncome(
        CreateIncomeRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Name is required.");

        if (req.DefaultAmount < 0)
            return Results.BadRequest("Default amount must be zero or greater.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var income = Income.Create(appUser.Id, req.Name, req.Kind, req.DefaultAmount, timeProvider.GetUtcNow());
        db.Incomes.Add(income);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/incomes/{income.Id}", new IncomeDto(income.Id, income.Name, income.Kind, income.DefaultAmount));
    }

    private static async Task<IResult> ListIncomes(
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
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var incomes = await db.Incomes
            .OrderBy(i => i.Name)
            .Select(i => new IncomeDto(i.Id, i.Name, i.Kind, i.DefaultAmount))
            .ToListAsync(ct);

        return Results.Ok(incomes);
    }

    private static async Task<IResult> UpdateIncome(
        long id,
        UpdateIncomeRequest req,
        System.Security.Claims.ClaimsPrincipal user,
        IUserProvisioningService provisioning,
        ICurrentOwner currentOwner,
        AppDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Name is required.");

        if (req.DefaultAmount < 0)
            return Results.BadRequest("Default amount must be zero or greater.");

        var firebaseUid = user.GetFirebaseUid();
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Results.Unauthorized();

        var appUser = await provisioning.GetOrCreateAsync(firebaseUid, user.GetEmail(), user.GetName(), ct);
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var income = await db.Incomes.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (income is null)
            return Results.NotFound();

        income.Update(req.Name, req.Kind, req.DefaultAmount);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new IncomeDto(income.Id, income.Name, income.Kind, income.DefaultAmount));
    }

    private static async Task<IResult> DeleteIncome(
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
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var income = await db.Incomes.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (income is null)
            return Results.NotFound();

        income.Deactivate();
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}

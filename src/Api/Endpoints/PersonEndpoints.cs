using Api.Identity;
using Application.Abstractions.Services;
using BillsBackend.Api.Contracts;
using Data.Contexts;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

internal static class PersonEndpoints
{
    public static RouteGroupBuilder MapPersonEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/persons", CreatePerson);
        group.MapGet("/persons", ListPersons);
        group.MapPut("/persons/{id:long}", UpdatePerson);
        group.MapDelete("/persons/{id:long}", DeletePerson);
        return group;
    }

    private static async Task<IResult> CreatePerson(
        CreatePersonRequest req,
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
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var person = Person.Create(appUser.Id, req.Name, timeProvider.GetUtcNow());
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/persons/{person.Id}", new PersonDto(person.Id, person.Name));
    }

    private static async Task<IResult> ListPersons(
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

        var persons = await db.Persons
            .OrderBy(p => p.Name)
            .Select(p => new PersonDto(p.Id, p.Name))
            .ToListAsync(ct);

        return Results.Ok(persons);
    }

    private static async Task<IResult> UpdatePerson(
        long id,
        UpdatePersonRequest req,
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
        currentOwner.SetCurrentOwnerId(appUser.Id);

        var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null)
            return Results.NotFound();

        person.Rename(req.Name);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new PersonDto(person.Id, person.Name));
    }

    private static async Task<IResult> DeletePerson(
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

        var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null)
            return Results.NotFound();

        person.Deactivate();
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}

using Api.Contracts;
using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;
using Data.Contexts;
using Domain.Abstractions.Filters;

namespace Api.Endpoints;

internal static class PersonEndpoints
{
    public static RouteGroupBuilder MapPersonEndpoints(this RouteGroupBuilder group)
    {
        var personGroup = group
        .MapGroup("/persons")
        .AddEndpointFilter<UserEndpointFilter>();

        personGroup.MapPost("", CreatePerson);
        personGroup.MapGet("", ListPersons);
        personGroup.MapPut("/{id:long}", UpdatePerson);
        personGroup.MapDelete("/{id:long}", DeletePerson);
        return group;
    }

    private static async Task<IResult> CreatePerson(
        CreatePersonRequest req,
        IPersonService personService,
        ICurrentOwner currentOwner,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var result = await personService.CreateAsync(req.Name, ct);

        if (result.IsFailure)
            return result.ToHttpResult();

        var person = result.Value;
        return Results.Created($"/api/v1/persons/{person.Id}", person);
    }

    private static async Task<IResult> ListPersons(
        IPersonService personService,
        CancellationToken ct)
    {
        var result = await personService.GetAllByNameAsync(ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdatePerson(
        long id,
        UpdatePersonRequest req,
        IPersonService personService,
        CancellationToken ct)
    {
        var result = await personService.UpdateAsync(id, req.Name, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> DeletePerson(
        long id,
        IPersonService personService,
        CancellationToken ct)
    {
        var result = await personService.DeleteByIdAsync(id, ct);
        return result.ToHttpResult();
    }
}

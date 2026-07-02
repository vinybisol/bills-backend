namespace BillsBackend.Api.Contracts;

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

namespace Api.Contracts;

/// <summary>The request body for <c>POST /persons</c>.</summary>
/// <param name="Name">The desired person name.</param>
internal sealed record CreatePersonRequest(string Name);

/// <summary>The request body for <c>PUT /persons/{id}</c>.</summary>
/// <param name="Name">The new name for the person.</param>
internal sealed record UpdatePersonRequest(string Name);

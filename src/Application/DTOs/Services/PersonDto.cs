namespace Application.DTOs.Services;

/// <summary>The payload returned by person read operations.</summary>
/// <param name="Id">The internal person id.</param>
/// <param name="Name">The person's display name.</param>
public sealed record PersonDto(long Id, string Name);
namespace Application.DTOs.Services;

/// <summary>The payload returned by category read operations.</summary>
/// <param name="Id">The internal category id.</param>
/// <param name="Name">The category display name.</param>
public sealed record CategoryDto(long Id, string Name);
namespace Application.DTOs.Services;

/// <summary>The internal profile of the authenticated user.</summary>
/// <param name="Id">The internal <c>app_user.id</c>.</param>
/// <param name="Name">The user's display name; <see cref="string.Empty"/> when no name claim was present.</param>
/// <param name="Email">The user's e-mail address, or <see langword="null"/> when the token carries no e-mail claim.</param>
public sealed record AppUserDto(long Id, string Name, string? Email);

using System.ComponentModel.DataAnnotations;

namespace BillsBackend.Api.Identity;

/// <summary>
/// Strongly-typed configuration for validating Firebase-issued JWTs.
/// </summary>
/// <remarks>
/// Bound from the <c>Firebase</c> configuration section. The Firebase project id drives
/// both the token issuer (<c>https://securetoken.google.com/&lt;project-id&gt;</c>) and the
/// expected audience (<c>&lt;project-id&gt;</c>).
/// </remarks>
public sealed class FirebaseAuthOptions
{
    /// <summary>
    /// The name of the configuration section that binds to this options type.
    /// </summary>
    public const string SectionName = "Firebase";

    /// <summary>
    /// Gets or sets the Firebase project identifier.
    /// </summary>
    /// <value>The project id used to derive the token issuer and audience.</value>
    [Required(AllowEmptyStrings = false)]
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the expected token issuer derived from <see cref="ProjectId"/>.
    /// </summary>
    /// <value>The Firebase secure-token issuer URL.</value>
    public string Issuer => $"https://securetoken.google.com/{ProjectId}";
}

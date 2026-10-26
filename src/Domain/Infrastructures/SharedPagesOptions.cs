using System.ComponentModel.DataAnnotations;

namespace Domain.Infrastructures;

public sealed record class SharedPagesOptions
{
    public const string SectionName = "SharedPages";

    [Required(AllowEmptyStrings = false)]
    public required string Secret { get; init; }
}

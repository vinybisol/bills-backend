using System.ComponentModel.DataAnnotations;

namespace Domain.Infrastructures;

public record class SharedPagesOptions
{
    public const string SectionName = "SharedPages";

    [Required(AllowEmptyStrings = false)]
    public string Secret { get; init; } = string.Empty;
}

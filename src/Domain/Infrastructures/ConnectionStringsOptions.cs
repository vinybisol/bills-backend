using System.ComponentModel.DataAnnotations;

namespace Domain.Infrastructures;

public sealed record ConnectionStringsOptions
{
    public static string SectionName => "ConnectionStrings";

    [Required(AllowEmptyStrings = false)]
    public required string Neon { get; init; }
}
namespace Domain.Infrastructures;

public sealed record AppOptions
{
    public required bool UseProdConnection { get; init; }
    public required ConnectionStringsOptions ConnectionStrings { get; init; }
}

public record ConnectionStringsOptions
{
    public required string Neon { get; init; }
    public required string NeonProd { get; init; }
}

public record FirebaseOptions
{
    public required string ProjectId { get; init; }
}
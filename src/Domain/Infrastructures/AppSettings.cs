namespace Domain.Infrastructures;

public record AppSettings
{
    public required bool UseProdConnection { get; init; }
    public required ConnectionStringsOptions ConnectionStrings { get; init; }


}

public record ConnectionStringsOptions
{
    public required string Neon { get; init; }
    public required string NeonProd { get; init; }
}
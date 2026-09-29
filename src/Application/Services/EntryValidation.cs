using Domain.Abstractions;

namespace Application.Services;

/// <summary>Input validation shared by the entry services.</summary>
internal static class EntryValidation
{
    internal const string YearField = "year";
    internal const string MonthField = "month";
    internal const string PlannedAmountField = "plannedAmount";
    internal const string ActualAmountField = "actualAmount";

    // Same bounds as the projection: entries outside them can never be projected.
    internal const int MinYear = ProjectionService.MinYear;
    internal const int MaxYear = ProjectionService.MaxYear;

    public static void AddPeriodErrors(List<Error> errors, int? year, int? month)
    {
        AddYearErrors(errors, year);

        if (month is null)
            errors.Add(new Error(MonthField, "Month is required.", ErrorType.Validation));
        else if (month is < 1 or > 12)
            errors.Add(new Error(MonthField, "Month must be between 1 and 12.", ErrorType.Validation));
    }

    public static void AddYearErrors(List<Error> errors, int? year)
    {
        if (year is null)
            errors.Add(new Error(YearField, "Year is required.", ErrorType.Validation));
        else if (year is < MinYear or > MaxYear)
            errors.Add(new Error(YearField, $"Year must be between {MinYear} and {MaxYear}.", ErrorType.Validation));
    }

    public static void AddNonNegativeError(List<Error> errors, decimal? amount, string field)
    {
        if (amount is < 0m)
            errors.Add(new Error(field, $"{char.ToUpperInvariant(field[0])}{field[1..]} must be zero or greater.", ErrorType.Validation));
    }

    public static ValidationError? ToValidationError(List<Error> errors) =>
        errors.Count == 0 ? null : new ValidationError([.. errors]);
}

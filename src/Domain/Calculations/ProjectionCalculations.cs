namespace Domain.Calculations;

/// <summary>Pure helpers for the annual projection of recurring templates.</summary>
public static class ProjectionCalculations
{
    /// <summary>Number of months projected per template per year.</summary>
    public const int MonthsPerYear = 12;

    /// <summary>
    /// Returns, in ascending order, the months (1–12) that have no entry yet for
    /// <paramref name="templateId"/> according to <paramref name="existing"/> (template id, month) pairs.
    /// </summary>
    public static IEnumerable<int> MissingMonths(long templateId, IReadOnlySet<(long TemplateId, int Month)> existing) =>
        Enumerable.Range(1, MonthsPerYear).Where(month => !existing.Contains((templateId, month)));
}

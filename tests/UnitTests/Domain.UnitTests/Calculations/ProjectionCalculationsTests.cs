using Domain.Calculations;

namespace Domain.UnitTests.Calculations;

[TestFixture]
public sealed class ProjectionCalculationsTests
{
    [Test]
    public void MissingMonths_NoExistingEntries_ReturnsAllTwelveMonthsInOrder() =>
        Assert.That(
            ProjectionCalculations.MissingMonths(1L, new HashSet<(long, int)>()),
            Is.EqualTo(Enumerable.Range(1, 12)));

    [Test]
    public void MissingMonths_SomeMonthsExist_ReturnsOnlyTheGaps()
    {
        // Arrange
        var existing = new HashSet<(long, int)> { (1L, 1), (1L, 6), (1L, 12) };

        // Act & Assert
        Assert.That(
            ProjectionCalculations.MissingMonths(1L, existing),
            Is.EqualTo(new[] { 2, 3, 4, 5, 7, 8, 9, 10, 11 }));
    }

    [Test]
    public void MissingMonths_AllMonthsExist_ReturnsEmpty()
    {
        // Arrange
        var existing = Enumerable.Range(1, 12).Select(m => (1L, m)).ToHashSet();

        // Act & Assert
        Assert.That(ProjectionCalculations.MissingMonths(1L, existing), Is.Empty);
    }

    [Test]
    public void MissingMonths_EntriesOfOtherTemplate_AreIgnored()
    {
        // Arrange
        var existing = Enumerable.Range(1, 12).Select(m => (2L, m)).ToHashSet();

        // Act & Assert
        Assert.That(ProjectionCalculations.MissingMonths(1L, existing).Count(), Is.EqualTo(ProjectionCalculations.MonthsPerYear));
    }
}

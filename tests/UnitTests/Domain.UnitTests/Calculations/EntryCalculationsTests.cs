using Domain.Calculations;

namespace Domain.UnitTests.Calculations;

[TestFixture]
public sealed class EntryCalculationsTests
{
    // --- EffectiveAmount ---

    [Test]
    public void EffectiveAmount_WithActual_ReturnsActual() =>
        Assert.That(EntryCalculations.EffectiveAmount(planned: 1000m, actual: 850m), Is.EqualTo(850m));

    [Test]
    public void EffectiveAmount_ActualZero_ReturnsZeroNotPlanned() =>
        Assert.That(EntryCalculations.EffectiveAmount(planned: 1000m, actual: 0m), Is.Zero);

    [Test]
    public void EffectiveAmount_WithoutActual_ReturnsPlanned() =>
        Assert.That(EntryCalculations.EffectiveAmount(planned: 1000m, actual: null), Is.EqualTo(1000m));

    // --- MyShare / Receivable ---

    [TestCase(500, 1, 500)]
    [TestCase(500, 0.5, 250)]
    [TestCase(500, 0, 0)]
    [TestCase(1200, 0.25, 300)]
    public void MyShare_SplitRatio_ReturnsOwnersFraction(decimal effective, decimal splitRatio, decimal expected) =>
        Assert.That(EntryCalculations.MyShare(effective, splitRatio), Is.EqualTo(expected));

    [TestCase(500, 1, 0)]
    [TestCase(500, 0.5, 250)]
    [TestCase(500, 0, 500)]
    [TestCase(1200, 0.25, 900)]
    public void Receivable_SplitRatio_ReturnsOtherPersonsFraction(decimal effective, decimal splitRatio, decimal expected) =>
        Assert.That(EntryCalculations.Receivable(effective, splitRatio), Is.EqualTo(expected));

    [TestCase(1234.56, 0.3)]
    [TestCase(99.99, 0.5)]
    [TestCase(0, 0.7)]
    public void MyShareAndReceivable_AnySplit_SumToEffective(decimal effective, decimal splitRatio) =>
        Assert.That(
            EntryCalculations.MyShare(effective, splitRatio) + EntryCalculations.Receivable(effective, splitRatio),
            Is.EqualTo(effective));

    // --- IsInForwardRange ---

    [TestCase(2026, 7, 2026, 7, true, TestName = "IsInForwardRange_SameMonth_IsIncluded")]
    [TestCase(2026, 12, 2026, 7, true, TestName = "IsInForwardRange_LaterMonthSameYear_IsIncluded")]
    [TestCase(2027, 1, 2026, 7, true, TestName = "IsInForwardRange_NextYearEarlierMonth_IsIncluded")]
    [TestCase(2026, 6, 2026, 7, false, TestName = "IsInForwardRange_PreviousMonth_IsExcluded")]
    [TestCase(2025, 12, 2026, 7, false, TestName = "IsInForwardRange_PreviousYearLaterMonth_IsExcluded")]
    public void IsInForwardRange_EntryVersusFrom_ReturnsExpected(int entryYear, int entryMonth, int fromYear, int fromMonth, bool expected) =>
        Assert.That(EntryCalculations.IsInForwardRange(entryYear, entryMonth, fromYear, fromMonth), Is.EqualTo(expected));

    // --- IsInPeriod ---

    [TestCase(2026, 7, null, null, null, null, true, TestName = "IsInPeriod_NoBounds_IsIncluded")]
    [TestCase(2026, 7, 2026, 7, 2026, 7, true, TestName = "IsInPeriod_OnBothBoundaries_IsIncluded")]
    [TestCase(2026, 6, 2026, 7, null, null, false, TestName = "IsInPeriod_BeforeFrom_IsExcluded")]
    [TestCase(2026, 8, null, null, 2026, 7, false, TestName = "IsInPeriod_AfterTo_IsExcluded")]
    [TestCase(2026, 1, 2025, 11, 2026, 2, true, TestName = "IsInPeriod_InsideCrossYearRange_IsIncluded")]
    [TestCase(2026, 6, 2026, null, null, null, true, TestName = "IsInPeriod_FromMonthMissing_FromBoundIgnored")]
    [TestCase(2026, 8, null, null, null, 7, true, TestName = "IsInPeriod_ToYearMissing_ToBoundIgnored")]
    [TestCase(2026, 5, 2026, 7, 2026, 3, false, TestName = "IsInPeriod_InvertedRange_IsExcluded")]
    public void IsInPeriod_EntryVersusBounds_ReturnsExpected(
        int entryYear, int entryMonth, int? fromYear, int? fromMonth, int? toYear, int? toMonth, bool expected) =>
        Assert.That(EntryCalculations.IsInPeriod(entryYear, entryMonth, fromYear, fromMonth, toYear, toMonth), Is.EqualTo(expected));

    // --- ResolveEventInstant ---

    [Test]
    public void ResolveEventInstant_WithDate_ReturnsMidnightUtcOfDate()
    {
        // Arrange
        var fallback = new DateTimeOffset(2026, 7, 5, 15, 30, 0, TimeSpan.FromHours(-3));

        // Act
        var instant = EntryCalculations.ResolveEventInstant(new DateOnly(2026, 7, 1), fallback);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(instant, Is.EqualTo(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(instant.Offset, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [Test]
    public void ResolveEventInstant_WithoutDate_ReturnsFallback()
    {
        // Arrange
        var fallback = new DateTimeOffset(2026, 7, 5, 15, 30, 0, TimeSpan.Zero);

        // Act & Assert
        Assert.That(EntryCalculations.ResolveEventInstant(null, fallback), Is.EqualTo(fallback));
    }

    // --- ComputeVariation ---

    [Test]
    public void ComputeVariation_NoPrevious_ReturnsNull() =>
        Assert.That(EntryCalculations.ComputeVariation(current: 150m, previous: null), Is.Null);

    [TestCase(152, 150, 2, 1.33)]
    [TestCase(90, 100, -10, -10)]
    [TestCase(100, 100, 0, 0)]
    [TestCase(400, 300, 100, 33.33)]
    [TestCase(200, 300, -100, -33.33)]
    public void ComputeVariation_WithPrevious_ReturnsAbsoluteAndRoundedPercent(
        decimal current, decimal previous, decimal expectedAbs, decimal expectedPct)
    {
        // Act
        var variation = EntryCalculations.ComputeVariation(current, previous);

        // Assert
        Assert.That(variation, Is.EqualTo(new EntryCalculations.Variation(expectedAbs, expectedPct)));
    }

    [Test]
    public void ComputeVariation_PreviousZero_ReturnsAbsoluteWithNullPercent() =>
        Assert.That(
            EntryCalculations.ComputeVariation(current: 50m, previous: 0m),
            Is.EqualTo(new EntryCalculations.Variation(50m, null)));
}

using BillsBackend.Api.Domain;

namespace Api.UnitTests.Domain;

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

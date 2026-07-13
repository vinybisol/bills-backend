using BillsBackend.Api.Domain;

namespace BillsBackend.UnitTests;

/// <summary>
/// Unit tests for <see cref="EntryCalculations"/>: effective amount, owner share, and receivable fraction.
/// </summary>
[TestFixture]
public sealed class EntryCalculationsTests
{
    // --- EffectiveAmount ---

    [Test]
    public void EffectiveAmount_WithActualAmount_ReturnsActual()
    {
        // Arrange / Act
        var result = EntryCalculations.EffectiveAmount(planned: 1000m, actual: 850m);

        // Assert
        Assert.That(result, Is.EqualTo(850m));
    }

    [Test]
    public void EffectiveAmount_WithNullActual_ReturnsPlanned()
    {
        // Arrange / Act
        var result = EntryCalculations.EffectiveAmount(planned: 1000m, actual: null);

        // Assert
        Assert.That(result, Is.EqualTo(1000m));
    }

    // --- MyShare ---

    [Test]
    public void MyShare_SplitRatio1_EqualsEffective()
    {
        // Arrange / Act
        var result = EntryCalculations.MyShare(effective: 500m, splitRatio: 1m);

        // Assert
        Assert.That(result, Is.EqualTo(500m));
    }

    [Test]
    public void MyShare_SplitRatio05_EqualsHalf()
    {
        // Arrange / Act
        var result = EntryCalculations.MyShare(effective: 500m, splitRatio: 0.5m);

        // Assert
        Assert.That(result, Is.EqualTo(250m));
    }

    [Test]
    public void MyShare_SplitRatio0_EqualsZero()
    {
        // Arrange / Act
        var result = EntryCalculations.MyShare(effective: 500m, splitRatio: 0m);

        // Assert
        Assert.That(result, Is.EqualTo(0m));
    }

    // --- Receivable ---

    [Test]
    public void Receivable_SplitRatio1_EqualsZero()
    {
        // Arrange / Act
        var result = EntryCalculations.Receivable(effective: 500m, splitRatio: 1m);

        // Assert
        Assert.That(result, Is.EqualTo(0m));
    }

    [Test]
    public void Receivable_SplitRatio05_EqualsHalf()
    {
        // Arrange / Act
        var result = EntryCalculations.Receivable(effective: 500m, splitRatio: 0.5m);

        // Assert
        Assert.That(result, Is.EqualTo(250m));
    }

    [Test]
    public void Receivable_SplitRatio0_EqualsEffective()
    {
        // Arrange / Act
        var result = EntryCalculations.Receivable(effective: 500m, splitRatio: 0m);

        // Assert
        Assert.That(result, Is.EqualTo(500m));
    }

    // --- ComputeVariation ---

    [Test]
    public void ComputeVariation_NoPrevious_ReturnsNull()
    {
        // Arrange / Act
        var result = EntryCalculations.ComputeVariation(current: 150m, previous: null);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ComputeVariation_Increase_ReturnsPositiveAbsAndPct()
    {
        // Arrange / Act — mirrors the issue's example: 150 -> 152 is +2.00 (+1.33%)
        var result = EntryCalculations.ComputeVariation(current: 152m, previous: 150m);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Value.Abs, Is.EqualTo(2m));
            Assert.That(result.Value.Pct, Is.EqualTo(1.33m));
        });
    }

    [Test]
    public void ComputeVariation_Decrease_ReturnsNegativeAbsAndPct()
    {
        // Arrange / Act
        var result = EntryCalculations.ComputeVariation(current: 90m, previous: 100m);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Value.Abs, Is.EqualTo(-10m));
            Assert.That(result.Value.Pct, Is.EqualTo(-10m));
        });
    }

    [Test]
    public void ComputeVariation_PreviousZero_PctIsNull()
    {
        // Arrange / Act — a percentage change relative to zero is undefined.
        var result = EntryCalculations.ComputeVariation(current: 50m, previous: 0m);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result!.Value.Abs, Is.EqualTo(50m));
            Assert.That(result.Value.Pct, Is.Null);
        });
    }
}

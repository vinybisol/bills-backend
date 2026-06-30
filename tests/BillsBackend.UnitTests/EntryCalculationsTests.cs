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
}

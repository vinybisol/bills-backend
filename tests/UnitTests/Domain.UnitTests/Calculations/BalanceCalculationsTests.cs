using Domain.Calculations;

namespace Domain.UnitTests.Calculations;

[TestFixture]
public sealed class BalanceCalculationsTests
{
    [Test]
    public void ComputeMonthBalance_AllZero_ReturnsZeroBalances() =>
        Assert.That(
            BalanceCalculations.ComputeMonthBalance(0m, 0m, 0m, 0m, 0m, 0m),
            Is.EqualTo(new MonthBalance(0m, 0m, 0m)));

    [Test]
    public void ComputeMonthBalance_TypicalMonth_ComputesOptimisticWorstCaseAndRealized()
    {
        // Act
        var balance = BalanceCalculations.ComputeMonthBalance(
            plannedIncome: 5000m,
            plannedMyShare: 3000m,
            receivablePending: 400m,
            receivedIncome: 4800m,
            receivableReceived: 200m,
            paidFull: 3500m);

        // Assert
        Assert.That(balance, Is.EqualTo(new MonthBalance(
            PlannedOptimistic: 2000m,
            PlannedWorstCase: 1600m,
            Realized: 1500m)));
    }

    [Test]
    public void ComputeMonthBalance_ExpensesExceedIncome_ReturnsNegativeBalances()
    {
        // Act
        var balance = BalanceCalculations.ComputeMonthBalance(1000m, 1500m, 100m, 0m, 0m, 800m);

        // Assert
        Assert.That(balance, Is.EqualTo(new MonthBalance(-500m, -600m, -800m)));
    }

    [TestCase(0, 2000)]
    [TestCase(2000, 0)]
    public void ComputeMonthBalance_PendingReceivable_OnlyAffectsWorstCase(decimal pending, decimal expectedWorstCase)
    {
        // Act
        var balance = BalanceCalculations.ComputeMonthBalance(3000m, 1000m, pending, 0m, 0m, 0m);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(balance.PlannedOptimistic, Is.EqualTo(2000m));
            Assert.That(balance.PlannedWorstCase, Is.EqualTo(expectedWorstCase));
            Assert.That(balance.Realized, Is.Zero);
        }
    }
}

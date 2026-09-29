namespace Domain.Calculations;

/// <summary>The three month balances ("saldos") shown to the owner.</summary>
/// <param name="PlannedOptimistic">Planned income minus my planned share — assumes everyone pays back.</param>
/// <param name="PlannedWorstCase">Optimistic balance minus the pending receivable — assumes nobody pays back.</param>
/// <param name="Realized">Received income plus received reimbursements minus the full amount paid.</param>
public readonly record struct MonthBalance(decimal PlannedOptimistic, decimal PlannedWorstCase, decimal Realized);

/// <summary>Pure balance ("saldo") computations.</summary>
public static class BalanceCalculations
{
    /// <summary>Computes the optimistic, worst-case and realized balances of a month.</summary>
    public static MonthBalance ComputeMonthBalance(
        decimal plannedIncome,
        decimal plannedMyShare,
        decimal receivablePending,
        decimal receivedIncome,
        decimal receivableReceived,
        decimal paidFull)
    {
        var optimistic = plannedIncome - plannedMyShare;
        return new MonthBalance(
            optimistic,
            optimistic - receivablePending,
            receivedIncome + receivableReceived - paidFull);
    }
}

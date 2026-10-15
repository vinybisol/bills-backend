namespace Domain.UnitTests.TestSupport;

/// <summary>
/// Shared <see cref="TestCaseSourceAttribute"/> data: strings that fail a "not null, empty or whitespace" guard.
/// </summary>
internal static class InvalidStrings
{
    public static IEnumerable<TestCaseData> Cases
    {
        get
        {
            yield return new TestCaseData((object?)null).SetArgDisplayNames("null");
            yield return new TestCaseData(string.Empty).SetArgDisplayNames("empty");
            yield return new TestCaseData("   ").SetArgDisplayNames("whitespace");
        }
    }
}

namespace UnitTestCommon;

public static class InvalidEntityIds
{
    public static IEnumerable<TestCaseData> Cases
    {
        get
        {
            yield return new TestCaseData(0L).SetArgDisplayNames("Zero");
            yield return new TestCaseData(-1L).SetArgDisplayNames("Minus one(-1), negative one(-1)");
        }
    }
}
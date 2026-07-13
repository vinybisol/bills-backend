using System.Collections;

namespace TestCommon.TestData;

public class InvalidStrings : IEnumerable<TheoryDataRow<string>>
{
    public IEnumerator<TheoryDataRow<string>> GetEnumerator()
    {
        yield return new("");
        yield return new("     ");
        yield return new(null!);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
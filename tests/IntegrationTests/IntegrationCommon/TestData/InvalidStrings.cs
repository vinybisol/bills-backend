using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace IntegrationCommon.TestData;

[ExcludeFromCodeCoverage]
public class InvalidStrings : IEnumerable<TheoryDataRow<string>>
{
    public IEnumerator<TheoryDataRow<string>> GetEnumerator()
    {
        yield return new("");
        yield return new("     ");
        yield return new(null!);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
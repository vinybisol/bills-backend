using System.Diagnostics.CodeAnalysis;

namespace UnitTestCommon;

[TestFixture]
[ExcludeFromCodeCoverage]
public sealed class FakeTest
{
    [Test]
    public void FirstTest()
    {
        Assert.True(true);
    }
}

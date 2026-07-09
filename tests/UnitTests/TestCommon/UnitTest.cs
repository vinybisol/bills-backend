using System.Diagnostics.CodeAnalysis;

namespace TestCommon;

[ExcludeFromCodeCoverage]
public class UnitTest
{
    [Fact]
    public void JustRun()
    {
        Assert.True(true);
    }
}
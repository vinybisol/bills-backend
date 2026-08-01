using System.Diagnostics.CodeAnalysis;

namespace IntegrationCommon;

[ExcludeFromCodeCoverage]
public class UnitTest
{
    [Fact]
    public void JustRun() => Assert.True(true);
}

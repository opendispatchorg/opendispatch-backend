namespace OpenDispatch.Domain.Tests;

/// <summary>
/// Confirms the test harness discovers and runs tests. The Domain has no types yet;
/// real domain tests arrive from step 5 onward.
/// </summary>
public class TestHarnessTests
{
    [Fact]
    public void TestHarnessRuns()
    {
        Assert.Equal(4, 2 + 2);
    }
}

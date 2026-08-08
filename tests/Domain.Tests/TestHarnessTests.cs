using OpenDispatch.TestSupport;

namespace OpenDispatch.Domain.Tests;

/// <summary>
/// The representative <c>Unit</c> test: pure, no I/O, and the shape everything under
/// <c>make test-fast</c> follows. The Domain has no types yet; real domain tests arrive
/// from step 5 onward.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public class TestHarnessTests
{
    [Fact]
    public void TestHarnessRuns()
    {
        Assert.Equal(4, 2 + 2);
    }
}

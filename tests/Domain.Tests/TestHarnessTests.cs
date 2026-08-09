using OpenDispatch.TestSupport;

namespace OpenDispatch.Domain.Tests;

/// <summary>
/// The representative <c>Unit</c> test: pure, no I/O, and the shape everything under
/// <c>make test-fast</c> follows. It asserts nothing about the domain on purpose — it is
/// here to fail loudly if the harness itself stops running.
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

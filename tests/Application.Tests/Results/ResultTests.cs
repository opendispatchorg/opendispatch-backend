using OpenDispatch.Application.Results;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Results;

/// <summary>
/// The one thing about <see cref="Result{TValue}"/> that is a decision rather than a getter:
/// what a failure hands back when somebody asks for the value it does not have.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class ResultTests
{
    [Fact]
    public void AFailureRefusesToHandOutAValueItDoesNotHave()
    {
        var result = Result.Failure<string>(Error.NotFound("customer.notFound", "No such customer."));

        // A default would travel on and fail somewhere that cannot explain itself, which is the
        // substitution this type exists to prevent.
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("customer.notFound", thrown.Message, StringComparison.Ordinal);
    }
}

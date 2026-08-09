using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Domain.Tests.Identifiers;

/// <summary>
/// The main thing these ids buy — that a <see cref="TechnicianId"/> cannot be passed where
/// a <see cref="JobId"/> is expected — is enforced by the compiler and needs no test. What
/// is left is the two behaviours each one has, checked across all six in one place rather
/// than in six near-identical files.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class StronglyTypedIdTests
{
    [Fact]
    public void NewMintsADistinctIdentifierEachTime()
    {
        Assert.NotEqual(JobId.New(), JobId.New());
        Assert.NotEqual(AssignmentId.New(), AssignmentId.New());
        Assert.NotEqual(TechnicianId.New(), TechnicianId.New());
        Assert.NotEqual(CustomerId.New(), CustomerId.New());
        Assert.NotEqual(InvoiceId.New(), InvoiceId.New());
        Assert.NotEqual(OrgId.New(), OrgId.New());
    }

    [Fact]
    public void FromRoundTripsTheUnderlyingGuid()
    {
        var value = Guid.NewGuid();

        Assert.Equal(value, JobId.From(value).Value);
        Assert.Equal(value, AssignmentId.From(value).Value);
        Assert.Equal(value, TechnicianId.From(value).Value);
        Assert.Equal(value, CustomerId.From(value).Value);
        Assert.Equal(value, InvoiceId.From(value).Value);
        Assert.Equal(value, OrgId.From(value).Value);
    }
}

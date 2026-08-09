using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Organizations;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Domain.Tests.Organizations;

/// <summary>
/// There is barely anything here to get wrong, but the one thing that would matter is the
/// identity: every business record in the system is scoped by it.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class OrganizationTests
{
    [Fact]
    public void EachOrganizationGetsItsOwnIdentity()
    {
        // If Create ever handed back a default or shared id, every tenant would resolve to
        // the same scope and the global query filters would stop separating anything.
        var first = Organization.Create("Vance Refrigeration");
        var second = Organization.Create("Lakeside Heating");

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(OrgId.From(Guid.Empty), first.Id);
    }

    [Fact]
    public void CreateNamesTheBusinessAndAnnouncesNothing()
    {
        var organization = Organization.Create("  Vance Refrigeration  ");

        Assert.Equal("Vance Refrigeration", organization.Name);
        Assert.Empty(organization.DomainEvents);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsAnOrganizationWithNoName(string name)
    {
        Assert.Throws<DomainException>(() => Organization.Create(name));
    }
}

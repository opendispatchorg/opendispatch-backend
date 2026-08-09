using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Customers;

/// <summary>
/// Locations are owned, so the thing worth checking is that the boundary holds: they only
/// come into being through the customer, and taking one away that was never there is
/// refused rather than quietly shrugged off.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class CustomerTests
{
    private static readonly GeoPoint Somewhere = new(51.5074d, -0.1278d);

    [Fact]
    public void ANewCustomerHasNowhereToVisitYet()
    {
        var customer = CustomerBuilder.Any().Build();

        Assert.Empty(customer.Locations);
    }

    [Fact]
    public void AddingALocationHandsBackTheIdAJobWouldPointAt()
    {
        var customer = CustomerBuilder.Any().Build();

        var locationId = customer.AddLocation("Home", "12 Rillington Place, London", Somewhere);

        var location = Assert.Single(customer.Locations);
        Assert.Equal(locationId, location.Id);
        Assert.Equal("Home", location.Label);
        Assert.Equal("12 Rillington Place, London", location.Address);
        Assert.Equal(Somewhere, location.Point);
    }

    [Fact]
    public void ACustomerCanHaveSeveralPlacesToVisit()
    {
        var customer = CustomerBuilder.Any().Build();

        customer.AddLocation("Head office", "1 High Street, London", Somewhere);
        customer.AddLocation("Warehouse", "2 Dock Road, London", new GeoPoint(51.51d, -0.12d));

        Assert.Equal(2, customer.Locations.Count);
    }

    [Fact]
    public void EachLocationGetsItsOwnIdentity()
    {
        var customer = CustomerBuilder.Any().Build();

        var first = customer.AddLocation("Head office", "1 High Street, London", Somewhere);
        var second = customer.AddLocation("Warehouse", "2 Dock Road, London", Somewhere);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void RemovingALocationLeavesTheOthersAlone()
    {
        var customer = CustomerBuilder.Any().Build();
        var office = customer.AddLocation("Head office", "1 High Street, London", Somewhere);
        var warehouse = customer.AddLocation("Warehouse", "2 Dock Road, London", Somewhere);

        customer.RemoveLocation(office);

        var remaining = Assert.Single(customer.Locations);
        Assert.Equal(warehouse, remaining.Id);
    }

    [Fact]
    public void RemovingALocationTheCustomerNeverHadIsRefused()
    {
        var customer = CustomerBuilder.Any().Build();
        customer.AddLocation("Head office", "1 High Street, London", Somewhere);

        Assert.Throws<DomainException>(() => customer.RemoveLocation(ServiceLocationId.New()));
    }

    [Fact]
    public void RemovingTheSameLocationTwiceIsRefusedTheSecondTime()
    {
        var customer = CustomerBuilder.Any().Build();
        var office = customer.AddLocation("Head office", "1 High Street, London", Somewhere);
        customer.RemoveLocation(office);

        Assert.Throws<DomainException>(() => customer.RemoveLocation(office));
    }

    [Theory]
    [InlineData("", "1 High Street, London")]
    [InlineData("   ", "1 High Street, London")]
    [InlineData("Head office", "")]
    [InlineData("Head office", "   ")]
    public void RejectsALocationNobodyCouldFind(string label, string address)
    {
        var customer = CustomerBuilder.Any().Build();

        Assert.Throws<DomainException>(() => customer.AddLocation(label, address, Somewhere));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsACustomerWithNoName(string name)
    {
        Assert.Throws<DomainException>(() => CustomerBuilder.Any().Named(name).Build());
    }

    [Fact]
    public void BlankContactDetailsAreRecordedAsAbsentRatherThanEmpty()
    {
        // An untouched form field must not become a phone number of "".
        var customer = CustomerBuilder.Any().Reachable(new ContactInfo("  ", string.Empty)).Build();

        Assert.Null(customer.Contact.Email);
        Assert.Null(customer.Contact.Phone);
    }
}

using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Customers;

/// <summary>
/// Erasure: what goes, what stays, and what may no longer be done.
/// </summary>
/// <remarks>
/// The rule a shop is answerable for, so it is pinned here rather than left to the handler that
/// calls it. Every field a customer's record holds about a person is checked by name — a test that
/// only looked at the name would pass a version that left the phone number and the address of the
/// house behind, which is the failure that matters.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class CustomerErasureTests
{
    private static readonly GeoPoint Home = new(51.5074d, -0.1278d);
    private static readonly DateTimeOffset Asked = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ErasingTakesTheNameTheContactDetailsAndEveryAddress()
    {
        var customer = CustomerBuilder.Any()
            .Named("Dana Whitlock")
            .Reachable(new ContactInfo("dana@whitlock.example", "+44 20 7946 0000"))
            .Build();

        customer.AddLocation("Home", "12 Rillington Place, London", Home);
        customer.AddLocation("The shop", "44 Bath Road, Slough", new GeoPoint(51.5107d, -0.5950d));

        customer.Erase(Asked);

        Assert.Equal(Tombstone.Text, customer.Name);
        Assert.Null(customer.Contact.Email);
        Assert.Null(customer.Contact.Phone);
        Assert.Equal(Asked, customer.ErasedAt);
        Assert.True(customer.IsErased);

        // Including the coordinates: where somebody lives identifies them as surely as the address.
        Assert.All(customer.Locations, location =>
        {
            Assert.Equal(Tombstone.Text, location.Label);
            Assert.Equal(Tombstone.Text, location.Address);
            Assert.Equal(Tombstone.Point, location.Point);
        });
    }

    /// <summary>
    /// The locations stay, tombstoned, rather than being removed: a job points at one by id from
    /// outside this aggregate, and taking them away would leave every one of that customer's jobs
    /// referencing a site that no longer exists.
    /// </summary>
    [Fact]
    public void ErasingKeepsTheLocationsAJobStillPointsAt()
    {
        var customer = CustomerBuilder.Any().Build();
        var home = customer.AddLocation("Home", "12 Rillington Place, London", Home);

        customer.Erase(Asked);

        Assert.Equal(home, Assert.Single(customer.Locations).Id);
    }

    /// <summary>
    /// Nothing puts them back. Each of these is a way somebody could be written into the record
    /// again — a name retyped, a new site added, an old one corrected — and the aggregate is where
    /// that has to be refused, because every one of them has a different caller.
    /// </summary>
    [Fact]
    public void AnErasedCustomerCannotBeEditedBackIntoExistence()
    {
        var customer = CustomerBuilder.Any().Build();
        var home = customer.AddLocation("Home", "12 Rillington Place, London", Home);

        customer.Erase(Asked);

        Assert.Throws<DomainException>(() => customer.Rename("Dana Whitlock"));
        Assert.Throws<DomainException>(
            () => customer.SetContact(new ContactInfo("dana@whitlock.example", null)));
        Assert.Throws<DomainException>(
            () => customer.AddLocation("Home", "12 Rillington Place, London", Home));
        Assert.Throws<DomainException>(
            () => customer.UpdateLocation(home, "Home", "12 Rillington Place, London", Home));
        Assert.Throws<DomainException>(() => customer.RemoveLocation(home));
    }

    /// <summary>
    /// Erasing twice is not an error, and the second one does not move the date: an operator
    /// answering a legal request may well retry it, and "already erased" is what they wanted.
    /// </summary>
    [Fact]
    public void ErasingAgainChangesNothing()
    {
        var customer = CustomerBuilder.Any().Build();

        customer.Erase(Asked);
        customer.Erase(Asked.AddDays(30));

        Assert.Equal(Asked, customer.ErasedAt);
    }
}

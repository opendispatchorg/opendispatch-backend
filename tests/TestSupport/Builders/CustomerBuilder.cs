using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.TestSupport.Builders;

/// <summary>
/// Builds <see cref="Customer"/>s for tests.
/// </summary>
/// <example>
/// <code>
/// var customer = CustomerBuilder.Any().Named("Vance Refrigeration").Build();
/// </code>
/// </example>
/// <remarks>
/// Locations are not part of the builder: adding one is usually the act a test is about,
/// and <see cref="Customer.AddLocation"/> returns the id a test then needs.
/// </remarks>
public sealed record CustomerBuilder
{
    private OrgId Org { get; init; } = OrgId.New();

    private string Name { get; init; } = "Vance Refrigeration";

    private ContactInfo Contact { get; init; } = new("hello@vance.example", "+44 20 7946 0000");

    /// <summary>An ordinary customer with contact details and no locations yet.</summary>
    public static CustomerBuilder Any() => new();

    /// <summary>Puts the customer in a particular tenant.</summary>
    public CustomerBuilder ForOrg(OrgId org) => this with { Org = org };

    /// <summary>Names the customer.</summary>
    public CustomerBuilder Named(string name) => this with { Name = name };

    /// <summary>Sets how to reach them.</summary>
    public CustomerBuilder Reachable(ContactInfo contact) => this with { Contact = contact };

    /// <summary>Creates the customer.</summary>
    public Customer Build() => Customer.Create(Org, Name, Contact);
}

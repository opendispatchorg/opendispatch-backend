using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Customers.GetCustomer;
using OpenDispatch.Application.Customers.ListCustomers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Customers;

/// <summary>
/// The Customers slice against a real database, through the registrations the host uses.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests run the slice over fakes, which settles what the handlers and validators do.
/// What only Postgres can settle is whether the work survives the request: that the pipeline
/// committed, that an owned collection came back with its root without an <c>Include</c> anywhere,
/// and that a coordinate is still the same place after a round trip through PostGIS.
/// </para>
/// <para>
/// Each request runs in its own scope, so each gets its own <c>DbContext</c> — the reads are reads
/// from the database and not from a change tracker that still remembers the write.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class CustomersFlowTests
{
    private readonly OrgId _tenant = OrgId.New();
    private readonly OrgId _rival = OrgId.New();
    private readonly PostgresFixture _postgres;

    public CustomersFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task TakesOnACustomerGivesThemALocationAndReadsBothBackOutOfPostgres()
    {
        await using var services = BuildHost();

        var created = await Send(
            services,
            new CreateCustomerCommand("Vance Refrigeration", "hello@vance.example", "+44 20 7946 0000"));
        Assert.True(created.IsSuccess);

        var added = await Send(services, new AddServiceLocationCommand(
            created.Value,
            "Head office",
            "12 Bath Road, Slough",
            51.5107,
            -0.5950));
        Assert.True(added.IsSuccess);

        var fetched = await Send(services, new GetCustomerQuery(created.Value));

        Assert.True(fetched.IsSuccess);
        Assert.Equal("Vance Refrigeration", fetched.Value.Name);
        Assert.Equal("hello@vance.example", fetched.Value.Email);

        var location = Assert.Single(fetched.Value.Locations);
        Assert.Equal(added.Value, location.Id);
        Assert.Equal("12 Bath Road, Slough", location.Address);
        Assert.Equal(51.5107, location.Latitude);
        Assert.Equal(-0.5950, location.Longitude);

        var listed = await Send(services, new ListCustomersQuery());
        Assert.Equal(created.Value, Assert.Single(listed.Value.Items).Id);
        Assert.Equal(1, listed.Value.Total);
    }

    /// <summary>
    /// The write side of tenancy, which the query filters cannot enforce on their own.
    /// </summary>
    /// <remarks>
    /// The customer is created by one organization and asked for by another. A handler that took
    /// the organization from anywhere but <c>ITenantContext</c> would fail this in one of two
    /// directions — the stranger would find it, or the creator would not.
    /// </remarks>
    [Fact]
    public async Task ACustomerBelongsToTheTenantThatTookThemOnAndToNobodyElse()
    {
        await using var services = BuildHost();

        var created = await Send(services, new CreateCustomerCommand("Ivy Fabrication", null, null));

        var stranger = await Send(services, new GetCustomerQuery(created.Value), _rival);
        Assert.True(stranger.IsFailure);
        Assert.Equal(CustomerErrors.NotFoundCode, stranger.Error!.Code);

        Assert.Empty((await Send(services, new ListCustomersQuery(), _rival)).Value.Items);
        Assert.NotEmpty((await Send(services, new ListCustomersQuery())).Value.Items);
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(
        ServiceProvider services,
        IRequest<TResponse> request,
        OrgId? actingAs = null)
    {
        using var scope = services.ActingAs(actingAs ?? _tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}

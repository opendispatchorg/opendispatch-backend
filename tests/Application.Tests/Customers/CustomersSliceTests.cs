using OpenDispatch.Application.Customers;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Customers.GetCustomer;
using OpenDispatch.Application.Customers.ListCustomers;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Customers;

/// <summary>
/// The four requests of the Customers slice, sent through the real pipeline.
/// </summary>
/// <remarks>
/// One flow covers the CRUD, as <c>TESTING.md</c> asks — creating a customer, giving them a
/// location and reading both back is the sequence a dispatcher actually performs, and it breaks
/// for real reasons. What is tested one case at a time is what is not CRUD: which layer refuses
/// bad input, what a request that names nothing existing gets back, and whose books the new
/// customer lands on.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class CustomersSliceTests
{
    [Fact]
    public async Task TakesOnACustomerGivesThemALocationAndReadsBothBack()
    {
        await using var slice = SliceHost.Customers();

        var created = await slice.Send(
            new CreateCustomerCommand("Vance Refrigeration", "hello@vance.example", "+44 20 7946 0000"));
        Assert.True(created.IsSuccess);

        var added = await slice.Send(new AddServiceLocationCommand(
            created.Value,
            "Head office",
            "12 Bath Road, Slough",
            51.5107,
            -0.5950));
        Assert.True(added.IsSuccess);

        var fetched = await slice.Send(new GetCustomerQuery(created.Value));

        Assert.True(fetched.IsSuccess);
        Assert.Equal(created.Value, fetched.Value.Id);
        Assert.Equal("Vance Refrigeration", fetched.Value.Name);
        Assert.Equal("hello@vance.example", fetched.Value.Email);
        Assert.Equal("+44 20 7946 0000", fetched.Value.Phone);

        var location = Assert.Single(fetched.Value.Locations);
        Assert.Equal(added.Value, location.Id);
        Assert.Equal("Head office", location.Label);
        Assert.Equal("12 Bath Road, Slough", location.Address);
        Assert.Equal(51.5107, location.Latitude);
        Assert.Equal(-0.5950, location.Longitude);
    }

    [Fact]
    public async Task ListsEveryCustomerOnTheBooks()
    {
        await using var slice = SliceHost.Customers();

        await slice.Send(new CreateCustomerCommand("Ivy Fabrication", null, null));
        await slice.Send(new CreateCustomerCommand("Vance Refrigeration", "hello@vance.example", null));

        var listed = await slice.Send(new ListCustomersQuery());

        Assert.True(listed.IsSuccess);
        Assert.Equal(
            ["Ivy Fabrication", "Vance Refrigeration"],
            listed.Value.Items.Select(customer => customer.Name));

        // The page says how many there are altogether, which is what a caller needs to know
        // whether to ask for another one.
        Assert.Equal(2, listed.Value.Total);

        // A customer nobody left contact details for is still a customer, and the summary says so
        // rather than inventing an empty string for the form field that was never filled in.
        Assert.Null(listed.Value.Items[0].Email);
        Assert.Null(listed.Value.Items[0].Phone);
    }

    /// <summary>
    /// A page is a window on the list, not the list: the second page carries on where the first
    /// stopped, and both report the same total.
    /// </summary>
    /// <remarks>
    /// The arithmetic is the fake repository's here and the database's in production, and the two
    /// are written to agree — skip, take, and a count over the whole tenant. What this pins is the
    /// slice's half: that the page a caller asked for is the page it is told it got, which is what
    /// a "page 3 of 7" control is built from.
    /// </remarks>
    [Fact]
    public async Task ServesOneWindowOfTheListAtATime()
    {
        await using var slice = SliceHost.Customers();

        foreach (var name in new[] { "Ada Plumbing", "Ivy Fabrication", "Vance Refrigeration" })
        {
            await slice.Send(new CreateCustomerCommand(name, null, null));
        }

        var first = await slice.Send(new ListCustomersQuery(Page: 1, PageSize: 2));
        var second = await slice.Send(new ListCustomersQuery(Page: 2, PageSize: 2));

        Assert.Equal(["Ada Plumbing", "Ivy Fabrication"], first.Value.Items.Select(customer => customer.Name));
        Assert.Equal(["Vance Refrigeration"], second.Value.Items.Select(customer => customer.Name));

        // Both pages say how many there are altogether, and which page they are.
        Assert.Equal(3, first.Value.Total);
        Assert.Equal(3, second.Value.Total);
        Assert.Equal(2, second.Value.Page);
        Assert.Equal(2, second.Value.PageSize);

        // Past the end is empty rather than an error: a client that keeps going finds the end.
        var past = await slice.Send(new ListCustomersQuery(Page: 9, PageSize: 2));
        Assert.Empty(past.Value.Items);
        Assert.Equal(3, past.Value.Total);
    }

    /// <summary>
    /// The cap is what stops the paged endpoint being the unpaged one under a new name.
    /// </summary>
    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, ListCustomersQuery.MaxPageSize + 1)]
    public async Task RefusesAPageNobodyShouldBeAskingFor(int page, int size)
    {
        await using var slice = SliceHost.Customers();

        var listed = await slice.Send(new ListCustomersQuery(page, size));

        Assert.IsType<ValidationError>(listed.Error);
    }

    /// <summary>
    /// The rule this slice establishes for every slice after it: the organization comes from the
    /// ambient tenant, and there is no way for a caller to say otherwise.
    /// </summary>
    /// <remarks>
    /// The query filters cannot enforce this — they constrain reads, not writes — so a customer
    /// filed under the wrong organization would be invisible to the tenant that created it and
    /// visible to one that did not. Reading it back through a tenant-scoped repository is what
    /// proves it landed on the right books.
    /// </remarks>
    [Fact]
    public async Task FilesTheCustomerUnderTheTenantThatAskedForThem()
    {
        await using var slice = SliceHost.Customers();

        var created = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, null));

        var stored = Assert.Single(slice.Store<Customer>().Saved);
        Assert.Equal(slice.Tenant, stored.OrgId);
        Assert.Equal(created.Value, stored.Id);
    }

    [Fact]
    public async Task AddingALocationToACustomerThisTenantDoesNotHaveIsAMiss()
    {
        await using var slice = SliceHost.Customers();
        var stranger = CustomerId.New();

        var added = await slice.Send(
            new AddServiceLocationCommand(stranger, "Head office", "12 Bath Road, Slough", 51.5107, -0.5950));

        Assert.True(added.IsFailure);
        Assert.Equal(CustomerErrors.NotFoundCode, added.Error!.Code);
        Assert.Equal(ErrorCategory.NotFound, added.Error.Category);
    }

    [Fact]
    public async Task FetchingACustomerThisTenantDoesNotHaveIsAMiss()
    {
        await using var slice = SliceHost.Customers();

        var fetched = await slice.Send(new GetCustomerQuery(CustomerId.New()));

        Assert.True(fetched.IsFailure);
        Assert.Equal(CustomerErrors.NotFoundCode, fetched.Error!.Code);
        Assert.Equal(ErrorCategory.NotFound, fetched.Error.Category);
    }

    [Theory]
    [InlineData("", "A customer must have a name.")]
    [InlineData("   ", "A customer must have a name.")]
    public async Task RefusesACustomerWithoutAName(string name, string expected)
    {
        await using var slice = SliceHost.Customers();

        var created = await slice.Send(new CreateCustomerCommand(name, null, null));

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Equal(expected, Assert.Single(failure.Failures[nameof(CreateCustomerCommand.Name)]));

        // Refused before the handler, so nothing was staged for a save that will never come.
        Assert.Empty(slice.Store<Customer>().Saved);
    }

    /// <summary>
    /// A mistyped email is worse than a missing one — it looks like a way to reach somebody — and
    /// the domain deliberately does not check the shape of one, so this is the only layer that can.
    /// </summary>
    [Fact]
    public async Task RefusesAContactDetailThatCannotBeReached()
    {
        await using var slice = SliceHost.Customers();

        var created = await slice.Send(
            new CreateCustomerCommand("Vance Refrigeration", "hello-at-vance", null));

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Contains(nameof(CreateCustomerCommand.Email), failure.Failures.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AcceptsACustomerWithNoContactDetailsAtAll()
    {
        await using var slice = SliceHost.Customers();

        var created = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, string.Empty));

        Assert.True(created.IsSuccess);

        var fetched = await slice.Send(new GetCustomerQuery(created.Value));

        // Blank and absent are the same thing, and there is one representation of it.
        Assert.Null(fetched.Value.Email);
        Assert.Null(fetched.Value.Phone);
    }

    [Fact]
    public async Task RefusesALocationWithNothingToDriveTo()
    {
        await using var slice = SliceHost.Customers();
        var created = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, null));

        var added = await slice.Send(
            new AddServiceLocationCommand(created.Value, string.Empty, "  ", 51.5107, -0.5950));

        var failure = Assert.IsType<ValidationError>(added.Error);
        Assert.Equal(
            [nameof(AddServiceLocationCommand.Address), nameof(AddServiceLocationCommand.Label)],
            failure.Failures.Keys);
    }

    /// <summary>
    /// The one the command's shape exists for. <c>GeoPoint</c> refuses a coordinate that is not a
    /// place by throwing, so a command that carried one would turn a mistyped longitude into an
    /// unhandled exception at the edge; carrying two doubles makes it a rejected field instead.
    /// </summary>
    [Theory]
    [InlineData(91d, 0d, nameof(AddServiceLocationCommand.Latitude))]
    [InlineData(-91d, 0d, nameof(AddServiceLocationCommand.Latitude))]
    [InlineData(0d, 181d, nameof(AddServiceLocationCommand.Longitude))]
    [InlineData(double.NaN, 0d, nameof(AddServiceLocationCommand.Latitude))]
    [InlineData(0d, double.PositiveInfinity, nameof(AddServiceLocationCommand.Longitude))]
    public async Task RefusesACoordinateThatIsNotAPlace(double latitude, double longitude, string field)
    {
        await using var slice = SliceHost.Customers();
        var created = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, null));

        var added = await slice.Send(new AddServiceLocationCommand(
            created.Value,
            "Head office",
            "12 Bath Road, Slough",
            latitude,
            longitude));

        var failure = Assert.IsType<ValidationError>(added.Error);
        Assert.Equal(field, Assert.Single(failure.Failures.Keys));

        var fetched = await slice.Send(new GetCustomerQuery(created.Value));
        Assert.Empty(fetched.Value.Locations);
    }
}

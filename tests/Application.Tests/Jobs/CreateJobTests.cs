using OpenDispatch.Application.Customers;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Jobs;

/// <summary>
/// Booking a job, sent through the real pipeline.
/// </summary>
/// <remarks>
/// Intake is the least interesting half of this slice and the resolution of the service location
/// is the other half: a job carries the coordinates of the place the work happens, and nothing in
/// the database can refuse a job pointing at a site that is not on the customer's books, so this
/// handler is the only thing that makes that reference good.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class CreateJobTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BooksAJobAsUnscheduledDemandAtTheCustomersOwnLocation()
    {
        await using var slice = SliceHost.Jobs();
        var (customer, location) = await ACustomerWithASite(slice);

        var created = await slice.Send(NewJob(customer, location));

        Assert.True(created.IsSuccess);

        var job = Assert.Single(slice.Store<Job>().Saved);
        Assert.Equal(created.Value, job.Id);
        Assert.Equal(slice.Tenant, job.OrgId);
        Assert.Equal(customer, job.CustomerId);
        Assert.Equal(location, job.LocationId);
        Assert.Equal("hvac", job.RequiredSkill);
        Assert.Equal(JobPriority.High, job.Priority);
        Assert.Equal(TimeSpan.FromHours(2), job.EstimatedDuration);

        // Demand, not a plan: who goes and when is an Assignment, and nothing here writes one.
        Assert.Equal(JobStatus.Unscheduled, job.Status);

        // Copied off the service location rather than taken from the caller, which is what lets
        // the scheduler build a distance matrix without loading a customer per stop.
        Assert.Equal(51.5107, job.Location.Lat);
        Assert.Equal(-0.5950, job.Location.Lng);
    }

    [Fact]
    public async Task RefusesAJobForACustomerThisTenantDoesNotHave()
    {
        await using var slice = SliceHost.Jobs();
        var (_, location) = await ACustomerWithASite(slice);

        var created = await slice.Send(NewJob(CustomerId.New(), location));

        Assert.Equal(CustomerErrors.NotFoundCode, created.Error!.Code);
        Assert.Equal(ErrorCategory.NotFound, created.Error.Category);
        Assert.Empty(slice.Store<Job>().Saved);
    }

    /// <summary>
    /// The reference nothing else checks. <c>ServiceLocation</c> is owned by its customer, so EF
    /// cannot express a foreign key from <c>Job.LocationId</c> to it and the database will accept
    /// a job pointing anywhere.
    /// </summary>
    [Fact]
    public async Task RefusesAJobAtASiteTheCustomerDoesNotHave()
    {
        await using var slice = SliceHost.Jobs();
        var (customer, _) = await ACustomerWithASite(slice);

        var created = await slice.Send(NewJob(customer, ServiceLocationId.New()));

        Assert.Equal(CustomerErrors.LocationNotFoundCode, created.Error!.Code);
        Assert.Equal(ErrorCategory.NotFound, created.Error.Category);
        Assert.Empty(slice.Store<Job>().Saved);
    }

    /// <summary>
    /// One customer's site is not another's, and the failure has to be the same miss as a site
    /// that does not exist — anything else would confirm the other customer has one.
    /// </summary>
    [Fact]
    public async Task RefusesAJobAtAnotherCustomersSite()
    {
        await using var slice = SliceHost.Jobs();
        var (mine, _) = await ACustomerWithASite(slice);
        var (_, theirs) = await ACustomerWithASite(slice, "Ivy Fabrication");

        var created = await slice.Send(NewJob(mine, theirs));

        Assert.Equal(CustomerErrors.LocationNotFoundCode, created.Error!.Code);
        Assert.Empty(slice.Store<Job>().Saved);
    }

    [Fact]
    public async Task RefusesAWindowThatEndsBeforeItStarts()
    {
        await using var slice = SliceHost.Jobs();
        var (customer, location) = await ACustomerWithASite(slice);

        var created = await slice.Send(NewJob(customer, location) with
        {
            WindowStart = MondayMorning,
            WindowEnd = MondayMorning.AddMinutes(-1),
        });

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Equal(
            "A job's window cannot end before it starts.",
            Assert.Single(failure.Failures[nameof(CreateJobCommand.WindowEnd)]));
        Assert.Empty(slice.Store<Job>().Saved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefusesAJobThatNamesNoSkill(string skill)
    {
        await using var slice = SliceHost.Jobs();
        var (customer, location) = await ACustomerWithASite(slice);

        var created = await slice.Send(NewJob(customer, location) with { RequiredSkill = skill });

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Equal(
            "A job must state the skill it requires.",
            Assert.Single(failure.Failures[nameof(CreateJobCommand.RequiredSkill)]));
        Assert.Empty(slice.Store<Job>().Saved);
    }

    [Fact]
    public async Task RefusesWorkThatIsExpectedToTakeNoTime()
    {
        await using var slice = SliceHost.Jobs();
        var (customer, location) = await ACustomerWithASite(slice);

        var created = await slice.Send(
            NewJob(customer, location) with { EstimatedDuration = TimeSpan.Zero });

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Contains(nameof(CreateJobCommand.EstimatedDuration), failure.Failures.Keys, StringComparer.Ordinal);
    }

    /// <summary>
    /// An integer cast to the enum is what this catches — a priority the objective function has no
    /// weight for would otherwise reach the scheduler.
    /// </summary>
    [Fact]
    public async Task RefusesAPriorityThatIsNotOne()
    {
        await using var slice = SliceHost.Jobs();
        var (customer, location) = await ACustomerWithASite(slice);

        var created = await slice.Send(
            NewJob(customer, location) with { Priority = (JobPriority)99 });

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Contains(nameof(CreateJobCommand.Priority), failure.Failures.Keys, StringComparer.Ordinal);
    }

    private static CreateJobCommand NewJob(CustomerId customer, ServiceLocationId location) =>
        new(
            customer,
            location,
            "hvac",
            JobPriority.High,
            MondayMorning,
            MondayMorning.AddHours(3),
            TimeSpan.FromHours(2));

    /// <summary>
    /// Arranged through the Customers slice rather than by reaching into the store: a job needs a
    /// real customer with a real site, and the commands that make one are already registered.
    /// </summary>
    private static async Task<(CustomerId Customer, ServiceLocationId Location)> ACustomerWithASite(
        SliceHost slice,
        string name = "Vance Refrigeration")
    {
        var customer = await slice.Send(new CreateCustomerCommand(name, null, null));
        var location = await slice.Send(new AddServiceLocationCommand(
            customer.Value,
            "Head office",
            "12 Bath Road, Slough",
            51.5107,
            -0.5950));

        return (customer.Value, location.Value);
    }
}

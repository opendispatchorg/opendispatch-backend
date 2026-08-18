using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Events;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Invoicing;
using OpenDispatch.Application.Invoicing.GenerateInvoice;
using OpenDispatch.Application.Invoicing.MarkPaid;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Invoicing;

/// <summary>
/// Call to cash, against a real database: a customer is taken on, a job is booked and dispatched,
/// a technician works it, it is billed, and the bill is settled.
/// </summary>
/// <remarks>
/// The flow <c>TESTING.md</c> asks for by name — "one test that creates a customer, schedules a
/// job, completes it and invoices it is worth more than thirty per-endpoint tests" — and the first
/// time every slice in the phase has run in one sequence. What is specific to this step is the last
/// two commands: the money arithmetic surviving a round trip through <c>Money</c>'s columns, and
/// <c>InvoicePaid</c> reaching a subscriber nobody wired up.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class InvoicingFlowTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Day = new(MondayMorning, MondayMorning.AddHours(9));

    private readonly OrgId _tenant = OrgId.New();
    private readonly DomainEventRecorder _recorder = new();
    private readonly PostgresFixture _postgres;

    public InvoicingFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task TakesAJobFromBookedToPaid()
    {
        await using var services = BuildHost();

        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", "hello@vance.example", null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Head office",
            "12 Bath Road, Slough",
            51.5107,
            -0.5950));

        var technician = await Send(services, new CreateTechnicianCommand(
            "Sam Rivera",
            ["hvac"],
            Day.Start,
            Day.End,
            51.5074,
            -0.1278));

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.High,
            Day.Start,
            Day.End,
            TimeSpan.FromHours(2)));

        await Send(services, new AssignJobCommand(job.Value, technician.Value, MondayMorning.AddHours(1)));

        var finishedAt = MondayMorning.AddHours(3);
        foreach (var status in new[] { JobStatus.Dispatched, JobStatus.EnRoute, JobStatus.InProgress })
        {
            Assert.True((await Send(services, new ChangeJobStatusCommand(job.Value, status))).IsSuccess);
        }

        Assert.True((await Send(
            services,
            new ChangeJobStatusCommand(job.Value, JobStatus.Completed, finishedAt))).IsSuccess);

        var raised = await Send(services, new GenerateInvoiceCommand(job.Value,
        [
            new InvoiceLine(LineItemKind.Labor, "Diagnosis and repair", 2.5m, 65m),
            new InvoiceLine(LineItemKind.Part, "Expansion vessel", 1m, 84.99m),
        ]));
        Assert.True(raised.IsSuccess);

        await using (var context = _postgres.NewContext(_tenant))
        {
            var invoice = await context.Invoices.SingleAsync(candidate => candidate.Id == raised.Value.Id);

            // The money came back out of the database as the same money: two lines, and a total
            // computed from them rather than stored beside them.
            Assert.Equal(InvoiceStatus.Draft, invoice.Status);
            Assert.Equal(2, invoice.Lines.Count);
            Assert.Equal(new Money(16_250L + 8_499L), invoice.Total);
            Assert.Equal(job.Value, invoice.JobId);

            // And the projection the caller was handed at the moment of creation says the same
            // thing the row now holds.
            Assert.Equal(invoice.Total, raised.Value.Total);

            Assert.Equal(JobStatus.Invoiced, await StatusAsync(context, job.Value));
        }

        Assert.True((await Send(services, new MarkPaidCommand(raised.Value.Id))).IsSuccess);

        await using (var context = _postgres.NewContext(_tenant))
        {
            var invoice = await context.Invoices.SingleAsync(candidate => candidate.Id == raised.Value.Id);

            Assert.Equal(InvoiceStatus.Paid, invoice.Status);
            Assert.Equal(JobStatus.Paid, await StatusAsync(context, job.Value));
        }

        // The extension seam, on the one event this phase raises that nothing in the application
        // subscribes to: a handler that exists only in this test assembly heard it.
        var settled = Assert.IsType<InvoicePaid>(Assert.Single(_recorder.Received.OfType<InvoicePaid>()));
        Assert.Equal(raised.Value.Id, settled.InvoiceId);
        Assert.Equal(job.Value, settled.JobId);
    }

    [Fact]
    public async Task RefusesToBillWorkThatIsNotFinishedAndLeavesNoInvoiceBehind()
    {
        await using var services = BuildHost();

        var customer = await Send(services, new CreateCustomerCommand("Ivy Fabrication", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Site",
            "3 Mill Lane",
            51.3762,
            -0.0982));

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            Day.Start,
            Day.End,
            TimeSpan.FromHours(1)));

        var raised = await Send(services, new GenerateInvoiceCommand(job.Value,
            [new InvoiceLine(LineItemKind.Labor, "Callout", 1m, 90m)]));

        Assert.Equal(InvoiceErrors.JobNotCompletedCode, raised.Error!.Code);

        await using var context = _postgres.NewContext(_tenant);
        Assert.Empty(await context.Invoices.ToListAsync());
        Assert.Equal(JobStatus.Unscheduled, await StatusAsync(context, job.Value));
    }

    /// <summary>
    /// The ordinary case, and the promise Document 1 makes: the technician's own labour and parts
    /// become the bill, with nobody in the office re-typing them.
    /// </summary>
    [Fact]
    public async Task BillsWhatTheTechnicianRecordedWhenTheRequestStatesNothing()
    {
        await using var services = BuildHost();
        var job = await CompletedJobAsync(services, "Halpert Paper");

        await RecordAsync(job, (LineItemKind.Labor, "Two hours on the roof", 2m, 85m),
                               (LineItemKind.Part, "Run capacitor", 1m, 42.50m));

        var raised = await Send(services, new GenerateInvoiceCommand(job));

        Assert.True(raised.IsSuccess);

        // 170.00 + 42.50, and the lines say what the van said rather than a paraphrase of it.
        Assert.Equal(Money.FromDollars(212.50m), raised.Value.Total);
        Assert.Collection(
            raised.Value.Lines,
            labour => Assert.Equal("Two hours on the roof", labour.Description),
            part => Assert.Equal("Run capacitor", part.Description));

        await using var context = _postgres.NewContext(_tenant);
        var invoice = await context.Invoices.SingleAsync(candidate => candidate.Id == raised.Value.Id);

        Assert.Equal(Money.FromDollars(212.50m), invoice.Total);
        Assert.Equal(JobStatus.Invoiced, await StatusAsync(context, job));

        // And the record of the visit is untouched by having been billed — two separate things,
        // as JobLine and LineItem have always claimed to be.
        var billed = await context.Jobs.SingleAsync(candidate => candidate.Id == job);
        Assert.Equal(2, billed.Lines.Count);
    }

    /// <summary>
    /// The office keeps the last word. A visit whose field record is wrong, a warranty call billed
    /// differently, a call-out fee nobody stood in a house and typed — stated lines win, and the
    /// job's own record is neither used nor altered.
    /// </summary>
    [Fact]
    public async Task StatedLinesWinOverWhatTheJobRecorded()
    {
        await using var services = BuildHost();
        var job = await CompletedJobAsync(services, "Schrute Farms");

        await RecordAsync(job, (LineItemKind.Labor, "Four hours", 4m, 85m));

        var raised = await Send(services, new GenerateInvoiceCommand(job,
            [new InvoiceLine(LineItemKind.Labor, "Goodwill: two hours", 2m, 85m)]));

        Assert.True(raised.IsSuccess);
        Assert.Equal(Money.FromDollars(170m), raised.Value.Total);
        Assert.Equal("Goodwill: two hours", Assert.Single(raised.Value.Lines).Description);

        await using var context = _postgres.NewContext(_tenant);
        var billed = await context.Jobs.SingleAsync(candidate => candidate.Id == job);

        Assert.Equal("Four hours", Assert.Single(billed.Lines).Description);
    }

    /// <summary>
    /// A visit that recorded nothing and a request that states nothing: a named refusal rather than
    /// a bill for nothing. The failure has to be an ordinary <c>Result</c> and not the aggregate's
    /// exception, because a dispatcher reads it.
    /// </summary>
    [Fact]
    public async Task RefusesToBillAJobWithNothingOnItAndLeavesTheJobAlone()
    {
        await using var services = BuildHost();
        var job = await CompletedJobAsync(services, "Lackawanna Heating");

        var raised = await Send(services, new GenerateInvoiceCommand(job));

        Assert.Equal(InvoiceErrors.NothingToBillCode, raised.Error!.Code);

        await using var context = _postgres.NewContext(_tenant);

        Assert.Empty(await context.Invoices.ToListAsync());

        // The job keeps its one Completed → Invoiced transition, which is what stops it being
        // billed twice.
        Assert.Equal(JobStatus.Completed, await StatusAsync(context, job));
    }

    /// <summary>A booked job walked all the way to <c>Completed</c>, with nothing recorded on it yet.</summary>
    private async Task<JobId> CompletedJobAsync(ServiceProvider services, string customerName)
    {
        var customer = await Send(services, new CreateCustomerCommand(customerName, null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value, "Site", "1 Mill Lane", 51.3762, -0.0982));

        var technician = await Send(services, new CreateTechnicianCommand(
            "Sam Rivera", ["hvac"], Day.Start, Day.End, 51.5074, -0.1278));

        var job = await Send(services, new CreateJobCommand(
            customer.Value, location.Value, "hvac", JobPriority.Normal,
            Day.Start, Day.End, TimeSpan.FromHours(2)));

        await Send(services, new AssignJobCommand(job.Value, technician.Value, MondayMorning.AddHours(1)));

        foreach (var status in new[] { JobStatus.Dispatched, JobStatus.EnRoute, JobStatus.InProgress, JobStatus.Completed })
        {
            Assert.True((await Send(services, new ChangeJobStatusCommand(job.Value, status))).IsSuccess);
        }

        return job.Value;
    }

    /// <summary>
    /// Writes the technician's lines directly.
    /// </summary>
    /// <remarks>
    /// The way in through the application is a sync push from the technician who owns the stop, and
    /// that whole path is exercised end to end by <c>CallToCashFlowTests</c> — which is where the
    /// claim "the field's own ops become the bill" belongs. These cases are about the invoicing
    /// rule, so they state the job's record rather than earning it.
    /// </remarks>
    private async Task RecordAsync(
        JobId job,
        params (LineItemKind Kind, string Description, decimal Quantity, decimal UnitPrice)[] lines)
    {
        await using var context = _postgres.NewContext(_tenant);

        var writing = await context.Jobs.SingleAsync(candidate => candidate.Id == job);

        foreach (var line in lines)
        {
            writing.RecordLine(
                line.Kind,
                line.Description,
                line.Quantity,
                Money.FromDollars(line.UnitPrice),
                MondayMorning.AddHours(2));
        }

        await context.SaveChangesAsync();
    }

    private static async Task<JobStatus> StatusAsync(
        Infrastructure.Persistence.AppDbContext context,
        JobId job) =>
        await context.Jobs.Where(candidate => candidate.Id == job).Select(candidate => candidate.Status).SingleAsync();

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres)
            .AddSingleton(_recorder)
            .AddMediatR(mediator => mediator.RegisterServicesFromAssemblyContaining<SampleEventRecorder>())
            .BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}

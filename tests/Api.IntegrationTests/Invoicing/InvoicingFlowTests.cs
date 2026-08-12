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

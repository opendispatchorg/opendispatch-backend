using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Customers.EraseCustomer;
using OpenDispatch.Application.Invoicing.GenerateInvoice;
using OpenDispatch.Application.Invoicing.MarkPaid;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Notifications;

/// <summary>
/// The two moments a customer is told about, and the four times they are not.
/// </summary>
/// <remarks>
/// <para>
/// Through the real domain-event dispatch over a real database, because the claim is that
/// <em>nothing that raises these events knows this subscriber exists</em>. A test that called the
/// handler directly would prove the message text and nothing about the seam.
/// </para>
/// <para>
/// The rules that earn their own cases are the ones a shop would be embarrassed by: writing to
/// somebody who asked to be forgotten, and failing a dispatcher's action because a mail server is
/// unwell.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class CustomerNotificationTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Day = new(MondayMorning, MondayMorning.AddHours(9));

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public CustomerNotificationTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task TellsACustomerWhenTheirTechnicianIsOnTheWay()
    {
        var mail = new RecordingNotificationSender();
        await using var services = BuildHost(mail);

        var job = await ABookedJobAsync(services, "Ada Whitlock", "ada@whitlock.example");

        await Send(services, new ChangeJobStatusCommand(job, JobStatus.Dispatched));

        // Nothing yet: dispatching is an office act, and a customer does not need to hear about it.
        Assert.Empty(mail.Sent);

        await Send(services, new ChangeJobStatusCommand(job, JobStatus.EnRoute));

        var message = Assert.Single(mail.Sent);

        Assert.Equal(NotificationChannel.Email, message.Channel);
        Assert.Equal("ada@whitlock.example", message.To);
        Assert.Equal("Your technician is on the way", message.Subject);
        Assert.Contains("Ada Whitlock", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TellsACustomerWhatTheyOweAndThenThatItIsSettled()
    {
        var mail = new RecordingNotificationSender();
        await using var services = BuildHost(mail);

        var job = await ACompletedJobAsync(services, "Dwight Schrute", "dwight@schrutefarms.example");

        var invoice = await Send(services, new GenerateInvoiceCommand(job,
            [new InvoiceLine(LineItemKind.Labor, "Diagnosis and repair", 2m, 85m)]));

        // Raising the bill is its own message now, and it is the one a shop actually needs to send:
        // the step between doing the work and getting paid.
        var bill = Assert.Single(mail.Sent, sent => sent.Subject == "Your invoice");

        Assert.Equal("dwight@schrutefarms.example", bill.To);
        Assert.Contains("170.00", bill.Body, StringComparison.Ordinal);

        await Send(services, new MarkPaidCommand(invoice.Value.Id));

        var receipt = Assert.Single(mail.Sent, sent => sent.Subject == "Your invoice has been settled");

        Assert.Equal("dwight@schrutefarms.example", receipt.To);
        Assert.Contains("170.00", receipt.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule that matters most here. A customer who asked to be forgotten is never written to —
    /// and the address is gone anyway, but the erasure is what is checked, because writing to
    /// whoever holds that address now about work done for somebody erased is the failure this
    /// prevents.
    /// </summary>
    [Fact]
    public async Task NeverWritesToSomebodyWhoAskedToBeForgotten()
    {
        var mail = new RecordingNotificationSender();
        await using var services = BuildHost(mail);

        var customer = await Send(services, new CreateCustomerCommand(
            "Dana Whitlock", "dana@whitlock.example", null));
        var job = await ABookedJobAsync(services, customer.Value);

        await Send(services, new ChangeJobStatusCommand(job, JobStatus.Dispatched));
        await Send(services, new EraseCustomerCommand(customer.Value));

        // A phone that was out of signal when the erasure ran, catching up.
        var moved = await Send(services, new ChangeJobStatusCommand(job, JobStatus.EnRoute));

        // The job still moves — erasure removes who they were, not that the work happened.
        Assert.True(moved.IsSuccess);
        Assert.Empty(mail.Sent);
    }

    /// <summary>
    /// Plenty of a shop's customers have a phone number and nothing else, and their jobs must run
    /// exactly as everybody else's.
    /// </summary>
    [Fact]
    public async Task SaysNothingWhenThereIsNoAddressToSayItTo()
    {
        var mail = new RecordingNotificationSender();
        await using var services = BuildHost(mail);

        var job = await ABookedJobAsync(services, "Ivy Fabrication", email: null);

        await Send(services, new ChangeJobStatusCommand(job, JobStatus.Dispatched));
        var moved = await Send(services, new ChangeJobStatusCommand(job, JobStatus.EnRoute));

        Assert.True(moved.IsSuccess);
        Assert.Empty(mail.Sent);
    }

    /// <summary>
    /// A mail server that is down must not cost a dispatcher their action.
    /// </summary>
    /// <remarks>
    /// A domain-event handler runs after the commit but inside the request, so an exception from
    /// one fails that request and stops the handlers behind it — the board's repaint among them.
    /// The status change has already happened by then, so the failure would be a 500 for work that
    /// was done. What is given up is the retry, and that is stated rather than discovered.
    /// </remarks>
    [Fact]
    public async Task AMailServerThatIsDownDoesNotFailTheDispatchersRequest()
    {
        var mail = new RecordingNotificationSender { Broken = true };
        await using var services = BuildHost(mail);

        var job = await ABookedJobAsync(services, "Michael Scott", "michael@dundermifflin.example");

        await Send(services, new ChangeJobStatusCommand(job, JobStatus.Dispatched));
        var moved = await Send(services, new ChangeJobStatusCommand(job, JobStatus.EnRoute));

        Assert.True(moved.IsSuccess);

        await using var context = _postgres.NewContext(_tenant);
        var status = await context.Jobs.Where(row => row.Id == job).Select(row => row.Status).SingleAsync();

        Assert.Equal(JobStatus.EnRoute, status);
    }

    /// <summary>
    /// A deployment that named no mail server sends nothing and fails nothing — the supported,
    /// ordinary configuration for a shop that does not want automated email.
    /// </summary>
    /// <remarks>
    /// Nothing registers <c>INotificationSender</c> in this host, exactly as the composition root
    /// registers nothing when <c>Mail:Host</c> is blank. So this is also what proves the subscriber
    /// can be constructed at all without one, which is a fact about the container rather than about
    /// the handler.
    /// </remarks>
    [Fact]
    public async Task RunsTheSameFlowsWithNoMailProviderConfigured()
    {
        await using var services = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

        var job = await ACompletedJobAsync(services, "Jan Levinson", "jan@dundermifflin.example");

        var invoice = await Send(services, new GenerateInvoiceCommand(job,
            [new InvoiceLine(LineItemKind.Labor, "Callout", 1m, 90m)]));
        var paid = await Send(services, new MarkPaidCommand(invoice.Value.Id));

        Assert.True(paid.IsSuccess);

        await using var context = _postgres.NewContext(_tenant);
        var settled = await context.Invoices.SingleAsync(row => row.Id == invoice.Value.Id);

        Assert.Equal(InvoiceStatus.Paid, settled.Status);
    }

    private async Task<JobId> ABookedJobAsync(ServiceProvider services, string customerName, string? email)
    {
        var customer = await Send(services, new CreateCustomerCommand(customerName, email, null));

        return await ABookedJobAsync(services, customer.Value);
    }

    /// <summary>A job on a technician's day, ready to be walked through a visit.</summary>
    private async Task<JobId> ABookedJobAsync(ServiceProvider services, CustomerId customer)
    {
        var location = await Send(services, new AddServiceLocationCommand(
            customer, "Site", "1 Mill Lane", 51.3762, -0.0982));

        var technician = await Send(services, new CreateTechnicianCommand(
            "Sam Rivera", ["hvac"], Day.Start, Day.End, 51.5074, -0.1278));

        var job = await Send(services, new CreateJobCommand(
            customer, location.Value, "hvac", JobPriority.Normal,
            Day.Start, Day.End, TimeSpan.FromHours(2)));

        await Send(services, new AssignJobCommand(job.Value, technician.Value, MondayMorning.AddHours(1)));

        return job.Value;
    }

    private async Task<JobId> ACompletedJobAsync(ServiceProvider services, string customerName, string email)
    {
        var job = await ABookedJobAsync(services, customerName, email);

        foreach (var status in new[]
                 {
                     JobStatus.Dispatched, JobStatus.EnRoute, JobStatus.InProgress, JobStatus.Completed,
                 })
        {
            Assert.True((await Send(services, new ChangeJobStatusCommand(job, status))).IsSuccess);
        }

        return job;
    }

    private ServiceProvider BuildHost(RecordingNotificationSender mail) =>
        TestHost.Over(_postgres)
            .AddSingleton<INotificationSender>(mail)
            .BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}

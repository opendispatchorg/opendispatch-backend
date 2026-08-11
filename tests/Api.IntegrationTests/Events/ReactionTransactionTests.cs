using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Invoicing.GenerateInvoice;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Events;

/// <summary>
/// What happens when a domain-event handler sends a command of its own.
/// </summary>
/// <remarks>
/// <para>
/// The claim under test is written on <c>IUnitOfWork</c> — "a reaction is its own unit of work" —
/// and until now it was only written there. Step 30 chose that opening a second transaction while
/// one is open should throw, and then step 31 published events <em>after</em> the commit, which
/// quietly means a command sent from a subscriber does not hit that rule at all: it opens a fresh
/// transaction and keeps its work whatever becomes of the request that triggered it. That is the
/// opposite of what a reader of the "one at a time" rule would assume, so it is worth a test rather
/// than a paragraph.
/// </para>
/// <para>
/// It matters because Document 2 §12's flagship example — invoice automatically when a job is
/// completed — is exactly this shape. Step 40 deliberately did not write that handler; this proves
/// the seam it would arrive through works, without shipping the handler.
/// </para>
/// <para>
/// The subscriber is registered by name here rather than found by an assembly scan, which is the
/// one place in the suite that happens. A handler for <c>JobCompleted</c> discovered by scanning
/// would attach itself to every other integration test that completes a job and bill it behind
/// their backs. That discovery works is <c>DomainEventDispatchTests</c>' claim, not this one's.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ReactionTransactionTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly Reactions _reactions = new();
    private readonly PostgresFixture _postgres;

    public ReactionTransactionTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task ACommandSentFromASubscriberGetsATransactionOfItsOwnAndCommits()
    {
        await using var services = BuildHost();
        var job = await AJobInProgressAsync(services);

        var completed = await Send(services, new ChangeJobStatusCommand(job, JobStatus.Completed));

        Assert.True(completed.IsSuccess);

        // The reaction ran inside the outer command's publish, after that command had committed,
        // and opened a transaction of its own rather than throwing on the one-at-a-time rule.
        var raised = Assert.Single(_reactions.Raised);

        await using var context = _postgres.NewContext(_tenant);
        var invoice = await context.Invoices.SingleAsync(candidate => candidate.Id == raised);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(job, invoice.JobId);

        Assert.Equal(JobStatus.Invoiced, await StatusAsync(job));
    }

    /// <summary>
    /// The half that makes the phrase "its own unit of work" mean something: the reaction's work
    /// survives the request that triggered it failing afterwards.
    /// </summary>
    /// <remarks>
    /// The subscriber sends its command and then throws, which fails the request — step 31's
    /// "failures surface". The invoice it raised is already committed and nothing takes it back,
    /// which is the same shape as the outer command's own work surviving a thrown subscriber.
    /// </remarks>
    [Fact]
    public async Task TheReactionsWorkSurvivesTheRequestThatTriggeredItFailing()
    {
        await using var services = BuildHost();
        var job = await AJobInProgressAsync(services);
        _reactions.ThrowAfterReacting = true;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Send(services, new ChangeJobStatusCommand(job, JobStatus.Completed)));

        Assert.Equal(Reactions.Blew, thrown.Message);

        var raised = Assert.Single(_reactions.Raised);

        await using var context = _postgres.NewContext(_tenant);
        Assert.Equal(raised, Assert.Single(await context.Invoices.ToListAsync()).Id);

        // And the job is Invoiced, not merely Completed: the reaction's own transaction committed
        // both of its aggregates before the subscriber blew up.
        Assert.Equal(JobStatus.Invoiced, await StatusAsync(job));
    }

    private async Task<JobStatus> StatusAsync(JobId job)
    {
        await using var context = _postgres.NewContext(_tenant);

        return await context.Jobs
            .Where(candidate => candidate.Id == job)
            .Select(candidate => candidate.Status)
            .SingleAsync();
    }

    private async Task<JobId> AJobInProgressAsync(ServiceProvider services)
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Site",
            "12 Bath Road",
            51.5107,
            -0.5950));

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(3),
            TimeSpan.FromHours(1)));

        foreach (var status in new[]
        {
            JobStatus.Scheduled,
            JobStatus.Dispatched,
            JobStatus.EnRoute,
            JobStatus.InProgress,
        })
        {
            await Send(services, new ChangeJobStatusCommand(job.Value, status));
        }

        return job.Value;
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres)
            .AddSingleton(_reactions)
            .AddTransient<INotificationHandler<DomainEventNotification<JobCompleted>>, InvoiceOnCompletion>()
            .BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}

/// <summary>What the subscriber did, and whether it should fall over afterwards.</summary>
internal sealed class Reactions
{
    /// <summary>What the subscriber throws when it is told to.</summary>
    public const string Blew = "the subscriber blew up after reacting";

    /// <summary>The invoices the reaction raised, in order.</summary>
    public List<InvoiceId> Raised { get; } = [];

    /// <summary>Whether the subscriber should throw once it has done its work.</summary>
    public bool ThrowAfterReacting { get; set; }
}

/// <summary>
/// Document 2 §12's flagship example, written as a test fixture rather than as a feature: when a
/// job is completed, bill it.
/// </summary>
/// <remarks>
/// <para>
/// It sends a real command through the real pipeline, which is the whole point — a subscriber that
/// wrote through a repository directly would prove nothing about transactions.
/// </para>
/// <para>
/// <strong>It has to be inert unless a test asks for it,</strong> and that is why the
/// <see cref="Reactions"/> is resolved rather than injected. MediatR's scan takes a whole assembly,
/// so every integration test that registers the sample subscriber finds this one too — and a
/// <c>JobCompleted</c> handler that always ran would bill jobs behind the back of every other flow
/// test in the project. Asked for and absent means "no test wanted a reaction"; injecting it would
/// mean "this host is broken", which is what it said before this comment existed.
/// </para>
/// </remarks>
internal sealed class InvoiceOnCompletion(ISender sender, IServiceProvider services)
    : IDomainEventHandler<JobCompleted>
{
    public async Task Handle(JobCompleted domainEvent, CancellationToken cancellationToken)
    {
        if (services.GetService<Reactions>() is not { } reactions)
        {
            return;
        }

        var raised = await sender.Send(
            new GenerateInvoiceCommand(
                domainEvent.JobId,
                [new InvoiceLine(LineItemKind.Labor, "Work done", 1m, 90m)]),
            cancellationToken);

        // Deliberately unguarded: a failure here should surface as the value being unreadable
        // rather than as a quiet empty list, because "the reaction did not happen" is the thing
        // this test exists to notice.
        reactions.Raised.Add(raised.Value);

        if (reactions.ThrowAfterReacting)
        {
            throw new InvalidOperationException(Reactions.Blew);
        }
    }
}

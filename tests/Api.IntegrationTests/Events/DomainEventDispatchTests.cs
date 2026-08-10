using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Events;

/// <summary>
/// The extension seam, end to end: a command drives a job through the domain, and what the
/// domain said happened reaches a subscriber that the command has never heard of.
/// </summary>
/// <remarks>
/// <para>
/// Every test here goes through the real composition and a real database, because every claim
/// worth making is about the seam between them — that events are taken off aggregates by the
/// save, held until the transaction commits, and then delivered once each. None of that is
/// observable with a fake context.
/// </para>
/// <para>
/// The subscriber is found by assembly scan rather than registered by hand. "Adding a handler"
/// is the whole cost the architecture promises for a new reaction, and a test that wires the
/// handler up itself would be proving something easier than the promise.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class DomainEventDispatchTests
{
    private static readonly DateTimeOffset FinishedAt = new(2027, 3, 1, 12, 30, 0, TimeSpan.Zero);

    // Its own organization per test, so the tenant filters keep one test's rows out of another's.
    private readonly OrgId _tenant = OrgId.New();
    private readonly DomainEventRecorder _recorder = new();
    private readonly PostgresFixture _postgres;

    public DomainEventDispatchTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// The step's own test, plus the two things that make it worth having: the events arrive in
    /// the order the job raised them, and <c>JobCompleted</c> arrives once even though the
    /// aggregate was saved twice inside the transaction.
    /// </summary>
    [Fact]
    public async Task DeliversEveryEventOnceAndInTheOrderTheDomainRaisedIt()
    {
        var job = await SeedDispatchedJobAsync();

        var result = await SendAsync(new CompleteJobCommand(job, FinishedAt, ThenRefuse: false));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [typeof(JobEnRoute), typeof(JobInProgress), typeof(JobCompleted)],
            _recorder.Received.Select(domainEvent => domainEvent.GetType()));

        var completed = Assert.IsType<JobCompleted>(_recorder.Received[^1]);
        Assert.Equal(job, completed.JobId);

        // The domain was told when the work finished rather than reading a clock, and that is
        // what travelled — the fact, not the moment the server heard it.
        Assert.Equal(FinishedAt, completed.OccurredAt);
    }

    /// <summary>
    /// The decision this step turns on. A subscriber sees the completion only once every other
    /// connection can see it too — so a handler that emails a customer or pushes to the board
    /// cannot fire for work that is still one rollback away from never having happened.
    /// </summary>
    [Fact]
    public async Task PublishesOnlyOnceTheWorkIsVisibleToEverybodyElse()
    {
        var job = await SeedDispatchedJobAsync();
        JobStatus? seenFromOutside = null;

        _recorder.OnReceived = async (domainEvent, cancellationToken) =>
        {
            if (domainEvent is not JobCompleted)
            {
                return;
            }

            // A separate context, and therefore a separate connection: it can only see what has
            // been committed. Published on the save instead, this would still read Dispatched.
            await using var outside = _postgres.NewContext(_tenant);
            seenFromOutside = await outside.Jobs
                .Where(candidate => candidate.Id == job)
                .Select(candidate => candidate.Status)
                .SingleAsync(cancellationToken);
        };

        await SendAsync(new CompleteJobCommand(job, FinishedAt, ThenRefuse: false));

        Assert.Equal(JobStatus.Completed, seenFromOutside);
    }

    /// <summary>
    /// The other half of the same decision: a fact that was taken back is never announced.
    /// </summary>
    [Fact]
    public async Task SaysNothingAboutWorkThatWasRolledBack()
    {
        var job = await SeedDispatchedJobAsync();

        var result = await SendAsync(new CompleteJobCommand(job, FinishedAt, ThenRefuse: true));

        Assert.Equal(CompleteJobHandler.Refused, result.Error);
        Assert.Empty(_recorder.Received);

        await using var context = _postgres.NewContext(_tenant);
        var reloaded = await context.Jobs.SingleAsync(candidate => candidate.Id == job);
        Assert.Equal(JobStatus.Dispatched, reloaded.Status);
    }

    /// <summary>
    /// A rolled-back command leaves nothing behind for the next one to announce. Two commands in
    /// one scope is an ordinary arrangement — an endpoint that sends two, the end-to-end flow in
    /// step 55 — and queued events outliving the transaction that raised them would surface as a
    /// customer notified about a job that was never touched.
    /// </summary>
    [Fact]
    public async Task DoesNotCarryARefusedCommandsEventsIntoTheNextOne()
    {
        var refused = await SeedDispatchedJobAsync();
        var kept = await SeedDispatchedJobAsync();

        await using var services = BuildHost();
        using var scope = services.ActingAs(_tenant);
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await sender.Send(new CompleteJobCommand(refused, FinishedAt, ThenRefuse: true));
        await sender.Send(new CompleteJobCommand(kept, FinishedAt, ThenRefuse: false));

        Assert.Equal(3, _recorder.Received.Count);
        Assert.Equal(kept, Assert.IsType<JobCompleted>(_recorder.Received[^1]).JobId);
    }

    /// <summary>
    /// The one path that cannot dispatch is refused rather than allowed to lose events quietly.
    /// </summary>
    /// <remarks>
    /// Publishing is asynchronous, so a synchronous save has nowhere to send what it collects.
    /// Nothing in the application takes this path — every port is async — but the failure it
    /// would cause is an invoice that is never raised, with nothing anywhere pointing at why.
    /// </remarks>
    [Fact]
    public async Task RefusesASynchronousSaveThatWouldDiscardTheEvents()
    {
        var job = JobBuilder.Any().ForOrg(_tenant).Build();
        job.Schedule();

        await using var context = _postgres.NewContext(_tenant);
        context.Jobs.Add(job);

        var thrown = Assert.Throws<InvalidOperationException>(() => _ = context.SaveChanges());
        Assert.Contains("SaveChangesAsync", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Failures surface — and it is worth being precise about what that does and does not mean.
    /// The request fails and publication stops, but the work is committed and stays committed:
    /// the reaction failed, not the thing it was reacting to.
    /// </summary>
    [Fact]
    public async Task ASubscriberThatThrowsFailsTheRequestAndKeepsTheCommittedWork()
    {
        var job = await SeedDispatchedJobAsync();
        _recorder.OnReceived = (_, _) => throw new InvalidOperationException("the subscriber blew up");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(new CompleteJobCommand(job, FinishedAt, ThenRefuse: false)));

        Assert.Equal("the subscriber blew up", thrown.Message);

        // The first event stopped the rest, rather than the remainder going out around it.
        Assert.Single(_recorder.Received);

        await using var context = _postgres.NewContext(_tenant);
        var reloaded = await context.Jobs.SingleAsync(candidate => candidate.Id == job);
        Assert.Equal(JobStatus.Completed, reloaded.Status);
    }

    private async Task<Result> SendAsync(CompleteJobCommand command)
    {
        await using var services = BuildHost();
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres)
            .AddSingleton(_recorder)
            // The sample command's handler and the subscriber both come from this scan. Nothing
            // in this test assembly is named in a registration, which is the point.
            .AddMediatR(mediator => mediator.RegisterServicesFromAssemblyContaining<SampleEventRecorder>())
            .BuildServiceProvider(validateScopes: true);

    /// <summary>
    /// A job on a technician's phone, ready to be worked.
    /// </summary>
    /// <remarks>
    /// <see cref="JobBuilder"/> clears the events raised on the way to a status, so seeding
    /// announces nothing — which is what lets <c>Assert.Empty</c> mean "the command said
    /// nothing" rather than "the setup happened to be quiet".
    /// </remarks>
    private async Task<JobId> SeedDispatchedJobAsync()
    {
        var job = JobBuilder.Any().ForOrg(_tenant).InStatus(JobStatus.Dispatched).Build();

        await using var context = _postgres.NewContext(_tenant);
        context.Jobs.Add(job);
        await context.SaveChangesAsync();

        return job.Id;
    }
}

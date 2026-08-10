using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Events;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Jobs;

/// <summary>
/// A job driven through its whole life against a real database, with a subscriber watching.
/// </summary>
/// <remarks>
/// The unit tests settle what the handler does; this settles that the two halves the step names
/// actually meet — a legal transition is committed <em>and</em> reaches a handler nobody wired up,
/// through step 31's interceptor, while an illegal one leaves both the row and the subscribers
/// untouched.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ChangeJobStatusFlowTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly DomainEventRecorder _recorder = new();
    private readonly PostgresFixture _postgres;

    public ChangeJobStatusFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task DrivesAJobToCompletionAndTellsASubscriberNobodyWiredUp()
    {
        await using var services = BuildHost();
        var job = await ABookedJobAsync(services);
        var finishedAt = MondayMorning.AddHours(2);

        foreach (var status in new[]
        {
            JobStatus.Scheduled,
            JobStatus.Dispatched,
            JobStatus.EnRoute,
            JobStatus.InProgress,
        })
        {
            Assert.True((await Send(services, new ChangeJobStatusCommand(job, status))).IsSuccess);
        }

        Assert.True((await Send(
            services,
            new ChangeJobStatusCommand(job, JobStatus.Completed, finishedAt))).IsSuccess);

        await using var context = _postgres.NewContext(_tenant);
        var reloaded = await context.Jobs.SingleAsync(candidate => candidate.Id == job);
        Assert.Equal(JobStatus.Completed, reloaded.Status);

        // The recorder subscribes to three of the five, which is enough to show that each command's
        // events were published after its own commit rather than batched or dropped.
        Assert.Equal(
            [typeof(JobEnRoute), typeof(JobInProgress), typeof(JobCompleted)],
            _recorder.Received.Select(raised => raised.GetType()));

        var completed = Assert.IsType<JobCompleted>(_recorder.Received[^1]);
        Assert.Equal(job, completed.JobId);
        Assert.Equal(finishedAt, completed.OccurredAt);
    }

    [Fact]
    public async Task AnIllegalMoveChangesNothingAndAnnouncesNothing()
    {
        await using var services = BuildHost();
        var job = await ABookedJobAsync(services);

        var result = await Send(services, new ChangeJobStatusCommand(job, JobStatus.EnRoute));

        Assert.Equal(JobErrors.IllegalTransitionCode, result.Error!.Code);

        await using var context = _postgres.NewContext(_tenant);
        var reloaded = await context.Jobs.SingleAsync(candidate => candidate.Id == job);
        Assert.Equal(JobStatus.Unscheduled, reloaded.Status);
        Assert.Empty(_recorder.Received);
    }

    private async Task<JobId> ABookedJobAsync(ServiceProvider services)
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Head office",
            "12 Bath Road, Slough",
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

        return job.Value;
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres)
            .AddSingleton(_recorder)
            // The subscriber is found by scanning this assembly, exactly as a real one would be
            // found in Application. Nothing here names a handler.
            .AddMediatR(mediator => mediator.RegisterServicesFromAssemblyContaining<SampleEventRecorder>())
            .BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}

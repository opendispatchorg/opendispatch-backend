using System.Diagnostics.Metrics;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Observability;
using OpenDispatch.Application.Scheduling.OptimizeDay;
using OpenDispatch.Application.Sync;
using OpenDispatch.Application.Sync.PushOps;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Observability;

/// <summary>
/// The two operations step 54 says have to be visible, measured while they really run
/// (Document 3, step 54).
/// </summary>
/// <remarks>
/// <para>
/// Through the real registrations and a real database, not by calling the metric objects: what can
/// actually break here is the wiring — an instrument nobody records on, a handler resolved without
/// its metrics, a counter incremented on the path that never runs in production. Recording on a
/// <c>SyncMetrics</c> a test constructed itself would prove none of that and would still pass with
/// the emission deleted from the handler.
/// </para>
/// <para>
/// The collectors are scoped to this host's own <see cref="IMeterFactory"/>, so a measurement from
/// another test's host — the suite runs collections in parallel — cannot be counted here. That is
/// the reason the metrics take a factory rather than owning a static meter.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class MetricsFlowTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public MetricsFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task OptimisingADayRecordsHowLongItTook()
    {
        await using var services = BuildHost();
        using var latency = Collect<double>(services, SchedulingMetrics.OptimizeDurationName);

        await ATechnicianAsync(services);
        await ABookedJobAsync(services);
        await ABookedJobAsync(services);

        var optimized = await Send(services, new OptimizeDayCommand(MondayMorning, MondayMorning.AddHours(8)));

        Assert.True(optimized.IsSuccess);

        var measurement = Assert.Single(latency.GetMeasurementSnapshot());

        // A real elapsed time, not a zero the instrument would report if it were recorded before
        // the work rather than after it. Reading two jobs, solving and staging the plan against a
        // container cannot take no time at all.
        Assert.True(
            measurement.Value > 0d,
            $"Optimize latency was recorded as {measurement.Value}ms, which is not a measurement of anything.");
    }

    /// <summary>
    /// One batch carrying both outcomes: a status the state machine allows, and one it refuses.
    /// </summary>
    /// <remarks>
    /// The refusal is the half that matters. It rides inside a 200 with no log level and no status
    /// code of its own (see <c>PushOpsHandler</c>), so this counter is the only thing standing
    /// between "a fleet of phones is being refused all morning" and a quiet dashboard.
    /// </remarks>
    [Fact]
    public async Task APushCountsWhatItAppliedAndWhyItRefusedTheRest()
    {
        await using var services = BuildHost();
        using var applied = Collect<long>(services, SyncMetrics.OpsAppliedName);
        using var conflicted = Collect<long>(services, SyncMetrics.OpsConflictedName);

        var job = await ADispatchedJobAsync(services);

        var pushed = await Send(services, new PushOpsCommand(
            TechnicianId.New(),
            [
                Status(job, JobStatus.EnRoute, MondayMorning.AddMinutes(5)),

                // Legal for the field workflow in general, illegal from where this job now is:
                // EnRoute goes to InProgress or Cancelled, never straight to Completed.
                Status(job, JobStatus.Completed, MondayMorning.AddMinutes(40)),
            ]));

        Assert.True(pushed.IsSuccess);
        Assert.Single(pushed.Value.Applied);
        Assert.Single(pushed.Value.Conflicts);

        Assert.Equal(1L, Assert.Single(applied.GetMeasurementSnapshot()).Value);

        var refusal = Assert.Single(conflicted.GetMeasurementSnapshot());
        Assert.Equal(1L, refusal.Value);
        Assert.Equal(
            JobErrors.IllegalTransitionCode,
            refusal.Tags[SyncMetrics.ConflictReasonTag]);
    }

    /// <summary>Watches one instrument on this host's meter and nobody else's.</summary>
    private static MetricCollector<T> Collect<T>(ServiceProvider services, string instrument)
        where T : struct =>
        new(
            services.GetRequiredService<IMeterFactory>(),
            OpenDispatchMetrics.MeterName,
            instrument);

    private static PushedOp Status(JobId job, JobStatus status, DateTimeOffset at) =>
        new(
            SyncOpId.From(Guid.NewGuid()),
            FieldOps.JobEntity,
            job.Value,
            FieldOps.StatusChange,
            JsonSerializer.SerializeToElement(new { status = status.ToString() }),
            BaseVersion: 1,
            ClientTs: at);

    private async Task<JobId> ADispatchedJobAsync(ServiceProvider services)
    {
        var job = await ABookedJobAsync(services);

        await Send(services, new ChangeJobStatusCommand(job.Value, JobStatus.Scheduled));
        await Send(services, new ChangeJobStatusCommand(job.Value, JobStatus.Dispatched));

        return job.Value;
    }

    private async Task<Application.Results.Result<JobId>> ABookedJobAsync(ServiceProvider services)
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Head office",
            "12 Bath Road, Slough",
            51.5107d,
            -0.5950d));

        return await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(8),
            TimeSpan.FromHours(1)));
    }

    private async Task ATechnicianAsync(ServiceProvider services) =>
        await Send(services, new CreateTechnicianCommand(
            "Sam Rivera",
            ["hvac"],
            MondayMorning,
            MondayMorning.AddHours(8),
            51.5074d,
            -0.1278d));

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}

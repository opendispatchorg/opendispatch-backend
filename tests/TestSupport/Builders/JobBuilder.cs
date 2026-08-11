using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.TestSupport.Builders;

/// <summary>
/// Builds <see cref="Job"/>s for tests, so a test states only the one field it is about
/// and a new required field on <see cref="Job.Create"/> is a change here rather than a
/// sweep through every test that ever made a job.
/// </summary>
/// <example>
/// <code>
/// var job = JobBuilder.Any().WithSkill("hvac").InStatus(JobStatus.Dispatched).Build();
/// </code>
/// </example>
/// <remarks>
/// A record with <c>with</c>-copies rather than a mutating fluent builder: two tests that
/// branch off the same starting point must not be able to affect each other.
/// </remarks>
public sealed record JobBuilder
{
    /// <summary>
    /// A fixed date, so a window built here means the same thing on every run. Tests that
    /// care about the clock supply their own.
    /// </summary>
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private OrgId Org { get; init; } = OrgId.New();

    private CustomerId Customer { get; init; } = CustomerId.New();

    private ServiceLocationId Location { get; init; } = ServiceLocationId.New();

    private GeoPoint Point { get; init; } = new(51.5074d, -0.1278d);

    private string Skill { get; init; } = "hvac";

    private JobPriority Priority { get; init; } = JobPriority.Normal;

    private TimeWindow Window { get; init; } = new(MorningOf, MorningOf.AddHours(3));

    private TimeSpan EstimatedDuration { get; init; } = TimeSpan.FromHours(1);

    private JobStatus Status { get; init; } = JobStatus.Unscheduled;

    /// <summary>An ordinary job, with every field defaulted to something plausible.</summary>
    public static JobBuilder Any() => new();

    /// <summary>Puts the job in a particular tenant — for tests where two orgs must not see each other.</summary>
    public JobBuilder ForOrg(OrgId org) => this with { Org = org };

    /// <summary>Puts the job on a particular customer's books.</summary>
    public JobBuilder ForCustomer(CustomerId customer) => this with { Customer = customer };

    /// <summary>Sets the skill a technician needs to take the job.</summary>
    public JobBuilder WithSkill(string skill) => this with { Skill = skill };

    /// <summary>Sets how badly the job needs doing.</summary>
    public JobBuilder WithPriority(JobPriority priority) => this with { Priority = priority };

    /// <summary>Puts the job somewhere on the map.</summary>
    public JobBuilder At(GeoPoint point) => this with { Point = point };

    /// <summary>Sets the window promised to the customer.</summary>
    public JobBuilder InWindow(TimeWindow window) => this with { Window = window };

    /// <summary>Sets how long the work is expected to take on site.</summary>
    public JobBuilder Lasting(TimeSpan estimatedDuration) => this with { EstimatedDuration = estimatedDuration };

    /// <summary>
    /// Walks the built job up to <paramref name="status"/> through the real intent methods,
    /// so a test that needs a dispatched job does not have to spell out how one gets there.
    /// </summary>
    public JobBuilder InStatus(JobStatus status) => this with { Status = status };

    /// <summary>
    /// Creates the job and drives it to the requested status. Domain events raised on the
    /// way are cleared, so a test sees only what its own act raised.
    /// </summary>
    public Job Build()
    {
        var job = Job.Create(Org, Customer, Location, Point, Skill, Priority, Window, EstimatedDuration);

        AdvanceTo(job, Status);
        job.ClearDomainEvents();

        return job;
    }

    // Spelled out as the walk it is rather than driven off the enum's numeric order: the
    // order of JobStatus is documentation, not a rule, and this should not quietly depend
    // on it.
    private static void AdvanceTo(Job job, JobStatus status)
    {
        if (status is JobStatus.Unscheduled)
        {
            return;
        }

        if (status is JobStatus.Cancelled)
        {
            job.Cancel();
            return;
        }

        job.Schedule();
        if (status is JobStatus.Scheduled)
        {
            return;
        }

        job.Dispatch();
        if (status is JobStatus.Dispatched)
        {
            return;
        }

        job.MarkEnRoute();
        if (status is JobStatus.EnRoute)
        {
            return;
        }

        job.MarkInProgress();
        if (status is JobStatus.InProgress)
        {
            return;
        }

        job.MarkCompleted(job.Window.End);
        if (status is JobStatus.Completed)
        {
            return;
        }

        job.MarkInvoiced();
        if (status is JobStatus.Invoiced)
        {
            return;
        }

        job.MarkPaid();
    }
}

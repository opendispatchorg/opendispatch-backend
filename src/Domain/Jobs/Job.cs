using System.Collections.Frozen;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Jobs;

/// <summary>
/// A job: what the customer needs doing, where, by when, and how far through it is.
/// </summary>
/// <remarks>
/// <para>
/// The job is the <em>demand</em>. It is deliberately not the plan — which technician goes,
/// at what time, in what order is an <c>Assignment</c>, a separate aggregate. Re-optimising
/// a day rewrites assignments and does not touch a single job, which is the point.
/// </para>
/// <para>
/// Status only ever changes through the intent methods below, and each of those goes
/// through <see cref="Transition"/>. There is no setter, no <c>SetStatus</c>, and no way
/// for a handler, a controller or a sync op to put a job into a state the table forbids —
/// that check exists once, here, rather than at every call site that might forget it.
/// </para>
/// </remarks>
public sealed class Job : AggregateRoot
{
    /// <summary>
    /// What may follow what. The whole lifecycle in one place: a linear path from booked to
    /// paid, with cancellation available up to the moment the work is done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once a job is <see cref="JobStatus.Completed"/> it can no longer be cancelled — the
    /// work happened, and unhappening it is a credit note, not a status change.
    /// </para>
    /// <para>
    /// Every status has an entry, including the terminal ones, so a lookup never has to ask
    /// whether the key is there. The step from <see cref="JobStatus.Completed"/> onward has
    /// no intent method yet; it arrives with the invoicing slice.
    /// </para>
    /// </remarks>
    private static readonly FrozenDictionary<JobStatus, JobStatus[]> Allowed =
        new Dictionary<JobStatus, JobStatus[]>
        {
            [JobStatus.Unscheduled] = [JobStatus.Scheduled, JobStatus.Cancelled],
            [JobStatus.Scheduled] = [JobStatus.Dispatched, JobStatus.Cancelled],
            [JobStatus.Dispatched] = [JobStatus.EnRoute, JobStatus.Cancelled],
            [JobStatus.EnRoute] = [JobStatus.InProgress, JobStatus.Cancelled],
            [JobStatus.InProgress] = [JobStatus.Completed, JobStatus.Cancelled],
            [JobStatus.Completed] = [JobStatus.Invoiced],
            [JobStatus.Invoiced] = [JobStatus.Paid],
            [JobStatus.Paid] = [],
            [JobStatus.Cancelled] = [],
        }.ToFrozenDictionary();

    private Job(
        JobId id,
        OrgId orgId,
        CustomerId customerId,
        ServiceLocationId locationId,
        GeoPoint location,
        string requiredSkill,
        JobPriority priority,
        TimeWindow window,
        TimeSpan estimatedDuration)
    {
        Id = id;
        OrgId = orgId;
        CustomerId = customerId;
        LocationId = locationId;
        Location = location;
        RequiredSkill = requiredSkill;
        Priority = priority;
        Window = window;
        EstimatedDuration = estimatedDuration;
        Status = JobStatus.Unscheduled;
    }

    /// <summary>This job's identity.</summary>
    public JobId Id { get; private set; }

    /// <summary>The tenant this job belongs to.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>The customer the work is for.</summary>
    public CustomerId CustomerId { get; private set; }

    /// <summary>The customer's service location the work happens at.</summary>
    public ServiceLocationId LocationId { get; private set; }

    /// <summary>
    /// Where the work is, held on the job rather than looked up through the customer so the
    /// scheduler can build a distance matrix without loading another aggregate.
    /// </summary>
    public GeoPoint Location { get; private set; }

    /// <summary>The skill a technician must have to take this job. A hard scheduling constraint.</summary>
    public string RequiredSkill { get; private set; }

    /// <summary>How badly it needs doing.</summary>
    public JobPriority Priority { get; private set; }

    /// <summary>
    /// The window promised to the customer. Soft: the scheduler may run past it at a heavy
    /// penalty rather than declaring the job unschedulable.
    /// </summary>
    public TimeWindow Window { get; private set; }

    /// <summary>How long the work is expected to take once the technician is on site.</summary>
    public TimeSpan EstimatedDuration { get; private set; }

    /// <summary>How far through its life the job is.</summary>
    public JobStatus Status { get; private set; }

    /// <summary>
    /// Books a new job. It starts <see cref="JobStatus.Unscheduled"/> — creating demand and
    /// planning for it are separate acts.
    /// </summary>
    /// <exception cref="DomainException">
    /// The job would be unworkable: no skill named, a duration of nothing, or a priority
    /// outside the ones that exist.
    /// </exception>
    public static Job Create(
        OrgId orgId,
        CustomerId customerId,
        ServiceLocationId locationId,
        GeoPoint location,
        string requiredSkill,
        JobPriority priority,
        TimeWindow window,
        TimeSpan estimatedDuration)
    {
        if (string.IsNullOrWhiteSpace(requiredSkill))
        {
            throw new DomainException("A job must state the skill it requires.");
        }

        if (estimatedDuration <= TimeSpan.Zero)
        {
            throw new DomainException("A job must be expected to take some time.");
        }

        // Guards against a cast integer arriving from a DTO and quietly becoming a
        // priority weight nothing in the objective function expects.
        if (!Enum.IsDefined(priority))
        {
            throw new DomainException($"'{priority}' is not a priority a job can have.");
        }

        return new Job(
            JobId.New(),
            orgId,
            customerId,
            locationId,
            location,
            requiredSkill.Trim(),
            priority,
            window,
            estimatedDuration);
    }

    /// <summary>Plans the job into someone's day.</summary>
    public void Schedule()
    {
        Transition(JobStatus.Scheduled);
        Raise(new JobScheduled(Id));
    }

    /// <summary>Sends the job to the technician's phone.</summary>
    public void Dispatch()
    {
        Transition(JobStatus.Dispatched);
        Raise(new JobDispatched(Id));
    }

    /// <summary>The technician has set off.</summary>
    public void MarkEnRoute()
    {
        Transition(JobStatus.EnRoute);
        Raise(new JobEnRoute(Id));
    }

    /// <summary>The technician is on site and working.</summary>
    public void MarkInProgress()
    {
        Transition(JobStatus.InProgress);
        Raise(new JobInProgress(Id));
    }

    /// <summary>The work is finished.</summary>
    /// <param name="at">
    /// When it actually finished. Passed in rather than read from a clock because a job
    /// completed offline is reported later than it happened.
    /// </param>
    public void MarkCompleted(DateTimeOffset at)
    {
        Transition(JobStatus.Completed);
        Raise(new JobCompleted(Id, at));
    }

    /// <summary>Calls the job off.</summary>
    public void Cancel()
    {
        Transition(JobStatus.Cancelled);
        Raise(new JobCancelled(Id));
    }

    private void Transition(JobStatus next)
    {
        if (!Allowed[Status].Contains(next))
        {
            throw new DomainException($"Illegal transition {Status} -> {next}.");
        }

        Status = next;
    }
}

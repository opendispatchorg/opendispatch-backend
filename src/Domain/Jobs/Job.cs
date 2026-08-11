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
    /// <para>
    /// Stages cannot be skipped, and that is a decision rather than an oversight. A
    /// technician already standing on the doorstep still moves through
    /// <see cref="JobStatus.EnRoute"/>, which costs one tap in an app that presents the
    /// buttons in order anyway, and buys the offline-sync endpoint the tightest possible
    /// rule for judging what a stale phone is allowed to do to a job.
    /// </para>
    /// </remarks>
    private static readonly FrozenDictionary<JobStatus, IReadOnlySet<JobStatus>> Allowed =
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
        }.ToFrozenDictionary(row => row.Key, IReadOnlySet<JobStatus> (row) => row.Value.ToFrozenSet());

    /// <summary>
    /// What may follow what, readable but not changeable: every status, and the statuses a job
    /// in it may move to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is enforced here and consulted elsewhere. Publishing it does not weaken
    /// anything — <see cref="Transition"/> is still the only way a status changes, and nothing
    /// outside this class can reach an illegal state — but it does let the contract generator
    /// export the table so the technician app can decide offline which buttons to offer
    /// (Document 6 §5) without a second, hand-copied opinion of the lifecycle.
    /// </para>
    /// <para>
    /// It is the whole table, terminal states and the rows past <see cref="JobStatus.Completed"/>
    /// included. Those rows are legal; they are simply not client-initiated. A table that hid
    /// them would be lying about the domain in order to keep a client honest.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<JobStatus, IReadOnlySet<JobStatus>> AllowedTransitions => Allowed;

    /// <summary>
    /// The statuses a job can still be planned from — everything that is not finished,
    /// abandoned, or already under way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which work the optimiser is allowed to move is a business question, and it is asked in
    /// three places that must not answer it differently: the optimiser building a day, the
    /// emergency-insert path rebuilding one, and eventually the board deciding what a dispatcher
    /// may drag. So it is stated once, here, rather than as a status list in a query.
    /// </para>
    /// <para>
    /// <see cref="JobStatus.Dispatched"/> is in and it is the row worth arguing about: the job is
    /// on a technician's phone but nobody has set off, so re-planning it costs a push rather than
    /// a wasted drive. From <see cref="JobStatus.EnRoute"/> onward the technician has committed,
    /// and the day belongs to them.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<JobStatus> SchedulableStatuses { get; } = new[]
    {
        JobStatus.Unscheduled,
        JobStatus.Scheduled,
        JobStatus.Dispatched,
    }.ToFrozenSet();

    // Materialisation constructor. Loading is not construction: the persistence layer builds the
    // instance through this and then sets every mapped member from the row, so a constructor
    // that establishes a starting state — an Unscheduled job, a Draft invoice — never runs
    // against data that is long past it.
    //
    // Job, Technician and Customer cannot be loaded without one; the rest have it anyway, so the
    // rule holds everywhere rather than in three places a reader would have to work out.
    // The placeholder is overwritten before anything can observe it, and exists only so a
    // non-nullable member is not left null.
    private Job() => RequiredSkill = string.Empty;

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

    /// <summary>
    /// Whether the job may move to <paramref name="next"/> from where it is now.
    /// </summary>
    /// <param name="next">The status being considered.</param>
    /// <remarks>
    /// <para>
    /// The question <see cref="Transition"/> answers by throwing, asked without throwing. It
    /// exists so a caller that must report an illegal move rather than crash on one — a handler
    /// returning a <c>Result</c>, a sync push deciding a stale phone's op is a conflict — can ask
    /// the domain instead of catching an exception and hoping it was about the state machine.
    /// </para>
    /// <para>
    /// It is the same table the technician app already reads: step 22b exports
    /// <see cref="AllowedTransitions"/> as <c>canTransition</c> so a phone can decide offline
    /// which buttons to offer. This is that function, on this side of the wire, so the two answers
    /// cannot come from two opinions of the lifecycle.
    /// </para>
    /// <para>
    /// Asking does not weaken enforcement: every intent method still goes through
    /// <see cref="Transition"/>, so a caller that skips the question still cannot reach an illegal
    /// state.
    /// </para>
    /// </remarks>
    public bool CanTransition(JobStatus next) => Allowed[Status].Contains(next);

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

    /// <summary>The work has been billed.</summary>
    /// <remarks>
    /// <para>
    /// Raises nothing, and it is the only pair of transitions that does not — the invoice
    /// announces itself. A job reaching <see cref="JobStatus.Invoiced"/> is a consequence of an
    /// <c>Invoice</c> being raised against it, and <c>InvoicePaid</c> already carries the job's
    /// identity for anything that wants to react. A second event describing the same fact from the
    /// other side would be two announcements of one thing, and the step-13 catalog names neither.
    /// </para>
    /// <para>
    /// Nothing but the invoicing slice may call this, and nothing enforces that: the job cannot see
    /// its invoice, so "there is a bill for this" is a rule one aggregate up. It is why the status
    /// change and the invoice are written in one transaction.
    /// </para>
    /// </remarks>
    public void MarkInvoiced() => Transition(JobStatus.Invoiced);

    /// <summary>The bill has been settled.</summary>
    /// <remarks>Silent, for the reason given on <see cref="MarkInvoiced"/>.</remarks>
    public void MarkPaid() => Transition(JobStatus.Paid);

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

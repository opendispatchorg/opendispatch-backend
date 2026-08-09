using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Scheduling.Model;

/// <summary>
/// A unit of work as the scheduler sees it: where it is, what it takes to do, when it was
/// promised, and how long it will hold a technician up.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not <c>Job</c>. The engine is a pure library and takes a plain
/// problem object — it has no business knowing a job's status, its customer, or how to move
/// it through a state machine. What is left is exactly the five facts that place a stop on a
/// map and in a day. Mapping a <c>Job</c> onto one of these belongs to the caller.
/// </para>
/// <para>
/// <see cref="Window"/> is the customer's promise and is <em>soft</em>: a schedule that runs
/// past it is legal and pays a penalty. <see cref="Duration"/> and <see cref="RequiredSkill"/>
/// are not — they feed hard constraints.
/// </para>
/// </remarks>
public sealed record SchedJob(
    JobId Id,
    GeoPoint Location,
    string RequiredSkill,
    JobPriority Priority,
    TimeWindow Window,
    TimeSpan Duration)
{
    /// <summary>
    /// The skill a technician must hold to take this job. Compared case-insensitively
    /// against <see cref="TechPlan.Skills"/>; stored with the caller's casing and outer
    /// whitespace trimmed.
    /// </summary>
    public string RequiredSkill { get; } = string.IsNullOrWhiteSpace(RequiredSkill)
        ? throw new ArgumentException("A job must state the skill it requires.", nameof(RequiredSkill))
        : RequiredSkill.Trim();

    /// <summary>
    /// How badly the job needs doing. Scales what the objective pays for leaving it
    /// unassigned, so an undefined value cast in from outside would silently distort the
    /// whole search.
    /// </summary>
    public JobPriority Priority { get; } = Enum.IsDefined(Priority)
        ? Priority
        : throw new ArgumentOutOfRangeException(
            nameof(Priority), Priority, "Not a priority a job can have.");

    /// <summary>How long the work occupies the technician once they are on site. Always positive.</summary>
    public TimeSpan Duration { get; } = Duration > TimeSpan.Zero
        ? Duration
        : throw new ArgumentOutOfRangeException(
            nameof(Duration), Duration, "A job must be expected to take some time.");
}

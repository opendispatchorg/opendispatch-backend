namespace OpenDispatch.Domain.Jobs;

/// <summary>
/// How badly a job needs doing. Drives the penalty the scheduler pays for leaving it
/// unassigned, so the numbers are the weights, not just an ordering.
/// </summary>
/// <remarks>
/// An enum rather than a bare <c>int</c>: a priority of 9,000 is not a thing a dispatcher
/// can mean, and the clients mirror this enum (step 21) rather than agreeing on a range by
/// convention.
/// </remarks>
public enum JobPriority
{
    /// <summary>Can wait. Slipping it to another day costs little.</summary>
    Low = 1,

    /// <summary>Ordinary booked work.</summary>
    Normal = 2,

    /// <summary>Wants doing today.</summary>
    High = 3,

    /// <summary>No heat, no water, no power. Bump whatever it takes.</summary>
    Emergency = 4,
}

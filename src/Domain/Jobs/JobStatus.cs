namespace OpenDispatch.Domain.Jobs;

/// <summary>
/// Where a job has got to in its life, from booked to paid.
/// </summary>
/// <remarks>
/// The order of the values follows the happy path, but nothing infers legality from that
/// ordering — what may follow what is stated explicitly by the transition table inside
/// <see cref="Job"/>. Values are numbered explicitly because this enum is mirrored to the
/// clients (step 21) and a reordering must not silently change the wire meaning.
/// </remarks>
public enum JobStatus
{
    /// <summary>Booked, but nobody is going to it yet.</summary>
    Unscheduled = 0,

    /// <summary>A technician and a time have been planned for it.</summary>
    Scheduled = 1,

    /// <summary>Sent to the technician's phone; it is on their day.</summary>
    Dispatched = 2,

    /// <summary>The technician is travelling to it.</summary>
    EnRoute = 3,

    /// <summary>The technician is on site doing the work.</summary>
    InProgress = 4,

    /// <summary>The work is done. Nothing further happens to the job in the field.</summary>
    Completed = 5,

    /// <summary>The completed work has been turned into an invoice.</summary>
    Invoiced = 6,

    /// <summary>The invoice has been settled. Terminal.</summary>
    Paid = 7,

    /// <summary>Called off before the work was finished. Terminal.</summary>
    Cancelled = 8,
}

namespace OpenDispatch.Application.Scheduling;

/// <summary>
/// What every planning slice runs with unless it is told otherwise.
/// </summary>
internal static class SchedulingDefaults
{
    /// <summary>
    /// The seed every plan is built under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An arbitrary constant, and it has to be a constant: the reproducibility Document 2 §4 asks
    /// for is between <em>runs</em>, not within one. A demo that produces a different board each
    /// time it is run is not a demo, and a plan a dispatcher cannot reproduce is one they cannot
    /// argue with.
    /// </para>
    /// <para>
    /// Shared by both planning slices so an optimise and an insert cannot disagree about which
    /// answer is the reproducible one — though only the optimiser's search is random at all; a
    /// single insertion tries every position and has one right answer.
    /// </para>
    /// <para>
    /// If a caller ever needs to explore alternative plans for the same day, that is a seed on the
    /// command and a deliberate decision, not a clock reading slipped in here.
    /// </para>
    /// </remarks>
    internal const int Seed = 20260810;
}

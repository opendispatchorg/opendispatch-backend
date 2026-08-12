namespace OpenDispatch.Domain.ValueObjects;

/// <summary>
/// A half-open interval in time, <c>[Start, End)</c>. Used for promised job windows and for
/// technician shifts.
/// </summary>
/// <remarks>
/// <para>
/// An inverted window is rejected at construction, so nothing downstream has to defend
/// against a negative <see cref="Duration"/>. A zero-length window is allowed — only
/// <c>End &lt; Start</c> is nonsense.
/// </para>
/// <para>
/// The interval is half-open, so two windows that merely touch (one ends exactly as the
/// next begins) do not overlap. That is the semantic the scheduler needs: back-to-back
/// stops on one technician are a valid route, not a conflict.
/// </para>
/// <para>
/// <see cref="DateTimeOffset"/> comparisons are by absolute instant, so windows expressed
/// in different UTC offsets compare correctly without any normalising by the caller.
/// </para>
/// </remarks>
public readonly record struct TimeWindow(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>The instant the window opens, inclusive.</summary>
    public DateTimeOffset Start { get; } = Start;

    /// <summary>The instant the window closes, exclusive. Never earlier than <see cref="Start"/>.</summary>
    public DateTimeOffset End { get; } = End >= Start
        ? End
        : throw new ArgumentOutOfRangeException(
            nameof(End), End, "A time window cannot end before it starts.");

    /// <summary>How long the window is open. Never negative.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>
    /// Whether this window and <paramref name="other"/> share any instant. Touching
    /// windows do not overlap.
    /// </summary>
    public bool Overlaps(TimeWindow other) => Start < other.End && other.Start < End;

    /// <summary>
    /// How late something beginning at <paramref name="start"/> is against this window. Zero when
    /// it begins before the window closes.
    /// </summary>
    /// <param name="start">When the thing actually begins.</param>
    /// <remarks>
    /// <para>
    /// <strong>Lateness is measured at the start, not the end.</strong> A technician who begins a
    /// job inside the promised window has kept the promise, however long the work then runs — the
    /// customer was told when somebody would be there, not when they would leave. The same rule
    /// prices a shift: a route is in overtime by how far past the shift's close the technician gets
    /// home.
    /// </para>
    /// <para>
    /// It lives here because two things need the same answer and must not each invent one. The
    /// engine charges for it in the objective function, and the board colours a block by it — and
    /// a dispatcher looking at a block the optimiser considered on time, marked late, would be
    /// looking at two opinions of one promise. Stating it on the window rather than on
    /// <c>Job</c> is what lets the shift use it too.
    /// </para>
    /// <para>
    /// The window is half-open, so beginning exactly as it closes is late by nothing rather than
    /// late by an instant — the same boundary <see cref="Overlaps"/> uses.
    /// </para>
    /// </remarks>
    public TimeSpan LatenessOf(DateTimeOffset start) => start > End ? start - End : TimeSpan.Zero;
}

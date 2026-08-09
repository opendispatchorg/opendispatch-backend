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
}

namespace OpenDispatch.Scheduling;

/// <summary>
/// How hard the annealer looks, and how quickly it stops being willing to accept a worse day.
/// </summary>
/// <remarks>
/// <para>
/// The defaults were picked by measurement, over a hand-written day and a generated one at
/// several sizes and seeds, and they cost about thirty milliseconds for a shop-sized problem.
/// They are exposed because the right answer depends on how big the day is and how long the
/// caller will wait, and because a test that wants the constructor's day without the search
/// should be able to ask for zero iterations rather than reach for a different scheduler.
/// </para>
/// <para>
/// Temperatures are in the same currency as the objective: travel minutes. A start temperature
/// of twenty means the search will, at the outset, often accept a rearrangement that costs
/// another ten minutes of driving — and will have stopped doing so long before it finishes,
/// since fifty thousand iterations of this cooling leave the temperature at about a
/// thousandth of a minute.
/// </para>
/// <para>
/// What the measurement did not show is any benefit from the cooling itself. Holding
/// <see cref="Iterations"/> constant, a search that never accepts a worse day scores within a
/// few percent of this one in both directions, so <see cref="StartTemperature"/> and
/// <see cref="Cooling"/> are the two knobs here least worth turning —
/// <see cref="AnnealingScheduler"/> carries the numbers and the one lead left untried.
/// <see cref="Iterations"/> is the one that does move the answer.
/// </para>
/// </remarks>
public sealed record AnnealingOptions
{
    private readonly double _startTemperature = 20d;
    private readonly double _cooling = 0.9998d;
    private readonly int _iterations = 50_000;

    /// <summary>What the search settles for when nobody has an opinion.</summary>
    public static AnnealingOptions Default { get; } = new();

    /// <summary>
    /// How willing the search is to accept a worse schedule at the start, in travel minutes.
    /// </summary>
    /// <remarks>
    /// Too low and it never leaves the constructor's day; too high and it spends the early
    /// iterations wandering at random. It wants to be somewhere near the size of the swings a
    /// single move makes.
    /// </remarks>
    public double StartTemperature
    {
        get => _startTemperature;
        init => _startTemperature = double.IsFinite(value) && value > 0d
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value), value, "A starting temperature must be finite and above zero.");
    }

    /// <summary>What the temperature is multiplied by after every iteration.</summary>
    /// <remarks>
    /// Just under one. It has to be below one or the search never settles, and above zero or
    /// it settles immediately — and how far below one it should be depends entirely on
    /// <see cref="Iterations"/>, since what matters is where the two of them leave the
    /// temperature at the end.
    /// </remarks>
    public double Cooling
    {
        get => _cooling;
        init => _cooling = double.IsFinite(value) && value is > 0d and < 1d
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value), value, "Cooling must sit between zero and one, exclusive, or the search never settles.");
    }

    /// <summary>How many rearrangements to consider.</summary>
    /// <remarks>Zero is a legitimate answer: it means take the constructor's day and stop.</remarks>
    public int Iterations
    {
        get => _iterations;
        init => _iterations = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value), value, "A search cannot run a negative number of iterations.");
    }
}

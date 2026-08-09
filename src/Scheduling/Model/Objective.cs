namespace OpenDispatch.Scheduling.Model;

/// <summary>
/// What the engine is trying to minimise, expressed as the price of each thing that can go
/// wrong. Every term is denominated in the same currency, so they can be added up and
/// compared.
/// </summary>
/// <remarks>
/// <para>
/// The unit is the <em>travel minute</em>: <see cref="Travel"/> is the price of one minute of
/// driving, and the rest say what a minute of lateness, a minute of overtime, or a dropped
/// job is worth in driving. Weights are ratios, not absolutes — doubling all four changes
/// nothing about which schedule wins.
/// </para>
/// <para>
/// A weight of zero switches a term off, which is what makes single-term tests possible.
/// Negative weights are rejected: a negative price turns a penalty into a reward, and the
/// search would happily run every job late.
/// </para>
/// <para>
/// This type holds the prices only. The arithmetic that applies them to a
/// <see cref="Solution"/> is the objective <em>evaluator</em>, and it does not exist yet.
/// </para>
/// </remarks>
/// <param name="Travel">Cost of one minute of driving. The unit the other three are quoted in.</param>
/// <param name="Lateness">
/// Cost of one minute past a job's promised window. Deliberately several times
/// <paramref name="Travel"/>: lateness is a soft constraint, so the only thing stopping the
/// engine running the whole day late is that it is expensive.
/// </param>
/// <param name="Overtime">Cost of one minute worked past the end of a technician's shift.</param>
/// <param name="Unassigned">
/// Cost of leaving a job undone, per unit of its <see cref="Domain.Jobs.JobPriority"/> — so
/// dropping an emergency costs four times dropping a low-priority job.
/// </param>
public sealed record Objective(double Travel, double Lateness, double Overtime, double Unassigned)
{
    /// <summary>
    /// Sensible starting weights: a minute late costs five minutes of driving, a minute of
    /// overtime two, and dropping even the least urgent job costs more than eight hours
    /// behind the wheel — so an unassigned job is always the last resort, but never
    /// structurally impossible the way a hard constraint would make it.
    /// </summary>
    public static Objective Default { get; } = new(Travel: 1d, Lateness: 5d, Overtime: 2d, Unassigned: 500d);

    /// <summary>Cost of one minute of driving. The unit the other three are quoted in.</summary>
    public double Travel { get; } = Weight(Travel, nameof(Travel));

    /// <summary>Cost of one minute past a job's promised window.</summary>
    public double Lateness { get; } = Weight(Lateness, nameof(Lateness));

    /// <summary>Cost of one minute worked past the end of a technician's shift.</summary>
    public double Overtime { get; } = Weight(Overtime, nameof(Overtime));

    /// <summary>Cost of leaving a job undone, per unit of its priority.</summary>
    public double Unassigned { get; } = Weight(Unassigned, nameof(Unassigned));

    // Finite as well as non-negative: an infinite weight is a hard constraint wearing a
    // disguise, and a NaN one makes every candidate schedule incomparable to every other.
    private static double Weight(double value, string name) =>
        double.IsFinite(value) && value >= 0d
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "An objective weight must be finite and non-negative.");
}

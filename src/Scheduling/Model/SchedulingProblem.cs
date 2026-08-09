using System.Collections.Immutable;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Scheduling.Model;

/// <summary>
/// Everything the engine needs to plan a stretch of time: who is available, what wants doing,
/// what a good answer is worth, and the seed that makes the answer reproducible.
/// </summary>
/// <remarks>
/// <para>
/// The whole input, in one object, with no way back out to the database. That isolation is
/// the point of the Scheduling project: a problem can be written down in a test, replayed
/// from a bug report, or benchmarked, and the engine cannot tell the difference.
/// </para>
/// <para>
/// <see cref="Seed"/> is carried in the problem rather than configured on the engine so that
/// a problem and its solution are reproducible together — the same problem run twice gives
/// the same schedule, and a demo or a failing test can be replayed exactly.
/// </para>
/// <para>
/// Shifts and windows are not required to sit inside <see cref="Horizon"/>. A promise made
/// for tomorrow is exactly the sort of thing a day's schedule has to be able to run late
/// against, and clipping it here would turn the soft constraint into a hard one by the back
/// door.
/// </para>
/// </remarks>
public sealed class SchedulingProblem
{
    /// <summary>States a scheduling problem.</summary>
    /// <param name="horizon">The stretch of time being planned.</param>
    /// <param name="technicians">Who is available. May be empty; then nothing can be assigned.</param>
    /// <param name="jobs">What wants doing. May be empty.</param>
    /// <param name="weights">What a good schedule is worth.</param>
    /// <param name="seed">Seeds the engine's randomness, so the same problem yields the same schedule.</param>
    /// <exception cref="ArgumentException">
    /// The same technician or the same job appears twice — routes are keyed by technician and
    /// a job may be placed only once, so a duplicate has no meaning the engine could honour.
    /// </exception>
    public SchedulingProblem(
        TimeWindow horizon,
        IEnumerable<TechPlan> technicians,
        IEnumerable<SchedJob> jobs,
        Objective weights,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(technicians);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(weights);

        Horizon = horizon;
        Technicians = [.. technicians];
        Jobs = [.. jobs];
        Weights = weights;
        Seed = seed;

        RejectDuplicates(Technicians.Select(t => t.Id), "technician", nameof(technicians));
        RejectDuplicates(Jobs.Select(j => j.Id), "job", nameof(jobs));
    }

    /// <summary>The stretch of time being planned.</summary>
    public TimeWindow Horizon { get; }

    /// <summary>Who is available to be given work, in a stable order.</summary>
    public ImmutableArray<TechPlan> Technicians { get; }

    /// <summary>What wants doing, in a stable order.</summary>
    public ImmutableArray<SchedJob> Jobs { get; }

    /// <summary>What a good schedule is worth.</summary>
    public Objective Weights { get; }

    /// <summary>Seeds the engine's randomness. The same problem and seed give the same schedule.</summary>
    public int Seed { get; }

    private static void RejectDuplicates<T>(IEnumerable<T> ids, string noun, string parameterName)
    {
        var seen = new HashSet<T>();

        foreach (var id in ids)
        {
            if (!seen.Add(id))
            {
                throw new ArgumentException(
                    $"The same {noun} appears twice in the problem: {id}.", parameterName);
            }
        }
    }
}

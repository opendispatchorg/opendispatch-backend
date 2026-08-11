using System.Collections.Immutable;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Application.Scheduling.InsertJob;

/// <summary>
/// The plan as it stands, expressed as something the engine can insert into.
/// </summary>
/// <remarks>
/// <para>
/// The engine's <c>Solution</c> is richer than the rows behind it. A stored stop knows when work
/// starts, how far the drive was and where it falls in the run; a <c>Stop</c> also knows when the
/// technician arrived and when they will finish. So this fills the gaps rather than inventing
/// them: the end comes from the job's own estimated duration, and the arrival is taken to be the
/// start, because a plan does not record waiting and pretending otherwise would put made-up
/// minutes into the engine's arithmetic.
/// </para>
/// <para>
/// Nothing here is priced. The arrival is not a term in the objective — travel, lateness, overtime
/// and dropped work are, and all four come out of values the rows genuinely hold — so a
/// reconstruction that loses the wait still costs the day exactly what the day costs.
/// </para>
/// </remarks>
internal static class CurrentDay
{
    /// <summary>
    /// Rebuilds each technician's run from their stored stops.
    /// </summary>
    /// <param name="stops">Every stop planned in the stretch of time being considered.</param>
    /// <param name="jobs">The work behind those stops, by id.</param>
    /// <returns>
    /// The routes, or the technician whose day overlaps itself — which is not a route anybody
    /// could drive and therefore not something a job can be slotted into.
    /// </returns>
    public static (IReadOnlyDictionary<TechnicianId, ImmutableArray<Stop>>? Routes, TechnicianId? Overlapping) Rebuild(
        IReadOnlyList<Assignment> stops,
        IReadOnlyDictionary<JobId, Job> jobs)
    {
        var routes = new Dictionary<TechnicianId, ImmutableArray<Stop>>();

        foreach (var day in stops.GroupBy(stop => stop.TechnicianId))
        {
            var run = ImmutableArray.CreateBuilder<Stop>();
            var previousEnd = DateTimeOffset.MinValue;

            // By start rather than by the stored sequence: a hand-dragged day can leave the
            // numbering behind, and the order a run is driven in is the order of the clock.
            foreach (var stop in day.OrderBy(stop => stop.ScheduledStart).ThenBy(stop => stop.Id.Value))
            {
                var job = jobs[stop.JobId];
                var start = stop.ScheduledStart;
                var end = start + job.EstimatedDuration;

                if (start < previousEnd)
                {
                    return (null, day.Key);
                }

                run.Add(new Stop(stop.JobId, Arrival: start, Start: start, End: end, stop.TravelMin));
                previousEnd = end;
            }

            routes[day.Key] = run.DrainToImmutable();
        }

        return (routes, null);
    }
}

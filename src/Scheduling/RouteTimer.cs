using System.Collections.Immutable;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Scheduling;

/// <summary>
/// Works out when a technician driving a given run of jobs is where — or refuses the run
/// outright.
/// </summary>
/// <remarks>
/// <para>
/// The single authority on what a technician is allowed to be given. Every hard constraint in
/// the architecture lives here and nowhere else: the skill they must hold, the shift the work
/// must finish inside, and the drive between consecutive stops that makes overlapping
/// impossible by arithmetic. Whatever proposes a route — the constructor placing a job, a
/// local-search move rearranging one — has to come through here, so there is exactly one
/// definition of a feasible day rather than one per caller.
/// </para>
/// <para>
/// What it does not judge is how <em>good</em> the run is. Lateness, overtime and the cost of
/// driving are the objective's business; this only answers whether the day is possible.
/// </para>
/// </remarks>
internal static class RouteTimer
{
    /// <summary>
    /// Times <paramref name="sequence"/> as <paramref name="technician"/> would drive it.
    /// </summary>
    /// <returns>
    /// The timed stops, or <see langword="null"/> if the technician cannot do this run: a job
    /// they are not qualified for, or work that would still be going after their shift ends.
    /// </returns>
    public static ImmutableArray<Stop>? Time(
        TechPlan technician,
        IReadOnlyList<SchedJob> sequence,
        TravelMatrix distances)
    {
        var stops = ImmutableArray.CreateBuilder<Stop>(sequence.Count);
        var clock = technician.Shift.Start;
        SchedJob? previous = null;

        foreach (var job in sequence)
        {
            if (!technician.HasSkill(job.RequiredSkill))
            {
                return null;
            }

            var leg = previous is null
                ? distances.FromHomeBase(technician.Id, job.Id)
                : distances.BetweenJobs(previous.Id, job.Id);

            var arrival = clock + TimeSpan.FromMinutes(leg);

            // Early is not the same as allowed. The window's opening is the one part of the
            // customer's promise the schedule treats as binding; its closing is not.
            var start = arrival > job.Window.Start ? arrival : job.Window.Start;
            var end = start + job.Duration;

            if (end > technician.Shift.End)
            {
                return null;
            }

            stops.Add(new Stop(job.Id, arrival, start, end, leg));
            clock = end;
            previous = job;
        }

        return stops.DrainToImmutable();
    }

    /// <summary>
    /// Total minutes behind the wheel for a timed run, including the drive home at the end.
    /// </summary>
    /// <remarks>
    /// The journey home is real driving and someone pays for it, so a route that finishes on
    /// the far side of the city is genuinely worse than one that finishes near the depot. It
    /// is not a <see cref="Stop"/> because nobody is being served at the end of it.
    /// </remarks>
    public static double TravelMinutes(
        TechPlan technician,
        ImmutableArray<Stop> stops,
        TravelMatrix distances)
    {
        if (stops.IsEmpty)
        {
            return 0d;
        }

        var driven = 0d;

        foreach (var stop in stops)
        {
            driven += stop.TravelMin;
        }

        return driven + distances.ToHomeBase(stops[^1].JobId, technician.Id);
    }

    /// <summary>When the technician gets back to their home base, having driven the run.</summary>
    public static DateTimeOffset HomeAgain(
        TechPlan technician,
        ImmutableArray<Stop> stops,
        TravelMatrix distances) =>
        stops.IsEmpty
            ? technician.Shift.Start
            : stops[^1].End + TimeSpan.FromMinutes(distances.ToHomeBase(stops[^1].JobId, technician.Id));
}

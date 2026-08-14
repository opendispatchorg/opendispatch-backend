using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
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
    /// <param name="technician">Whose day is being timed.</param>
    /// <param name="sequence">The jobs, in the order they would be driven.</param>
    /// <param name="distances">The drives between them.</param>
    /// <param name="notBefore">
    /// Instants a job may not be started before, over and above its window — the times stops
    /// already planned were promised for. Empty when a day is being built from nothing.
    /// </param>
    /// <returns>
    /// The timed stops, or <see langword="null"/> if the technician cannot do this run: a job
    /// they are not qualified for, or work that would still be going after their shift ends.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <paramref name="notBefore"/> is what makes <see cref="Insertion"/> keep the promise
    /// <see cref="IScheduler.Insert"/> makes: a day that is already planned is re-timed around the
    /// job being slotted in, and without it every stop is re-timed <em>from the start of the
    /// shift</em> — so an emergency at nine o'clock pulls a stop a dispatcher placed by hand at two
    /// back to the morning, and pulls a job a technician is already driving to along with it. The
    /// clock may still move a stop later, which is what displacing an afternoon means; it may not
    /// move one earlier than the customer was told.
    /// </para>
    /// <para>
    /// Building a day from nothing passes nothing, because there is no promise to keep: the
    /// constructor and the local search are choosing all of these times for the first time.
    /// </para>
    /// </remarks>
    public static ImmutableArray<Stop>? Time(
        TechPlan technician,
        IReadOnlyList<SchedJob> sequence,
        TravelMatrix distances,
        IReadOnlyDictionary<JobId, DateTimeOffset>? notBefore = null)
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
            // customer's promise the schedule treats as binding; its closing is not — and a stop
            // that has already been promised for a particular time is a second such opening.
            var earliest = job.Window.Start;

            if (notBefore is not null
                && notBefore.TryGetValue(job.Id, out var promised)
                && promised > earliest)
            {
                earliest = promised;
            }

            var start = arrival > earliest ? arrival : earliest;
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

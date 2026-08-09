using System.Collections.Frozen;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Scheduling.Travel;

/// <summary>
/// Every driving time in a problem, worked out once and then looked up by identity.
/// </summary>
/// <remarks>
/// <para>
/// A search asks for the same drives over and over — the same job pair reconsidered at every
/// position, on every technician, on every pass. Asking the provider each time is free for the
/// haversine default and ruinous for a road-network one, which is why
/// <see cref="ITravelTimeProvider.Matrix"/> exists at all. This is the thing that uses it:
/// one batched call per solve, indexed lookups thereafter.
/// </para>
/// <para>
/// Home bases come first in the point list and jobs after, so a technician's origin and a
/// job's site share one square matrix rather than needing two.
/// </para>
/// </remarks>
internal sealed class TravelMatrix
{
    private readonly double[][] _minutes;
    private readonly FrozenDictionary<TechnicianId, int> _homeBases;
    private readonly FrozenDictionary<JobId, int> _sites;

    private TravelMatrix(
        double[][] minutes,
        FrozenDictionary<TechnicianId, int> homeBases,
        FrozenDictionary<JobId, int> sites)
    {
        _minutes = minutes;
        _homeBases = homeBases;
        _sites = sites;
    }

    /// <summary>Works out every drive the problem could ask for, in one call to the provider.</summary>
    public static TravelMatrix For(SchedulingProblem problem, ITravelTimeProvider travel)
    {
        var points = new List<GeoPoint>(problem.Technicians.Length + problem.Jobs.Length);
        var homeBases = new Dictionary<TechnicianId, int>(problem.Technicians.Length);
        var sites = new Dictionary<JobId, int>(problem.Jobs.Length);

        foreach (var technician in problem.Technicians)
        {
            homeBases[technician.Id] = points.Count;
            points.Add(technician.HomeBase);
        }

        foreach (var job in problem.Jobs)
        {
            sites[job.Id] = points.Count;
            points.Add(job.Location);
        }

        return new TravelMatrix(
            travel.Matrix(points),
            homeBases.ToFrozenDictionary(),
            sites.ToFrozenDictionary());
    }

    /// <summary>Minutes from a technician's home base to a job — the first drive of their day.</summary>
    public double FromHomeBase(TechnicianId technician, JobId job) =>
        _minutes[_homeBases[technician]][_sites[job]];

    /// <summary>Minutes between two jobs.</summary>
    public double BetweenJobs(JobId leaving, JobId arrivingAt) =>
        _minutes[_sites[leaving]][_sites[arrivingAt]];
}

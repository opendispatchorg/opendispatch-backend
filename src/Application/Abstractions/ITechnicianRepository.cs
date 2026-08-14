using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Technicians;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Loads and stores technicians — the resource side of scheduling.
/// </summary>
public interface ITechnicianRepository
{
    /// <summary>Fetches one technician for changing: their skills, or their shift.</summary>
    /// <returns>The technician, or <see langword="null"/> if this tenant has no such technician.</returns>
    Task<Technician?> GetAsync(TechnicianId id, CancellationToken ct);

    /// <summary>Stages a newly taken-on technician.</summary>
    void Add(Technician technician);

    /// <summary>
    /// Fetches every technician in the tenant.
    /// </summary>
    /// <remarks>
    /// Unpaged on purpose, and the one place in this port set where that is not a smell: the
    /// scheduler has to consider the whole crew to place a job, and a shop with more
    /// technicians than fit in memory is not the shop this system is for.
    /// </remarks>
    Task<IReadOnlyList<Technician>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Every technician in the tenant, one at a time — for the export.
    /// </summary>
    /// <remarks>
    /// The crew is small enough that <see cref="ListAsync"/> would do, and this exists anyway so
    /// the export reads all five of its sources the same way: five streams drained in order, with
    /// nothing materialized. One list among four streams would be the exception a reader has to
    /// stop and explain.
    /// </remarks>
    IAsyncEnumerable<Technician> StreamAsync(CancellationToken ct);
}

using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Technicians;

namespace OpenDispatch.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ITechnicianRepository"/>
internal sealed class TechnicianRepository(AppDbContext context) : ITechnicianRepository
{
    public Task<Technician?> GetAsync(TechnicianId id, CancellationToken ct) =>
        context.Technicians.FirstOrDefaultAsync(technician => technician.Id == id, ct);

    public void Add(Technician technician) => context.Technicians.Add(technician);

    /// <remarks>
    /// Ordered by identity rather than by name: the caller is the scheduler, which has to
    /// consider the whole crew in a reproducible order, and a name is something a person edits.
    /// <para>
    /// <strong>Retired technicians are left out</strong>, which is the whole point of retiring one:
    /// all three callers — both schedulers and the office's list — are asking "who can take work",
    /// and somebody who left should not be planned a day or offered in a dropdown. Their stops and
    /// their audit entries still resolve through <see cref="GetAsync"/>, and the export still
    /// carries them, because the history is not retired with them.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<Technician>> ListAsync(CancellationToken ct) =>
        await context.Technicians
            .Where(technician => technician.RetiredAt == null)
            .OrderBy(technician => technician.Id)
            .ToListAsync(ct);

    /// <remarks>By name, because the export is read by a person or an importer rather than by the
    /// scheduler, whose ordering requirement <see cref="ListAsync"/> serves.<para>
    /// <strong>No-tracking, and that is not an optimisation.</strong> A tracked stream puts every row
    /// it hands out into the change tracker and holds it there until the request ends — so an export
    /// that streams precisely so a shop's history need not be held in memory would hold all of it
    /// anyway, one identity map at a time. Measured on a year of history: the peak came down by
    /// roughly a third. Nothing saves a projection, so there is nothing to track for.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<Technician> StreamAsync(CancellationToken ct) =>
        context.Technicians
            .AsNoTracking()
            .OrderBy(technician => technician.Name)
            .ThenBy(technician => technician.Id)
            .AsAsyncEnumerable();
}

using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IAssignmentRepository"/>
internal sealed class AssignmentRepository(AppDbContext context, IClock clock) : IAssignmentRepository
{
    public Task<Assignment?> GetAsync(AssignmentId id, CancellationToken ct) =>
        context.Assignments.FirstOrDefaultAsync(assignment => assignment.Id == id, ct);

    public void Add(Assignment assignment) => context.Assignments.Add(assignment);

    /// <remarks>
    /// <para>
    /// Removing a stop writes a note that it went, in the same save. This is the one place a stop
    /// is deleted, and after the transaction commits there is nothing left to infer from: pull
    /// answers "what changed?" by reading rows and their stamps, and a deleted row has neither. A
    /// technician whose stop was dropped by a re-optimisation would keep it on their phone and
    /// drive to it.
    /// </para>
    /// <para>
    /// It is here rather than in the handler that decided to drop the stop because there is only
    /// one of those today and there will be more; a note that has to be remembered is a note
    /// somebody eventually forgets, and the symptom is a wasted callout rather than an error.
    /// </para>
    /// </remarks>
    public void Remove(Assignment assignment)
    {
        context.Assignments.Remove(assignment);
        context.SyncRemovals.Add(SyncRemoval.OfStop(
            assignment.Id,
            assignment.OrgId,
            assignment.TechnicianId,
            clock.UtcNow));
    }

    /// <remarks>
    /// <c>SingleOrDefault</c> rather than <c>FirstOrDefault</c>: the port promises at most one
    /// stop per job and a unique index keeps that promise, so a second row is a broken database
    /// rather than a case to pick a winner from. Taking the first would hide it, and the symptom
    /// would surface later as a stop on the board that nobody can account for.
    /// </remarks>
    public Task<Assignment?> GetByJobAsync(JobId jobId, CancellationToken ct) =>
        context.Assignments.SingleOrDefaultAsync(assignment => assignment.JobId == jobId, ct);

    public async Task<IReadOnlyList<Assignment>> ListInHorizonAsync(TimeWindow horizon, CancellationToken ct) =>
        await context.Assignments
            // A stop is an instant, not a window, so it is in the horizon or it is not. Half-open
            // at the close, so two adjacent horizons neither drop a stop nor share one.
            .Where(assignment =>
                assignment.ScheduledStart >= horizon.Start && assignment.ScheduledStart < horizon.End)

            // By scheduled start, as the port specifies — it is the order a route is driven in.
            // The id breaks ties so re-optimising the same day twice reads it the same way.
            .OrderBy(assignment => assignment.ScheduledStart)
            .ThenBy(assignment => assignment.Id)
            .ToListAsync(ct);

    /// <remarks>
    /// By scheduled start, the same order the horizon-scoped read uses — streamed, because its one
    /// caller is the export and a shop's whole plan is not something to hold in memory to write it
    /// out.
    /// </remarks>
    public IAsyncEnumerable<Assignment> StreamAsync(CancellationToken ct) =>
        context.Assignments
            .OrderBy(assignment => assignment.ScheduledStart)
            .ThenBy(assignment => assignment.Id)
            .AsAsyncEnumerable();
}

using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IAssignmentRepository"/>
internal sealed class AssignmentRepository(AppDbContext context) : IAssignmentRepository
{
    public Task<Assignment?> GetAsync(AssignmentId id, CancellationToken ct) =>
        context.Assignments.FirstOrDefaultAsync(assignment => assignment.Id == id, ct);

    public void Add(Assignment assignment) => context.Assignments.Add(assignment);

    public void Remove(Assignment assignment) => context.Assignments.Remove(assignment);

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
}

using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Tests.Jobs;

/// <summary>
/// <see cref="IAssignmentRepository"/> over a <see cref="FakeStore{TAggregate}"/>, scoped to a
/// tenant like the real one.
/// </summary>
/// <remarks>
/// <see cref="GetByJobAsync"/> uses <c>Single</c> rather than <c>First</c>, as the real one does:
/// the port promises at most one stop per job and a unique index keeps that promise, so a second
/// row is a broken plan rather than a case to pick a winner from. A fake that quietly took the
/// first would let a handler that adds instead of moving pass the test written to catch it.
/// </remarks>
internal sealed class FakeAssignmentRepository(FakeStore<Assignment> store, ITenantContext tenant)
    : IAssignmentRepository
{
    public Task<Assignment?> GetAsync(AssignmentId id, CancellationToken ct) =>
        Task.FromResult(Mine().FirstOrDefault(assignment => assignment.Id == id));

    public void Add(Assignment assignment) => store.Stage(assignment);

    /// <summary>
    /// Not implemented, deliberately.
    /// </summary>
    /// <remarks>
    /// Nothing drops a stop before step 37: assigning a job moves its stop, and only re-optimising
    /// a day that can no longer fit the work deletes one. A removal modelled now would have to
    /// guess whether it stages or takes effect at once, with nothing exercising the answer.
    /// </remarks>
    public void Remove(Assignment assignment) =>
        throw new NotSupportedException(
            "Nothing removes a stop before step 37; write this fake with the slice that needs it.");

    public Task<Assignment?> GetByJobAsync(JobId jobId, CancellationToken ct) =>
        Task.FromResult(Mine().SingleOrDefault(assignment => assignment.JobId == jobId));

    public Task<IReadOnlyList<Assignment>> ListInHorizonAsync(TimeWindow horizon, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Assignment>>(
        [
            .. Mine()
                // Half-open at the close, as the real query is, so two adjacent horizons neither
                // drop a stop nor share one.
                .Where(assignment =>
                    assignment.ScheduledStart >= horizon.Start && assignment.ScheduledStart < horizon.End)
                .OrderBy(assignment => assignment.ScheduledStart)
                .ThenBy(assignment => assignment.Id.Value),
        ]);

    private IEnumerable<Assignment> Mine() => store.Owned(tenant.OrgId);
}

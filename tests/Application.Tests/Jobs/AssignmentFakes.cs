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

    /// <remarks>
    /// The only deletion in the system, and it arrived with the slice that needed it: re-optimising
    /// a day that can no longer fit a job must leave no stop behind for it.
    /// </remarks>
    public void Remove(Assignment assignment) => store.StageRemoval(assignment);

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

    public async IAsyncEnumerable<Assignment> StreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var assignment in Mine()
            .OrderBy(assignment => assignment.ScheduledStart)
            .ThenBy(assignment => assignment.Id.Value))
        {
            yield return assignment;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private IEnumerable<Assignment> Mine() => store.Owned(tenant.OrgId);
}

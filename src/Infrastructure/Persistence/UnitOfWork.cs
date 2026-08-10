using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Persistence;

/// <inheritdoc cref="IUnitOfWork"/>
/// <remarks>
/// <para>
/// One line, because EF's <c>DbContext</c> already is a unit of work: the repositories stage
/// changes on the same scoped context, and this commits them. Wrapping it in anything more would
/// be re-implementing what is already there.
/// </para>
/// <para>
/// Atomicity comes from <c>SaveChangesAsync</c> itself, which opens a transaction around the
/// whole batch unless one is already open. So a handler that writes an assignment and moves a job
/// gets both or neither without asking for a transaction, and step 30's transaction behavior can
/// widen the scope later without changing anything here.
/// </para>
/// </remarks>
internal sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}

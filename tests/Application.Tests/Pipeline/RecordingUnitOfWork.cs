using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Application.Tests.Pipeline;

/// <summary>
/// A unit of work that writes nothing and remembers everything it was asked to do.
/// </summary>
/// <remarks>
/// Enough to pin the pipeline's side of the contract — when a transaction is opened, whether a
/// save happens before the commit, and what a failure does — without a database. That a
/// rollback actually takes rows back is a claim about Postgres, and is proved against Postgres
/// in <c>Api.IntegrationTests</c>.
/// </remarks>
internal sealed class RecordingUnitOfWork(PipelineJournal journal) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        journal.Record(PipelineJournal.Saved);
        return Task.FromResult(0);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct)
    {
        journal.Record(PipelineJournal.Begun);
        return Task.FromResult<IUnitOfWorkTransaction>(new Transaction(journal));
    }

    /// <remarks>
    /// <see cref="TransientFailures"/> is how many times the database is imagined to have failed
    /// transiently before succeeding — zero for the ordinary case. The real strategy re-runs
    /// everything inside the boundary, transaction included, which is what this reproduces.
    /// </remarks>
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<IUnitOfWorkTransaction, CancellationToken, Task<TResult>> work,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await BeginTransactionAsync(ct).ConfigureAwait(false);

            if (attempt < TransientFailures)
            {
                journal.Record(PipelineJournal.RetriedAfterFailure);

                continue;
            }

            return await work(transaction, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// How many attempts fail transiently before one is allowed through — the retry the persistence
    /// layer performs, without a database to unplug.
    /// </summary>
    public int TransientFailures { get; set; }

    private sealed class Transaction(PipelineJournal journal) : IUnitOfWorkTransaction
    {
        private bool _settled;

        public Task CommitAsync(CancellationToken ct)
        {
            _settled = true;
            journal.Record(PipelineJournal.Committed);

            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct)
        {
            _settled = true;
            journal.Record(PipelineJournal.RolledBack);

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            // EF rolls back a transaction disposed without a commit, and the behavior leans on
            // exactly that for the exception path. Modelling it here is what makes the exception
            // test mean something: a behavior that leaked the work would otherwise pass it.
            if (!_settled)
            {
                journal.Record(PipelineJournal.RolledBack);
            }

            return ValueTask.CompletedTask;
        }
    }
}

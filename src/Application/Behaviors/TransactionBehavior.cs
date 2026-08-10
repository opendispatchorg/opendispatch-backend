using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Behaviors;

/// <summary>
/// Runs a command inside one transaction and keeps it only if the command succeeded.
/// </summary>
/// <typeparam name="TRequest">The command. Queries do not reach this behavior.</typeparam>
/// <typeparam name="TResponse">Its result type.</typeparam>
/// <remarks>
/// <para>
/// Commands only, and the constraint on <see cref="ICommandBase"/> is what says so: the
/// container skips an open-generic behavior whose constraints a request does not satisfy, so a
/// query is not merely allowed to skip the transaction — it cannot be given one.
/// </para>
/// <para>
/// A successful command is saved and committed here rather than in the handler, so a slice
/// states what changed and this decides whether it is kept. A handler may still save partway
/// through when it needs the write ordered; the save below then finds nothing outstanding and
/// the commit is still the moment the work becomes real.
/// </para>
/// <para>
/// <strong>A failed result rolls back, exactly like an exception.</strong> This is the decision
/// worth knowing about: a handler that writes and then reports a failure — half a batch applied
/// before the next op turned out to be illegal — leaves nothing behind, so "the command failed"
/// and "the database is untouched" are the same statement rather than two a caller has to check
/// separately. A command whose partial work is meant to survive is reporting success with
/// something in it, not failure.
/// </para>
/// </remarks>
internal sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Disposing an uncommitted transaction rolls it back, so an exception on any path below
        // — including one thrown by the save — undoes the work without a catch block.
        await using var transaction = await unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var response = await next(cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return response;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }
}

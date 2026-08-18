using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Behaviors;

/// <summary>
/// The refusals a lost race can report.
/// </summary>
/// <remarks>
/// Beside the behavior that produces it rather than in a feature slice, because it belongs to no
/// feature: any command touching any aggregate can lose this race. The code is what a client
/// branches on, exactly like <c>JobErrors.IllegalTransitionCode</c>.
/// </remarks>
public static class ConcurrencyErrors
{
    /// <summary>The code every lost write carries.</summary>
    public const string StaleVersionCode = "concurrency.staleVersion";

    /// <summary>Reports that somebody else's change landed first.</summary>
    /// <remarks>
    /// A conflict rather than a server error, and the distinction is the whole point: the request
    /// was well-formed, the caller was allowed to make it, and nothing is broken — the world simply
    /// moved while they were looking at it. Retrying the *same* write would lose the same race
    /// again, which is why the message asks for a reload rather than a retry.
    /// </remarks>
    public static Error StaleVersion() => Error.Conflict(
        StaleVersionCode,
        "Somebody else changed this first. Reload to see the current state, then try again.");

    /// <summary>The code every write a unique index refused carries.</summary>
    public const string DuplicateCode = "concurrency.duplicate";

    /// <summary>Reports that somebody else has already created what this was creating.</summary>
    /// <remarks>
    /// Its own code rather than <see cref="StaleVersionCode"/>, because the two ask a client for
    /// different things. A stale version means "your copy is old"; this means "the thing you were
    /// making is already there" — a job somebody else planned in the same second, a day two
    /// dispatchers re-planned at once — and the client's next move is to look at what exists, not
    /// to reload and repeat.
    /// </remarks>
    public static Error Duplicate() => Error.Conflict(
        DuplicateCode,
        "Somebody else has already made this change. Reload to see what is there now.");
}

/// <summary>
/// Turns a lost optimistic-concurrency race into an ordinary refusal.
/// </summary>
/// <typeparam name="TRequest">The command or query.</typeparam>
/// <typeparam name="TResponse">Its result type.</typeparam>
/// <remarks>
/// <para>
/// <strong>Without this, the one thing the <c>Version</c> token exists to catch answers 500.</strong>
/// Two dispatchers dragging the same job is the ordinary way to reach it: the second save finds a
/// version that no longer matches, the persistence layer raises
/// <see cref="ConcurrencyConflictException"/>, and — before this behavior — nothing caught it, so a
/// routine collision was logged as a bug and reported to the client as a broken server.
/// </para>
/// <para>
/// <strong>It sits between validation and the transaction</strong>, which is the only position that
/// works: the conflict is thrown by the save *inside* <c>TransactionBehavior</c>, and that
/// behavior's transaction rolls back on dispose as the exception passes through it — so by the time
/// this catches it, nothing is half-written. Catching it any lower would mean catching it before
/// the rollback; any higher and the logging behavior would have already recorded a throw that is
/// not one.
/// </para>
/// <para>
/// It catches both shapes the same race takes — a stale <c>Version</c>, and a unique index
/// refusing a row two callers created at once (<see cref="DuplicateRecordException"/>) — and
/// nothing else. Every other exception is still a bug, still logged with its stack trace, and
/// still answered with a 500.
/// </para>
/// </remarks>
internal sealed class ConcurrencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result, IResult<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException)
        {
            return TResponse.FromError(ConcurrencyErrors.StaleVersion());
        }
        catch (DuplicateRecordException)
        {
            return TResponse.FromError(ConcurrencyErrors.Duplicate());
        }
    }
}

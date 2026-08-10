using MediatR;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Messaging;

/// <summary>
/// What the two command shapes share, so a behavior can say "a command" in a constraint.
/// </summary>
/// <remarks>
/// It is empty and it is not a request. <c>ICommand</c> and <c>ICommand&lt;TValue&gt;</c> cannot
/// inherit one from the other — each declares a different <c>IRequest&lt;&gt;</c> response and
/// MediatR resolves a request by that single interface — so the thing they have in common has to
/// be stated separately. <c>TransactionBehavior</c> constrains on this, which is how "around
/// command handlers" becomes something the compiler checks rather than something a naming
/// convention hopes for.
/// </remarks>
public interface ICommandBase;

/// <summary>
/// A request that changes something and reports only whether it worked.
/// </summary>
/// <remarks>
/// <para>
/// Commands are the transactional half of the pipeline: one runs inside a transaction that
/// commits when the result is a success and rolls back when it is not, so a command that fails
/// leaves nothing behind.
/// </para>
/// <para>
/// The response type is fixed to <see cref="Result"/> rather than left to each command, which
/// makes the golden rule — handlers return a result, they do not throw for expected failures —
/// structural instead of a convention every new slice has to be told about. Document 2 §5
/// sketches commands as <c>IRequest&lt;Result&gt;</c> directly; this is that, named.
/// </para>
/// </remarks>
public interface ICommand : ICommandBase, IRequest<Result>;

/// <summary>
/// A request that changes something and hands back a value — the id of what it created, most
/// often.
/// </summary>
/// <typeparam name="TValue">What the command produces on success.</typeparam>
public interface ICommand<TValue> : ICommandBase, IRequest<Result<TValue>>;

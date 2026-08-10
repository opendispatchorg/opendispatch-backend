using MediatR;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Messaging;

/// <summary>
/// A request that reads and changes nothing.
/// </summary>
/// <typeparam name="TValue">What it returns — a projection, not an aggregate.</typeparam>
/// <remarks>
/// <para>
/// The half of the pipeline that is not a command, and therefore the half that gets no
/// transaction: a read does not need one, and opening one per dispatch-board poll is a round
/// trip bought for nothing. Validation and logging still apply — a query with a nonsensical
/// date range should be refused the same way a command is.
/// </para>
/// <para>
/// A query returns a <see cref="Result{TValue}"/> like everything else, because "no such
/// customer" is an answer a caller has to handle and not an exception.
/// </para>
/// </remarks>
public interface IQuery<TValue> : IRequest<Result<TValue>>;

namespace OpenDispatch.Application.Results;

/// <summary>
/// A <see cref="Result"/> that carries a value when it succeeds.
/// </summary>
/// <typeparam name="TValue">
/// What the request produces. Usually small — an id, a projection — because a command that
/// hands back a whole aggregate is a command doing a query's job.
/// </typeparam>
/// <remarks>
/// Reading <see cref="Value"/> on a failure throws rather than returning <see langword="null"/>
/// or a default. A caller that has not checked <see cref="Result.IsSuccess"/> has made a
/// mistake, and the alternative is a <c>default</c> travelling onwards to fail somewhere that
/// cannot explain itself — the exact substitution this type exists to prevent.
/// </remarks>
public sealed class Result<TValue> : Result, IResult<Result<TValue>>
{
    private readonly TValue _value;

    internal Result(TValue value, Error? error)
        : base(error) => _value = value;

    /// <summary>What the request produced.</summary>
    /// <exception cref="InvalidOperationException">The request failed, so there is no value.</exception>
    public TValue Value => IsSuccess
        ? _value
        : throw new InvalidOperationException(
            $"A failed result has no value. It failed with '{Error!.Code}': {Error.Message}");

    /// <inheritdoc />
    static Result<TValue> IResult<Result<TValue>>.FromError(Error error) => Failure<TValue>(error);
}

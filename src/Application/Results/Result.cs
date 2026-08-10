namespace OpenDispatch.Application.Results;

/// <summary>
/// The answer to a command or query: it worked, or it failed for a reason the caller was
/// always going to have to handle.
/// </summary>
/// <remarks>
/// <para>
/// Handlers return one of these rather than throwing, because the failures a handler produces
/// are not exceptional — a job that cannot legally move to the requested status, a customer
/// this tenant does not have. Making them values means the compiler can see them, the pipeline
/// can act on them (a failed command rolls back), and step 46 can map a category to a status
/// code in one place instead of every controller catching a family of exceptions.
/// </para>
/// <para>
/// Exceptions keep their job: a bug, a database that is not there, a malformed
/// <c>SchedulingProblem</c>. Nobody was going to handle those, and dressing them as results
/// would put them on the same footing as an inverted time window.
/// </para>
/// </remarks>
public class Result : IResult<Result>
{
    /// <summary>Creates a result. Use <see cref="Success()"/> or <see cref="Failure(Error)"/>.</summary>
    /// <param name="error">What went wrong, or <see langword="null"/> for success.</param>
    private protected Result(Error? error) => Error = error;

    /// <summary>What went wrong, or <see langword="null"/> if nothing did.</summary>
    public Error? Error { get; }

    /// <summary>Whether the request did what was asked.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>Whether the request failed for an expected reason.</summary>
    public bool IsFailure => Error is not null;

    /// <summary>A command that did what was asked and has nothing to report back.</summary>
    public static Result Success() => new(null);

    /// <summary>A request that failed for an expected reason.</summary>
    /// <param name="error">What went wrong.</param>
    public static Result Failure(Error error) => new(error);

    /// <summary>A request that produced a value.</summary>
    /// <typeparam name="TValue">What it produced.</typeparam>
    /// <param name="value">The value.</param>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, null);

    /// <summary>A request that failed and therefore has no value.</summary>
    /// <typeparam name="TValue">What it would have produced.</typeparam>
    /// <param name="error">What went wrong.</param>
    public static Result<TValue> Failure<TValue>(Error error) => new(default!, error);

    /// <inheritdoc />
    static Result IResult<Result>.FromError(Error error) => Failure(error);
}

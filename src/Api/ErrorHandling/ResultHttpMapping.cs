using OpenDispatch.Application.Results;

namespace OpenDispatch.Api.ErrorHandling;

/// <summary>
/// The one place a <see cref="Result"/> becomes an HTTP response (Document 2 §7, step 46) —
/// "used by all controllers" means every endpoint calls <see cref="ToHttpResult"/> rather than
/// inventing its own <c>if (result.IsSuccess)</c>, the way <c>AuthEndpoints.LoginAsync</c> did
/// before this step existed to replace it.
/// </summary>
public static class ResultHttpMapping
{
    /// <summary>Answers a command's result: success has nothing to say, so it is 204.</summary>
    /// <param name="result">What the handler produced.</param>
    /// <param name="onSuccess">
    /// What to answer instead of the default 204 — a 201 with a location, for instance. Not
    /// called on failure.
    /// </param>
    public static IResult ToHttpResult(this Result result, Func<IResult>? onSuccess = null) =>
        result.IsSuccess
            ? onSuccess is null ? Results.NoContent() : onSuccess()
            : result.Error!.ToProblem();

    /// <summary>Answers a query's or command's result: success carries a value, so it is 200.</summary>
    /// <param name="result">What the handler produced.</param>
    /// <param name="onSuccess">What to answer instead of the default 200. Not called on failure.</param>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result, Func<TValue, IResult>? onSuccess = null) =>
        result.IsSuccess
            ? onSuccess is null ? Results.Ok(result.Value) : onSuccess(result.Value)
            : result.Error!.ToProblem();

    /// <summary>
    /// An <see cref="Error"/> as a ProblemDetails response — <see cref="ValidationError"/>
    /// specially, since ASP.NET Core's own validation problem shape (an <c>errors</c> object
    /// keyed by field) already says what it needs to; every other category through the same
    /// <see cref="ErrorCategory"/> → status mapping, with <see cref="Error.Code"/> riding along
    /// as an extension member so a client can branch on it without parsing English.
    /// </summary>
    public static IResult ToProblem(this Error error) =>
        error is ValidationError validation
            ? Results.ValidationProblem(validation.Failures.ToDictionary(
                failure => failure.Key,
                failure => failure.Value.ToArray()))
            : Results.Problem(
                statusCode: error.Category.ToStatusCode(),
                title: error.Category.ToTitle(),
                detail: error.Message,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    /// <summary>
    /// How each <see cref="ErrorCategory"/> is answered. The one place this table exists —
    /// Document 3, step 46's "shared mapping... used by all controllers" — so a category
    /// invented later is a compile error here (see the exhaustive <see langword="throw"/> arm)
    /// rather than a status code decided differently at each call site.
    /// </summary>
    public static int ToStatusCode(this ErrorCategory category) => category switch
    {
        ErrorCategory.Validation => StatusCodes.Status400BadRequest,
        ErrorCategory.NotFound => StatusCodes.Status404NotFound,
        ErrorCategory.Conflict => StatusCodes.Status409Conflict,
        ErrorCategory.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => throw new ArgumentOutOfRangeException(
            nameof(category), category, $"{nameof(ErrorCategory)} has a member step 46 does not map."),
    };

    private static string ToTitle(this ErrorCategory category) => category switch
    {
        ErrorCategory.Validation => "The request is not valid.",
        ErrorCategory.NotFound => "Not found.",
        ErrorCategory.Conflict => "The request conflicts with the current state.",
        ErrorCategory.Unauthorized => "Not authorized.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(category), category, $"{nameof(ErrorCategory)} has a member step 46 does not map."),
    };
}

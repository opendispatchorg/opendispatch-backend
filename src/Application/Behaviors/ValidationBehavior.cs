using FluentValidation;
using FluentValidation.Results;
using MediatR;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Behaviors;

/// <summary>
/// Refuses a malformed request before any handler sees it.
/// </summary>
/// <typeparam name="TRequest">The command or query.</typeparam>
/// <typeparam name="TResponse">Its result type.</typeparam>
/// <remarks>
/// <para>
/// It runs every validator registered for the request, and if any of them objects it returns a
/// <see cref="ValidationError"/> without calling the handler. Short-circuiting is the point:
/// nothing is loaded, nothing is staged, and the transaction behavior below never opens a
/// transaction for a request that was never going to be kept.
/// </para>
/// <para>
/// All validators run and every failure is collected, rather than stopping at the first. A
/// caller fixing one field per round trip is a caller making several, and offline that is
/// several trips that may not be available.
/// </para>
/// <para>
/// This is shape validation, not business rules: required fields, ranges, a window that ends
/// after it starts. Whether a job may move to the requested status is the domain's to answer,
/// and answering it here would be the one thing the architecture forbids — a business rule
/// living outside the entity that owns it.
/// </para>
/// <para>
/// A request with no validator passes straight through. That is the ordinary case for queries
/// and for commands whose only parameter is an id.
/// </para>
/// </remarks>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result, IResult<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        List<ValidationFailure>? failures = null;

        // One at a time rather than all at once. An async rule reaches for a repository, and a
        // repository shares the request's one DbContext — which two validators running together
        // would be using concurrently. There is normally one validator anyway, so the
        // concurrency would buy nothing to pay for that.
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);

            if (!result.IsValid)
            {
                (failures ??= []).AddRange(result.Errors);
            }
        }

        return failures is null
            ? await next(cancellationToken).ConfigureAwait(false)
            : TResponse.FromError(new ValidationError(ByProperty(failures)));
    }

    /// <summary>
    /// Groups the failures by the property they concern, in a stable order so the same rejection
    /// reads and logs the same way twice.
    /// </summary>
    private static SortedDictionary<string, IReadOnlyList<string>> ByProperty(
        IEnumerable<ValidationFailure> failures) =>
        new SortedDictionary<string, IReadOnlyList<string>>(
            failures
                .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)[.. group.Select(failure => failure.ErrorMessage)],
                    StringComparer.Ordinal),
            StringComparer.Ordinal);
}

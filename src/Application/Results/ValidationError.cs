using System.Globalization;

namespace OpenDispatch.Application.Results;

/// <summary>
/// A request rejected by its validator, carrying which fields were wrong and why.
/// </summary>
/// <remarks>
/// <para>
/// The one <see cref="Error"/> that needs more than a code and a sentence. A form has several
/// fields and a caller fixing one at a time is a caller making several round trips, so every
/// failure the validator found travels together — which is also the shape ASP.NET Core's
/// validation problem details expect at the edge in step 46.
/// </para>
/// <para>
/// Produced by <c>ValidationBehavior</c> rather than by handlers. A handler that finds a
/// business rule broken is reporting a <see cref="ErrorCategory.Conflict"/>, not a validation
/// failure: the request was well-formed, the world was not in the state it assumed.
/// </para>
/// </remarks>
public sealed record ValidationError : Error
{
    /// <summary>The code every validation failure carries. Clients branch on the fields, not on this.</summary>
    public const string ValidationFailed = "request.invalid";

    /// <summary>Creates the error from the failures a validator reported.</summary>
    /// <param name="failures">
    /// Messages keyed by the property they concern, ordered so the same rejection reads the
    /// same way twice.
    /// </param>
    public ValidationError(IReadOnlyDictionary<string, IReadOnlyList<string>> failures)
        : base(ValidationFailed, Describe(failures), ErrorCategory.Validation) =>
        Failures = failures;

    /// <summary>What was wrong, keyed by the property it was wrong about.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Failures { get; }

    /// <summary>
    /// Renders the failures as one sentence, so a log line or a problem detail says what was
    /// rejected rather than only that something was.
    /// </summary>
    private static string Describe(IReadOnlyDictionary<string, IReadOnlyList<string>> failures) =>
        failures.Count == 0
            ? "The request is not valid."
            : string.Join(
                "; ",
                failures.Select(failure => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1}",
                    failure.Key,
                    string.Join(", ", failure.Value))));
}

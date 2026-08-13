using System.Diagnostics.CodeAnalysis;

namespace OpenDispatch.Application.Results;

/// <summary>
/// An expected failure: what went wrong, in terms the caller can act on.
/// </summary>
/// <param name="Code">
/// A stable, dotted identifier for this particular failure — <c>job.illegalTransition</c>,
/// <c>customer.notFound</c>. Machine-readable and never localised: it is what a client branches
/// on, so it belongs to the contract in the way a message does not.
/// </param>
/// <param name="Message">
/// One sentence for a human, in English. Safe to show: it describes the request, never the
/// internals of the system that refused it.
/// </param>
/// <param name="Category">How the edge should answer. See <see cref="ErrorCategory"/>.</param>
/// <remarks>
/// <para>
/// Two fields where one might do, because they answer different questions. A code alone leaves
/// every client writing its own English; a message alone cannot be branched on without matching
/// strings, which is the thing that breaks the day somebody improves the wording.
/// </para>
/// <para>
/// Errors are values, not exceptions. A job that cannot legally move from <c>Completed</c> to
/// <c>EnRoute</c> is an ordinary answer to an ordinary question — the domain throws, the handler
/// turns that into one of these, and nothing unwinds a stack for it.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "'Error' is the word Document 2 section 5 uses for what a Result carries, and the word every reader of a Result already has. The rule guards against VB consumers, which a C#-only backend with TypeScript clients does not have.")]
public record Error(string Code, string Message, ErrorCategory Category)
{
    /// <summary>Names something this tenant does not have.</summary>
    public static Error NotFound(string code, string message) =>
        new(code, message, ErrorCategory.NotFound);

    /// <summary>The thing exists; its state forbids what was asked.</summary>
    public static Error Conflict(string code, string message) =>
        new(code, message, ErrorCategory.Conflict);

    /// <summary>The caller's credentials do not check out.</summary>
    public static Error Unauthorized(string code, string message) =>
        new(code, message, ErrorCategory.Unauthorized);

    // There is deliberately no Validation factory here. A validation failure is produced in one
    // place — ValidationBehavior — and always as a ValidationError, so an error of that category
    // always says which fields were wrong. A second way to make one would be a way to make one
    // that does not.
}

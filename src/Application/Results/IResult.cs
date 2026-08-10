using System.Diagnostics.CodeAnalysis;

namespace OpenDispatch.Application.Results;

/// <summary>
/// A result type that can name a failure of itself.
/// </summary>
/// <typeparam name="TSelf">The implementing type — <see cref="Result"/> or <see cref="Result{TValue}"/>.</typeparam>
/// <remarks>
/// <para>
/// This exists for one caller. A pipeline behavior is generic over the response type, so a
/// behavior that short-circuits — validation refusing a request before the handler sees it —
/// has to produce a failure of a type it only knows as <c>TResponse</c>. Reflection can do
/// that at run time; a static abstract member does it at compile time, which is one fewer
/// thing to cache and one fewer way to be wrong about a closed generic.
/// </para>
/// <para>
/// The constraint <c>where TResponse : Result, IResult&lt;TResponse&gt;</c> is what carries it:
/// the container quietly skips an open-generic behavior whose constraints a request does not
/// satisfy, so a response that is not a result simply never reaches the behavior that needs one.
/// Every request in this system returns a result — <c>ICommand</c> and <c>IQuery</c> see to
/// that — so nothing is skipped in practice.
/// </para>
/// </remarks>
public interface IResult<TSelf>
    where TSelf : IResult<TSelf>
{
    /// <summary>Creates a failed result of this type.</summary>
    /// <param name="error">What went wrong.</param>
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "The parameter is an Error and calling it anything else would be a worse name to read. The rule guards against VB implementers, which a C#-only backend does not have.")]
    static abstract TSelf FromError(Error error);
}

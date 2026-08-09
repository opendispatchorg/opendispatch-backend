namespace OpenDispatch.Domain.Common;

/// <summary>
/// Thrown when code tries to drive an aggregate into a state its rules forbid — dispatching
/// a cancelled job, paying an invoice twice.
/// </summary>
/// <remarks>
/// <para>
/// An exception rather than a <c>Result</c> on purpose. Handlers return <c>Result</c> for
/// failures they expect and can explain to a user; reaching an illegal state is not one of
/// those, it is a bug in the caller, and the domain refuses rather than negotiating.
/// Expected failures should be checked before the aggregate is asked to do the impossible.
/// </para>
/// <para>
/// <strong>This is not the only way the domain throws, and the difference is deliberate.</strong>
/// Aggregates raise this when they refuse an otherwise well-formed request — an illegal
/// status transition, a second payment against a settled invoice. The primitive value
/// objects instead throw the BCL argument exceptions, because a latitude of 4,000 or a
/// window that ends before it starts is not a request the domain is declining; it is
/// malformed input that request validation should have rejected long before it reached a
/// constructor.
/// </para>
/// <para>
/// So when the API maps exceptions to status codes: a <see cref="DomainException"/> is the
/// domain answering "no" and belongs in the 4xx range, while an
/// <see cref="ArgumentException"/> escaping the domain means validation at the edge has a
/// hole and should be reported as a server fault, not dressed up as a client error.
/// </para>
/// </remarks>
public sealed class DomainException : Exception
{
    /// <summary>Creates the exception with a message describing the rule that was broken.</summary>
    public DomainException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying cause.</summary>
    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

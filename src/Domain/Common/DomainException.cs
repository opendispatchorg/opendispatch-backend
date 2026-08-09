namespace OpenDispatch.Domain.Common;

/// <summary>
/// Thrown when code tries to drive an aggregate into a state its rules forbid — dispatching
/// a cancelled job, paying an invoice twice.
/// </summary>
/// <remarks>
/// An exception rather than a <c>Result</c> on purpose. Handlers return <c>Result</c> for
/// failures they expect and can explain to a user; reaching an illegal state is not one of
/// those, it is a bug in the caller, and the domain refuses rather than negotiating.
/// Expected failures should be checked before the aggregate is asked to do the impossible.
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

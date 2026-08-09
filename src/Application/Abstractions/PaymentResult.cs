namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// What came back from trying to take money.
/// </summary>
/// <remarks>
/// <para>
/// A refused card is an ordinary outcome, not an exception: it is the customer's bank
/// declining, which the dispatcher has to be told about in words rather than as a 500. The
/// handler turns this into a <c>Result</c> at step 40; this type stays free of that so an
/// adapter never has to know how failures are reported at the edge.
/// </para>
/// <para>
/// Built only through <see cref="Taken"/> and <see cref="Refused"/>, so there is no such
/// thing as a success carrying a decline reason.
/// </para>
/// </remarks>
public sealed record PaymentResult
{
    private PaymentResult(bool succeeded, string? reference, string? failure)
    {
        Succeeded = succeeded;
        Reference = reference;
        Failure = failure;
    }

    /// <summary>Whether the money was taken.</summary>
    public bool Succeeded { get; }

    /// <summary>
    /// The processor's own identifier for the payment; <see langword="null"/> if it was
    /// refused. The only thing that ties a payment in this system to one in a statement, so
    /// a gateway that has nothing to say here has not really succeeded.
    /// </summary>
    public string? Reference { get; }

    /// <summary>Why it was refused, in terms fit to show a person; <see langword="null"/> if it succeeded.</summary>
    public string? Failure { get; }

    /// <summary>The money was taken.</summary>
    /// <param name="reference">The processor's identifier for the payment.</param>
    public static PaymentResult Taken(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        return new PaymentResult(true, reference, null);
    }

    /// <summary>The money was not taken.</summary>
    /// <param name="reason">Why, in terms fit to show a person.</param>
    public static PaymentResult Refused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new PaymentResult(false, null, reason);
    }
}

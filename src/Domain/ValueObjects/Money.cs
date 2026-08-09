namespace OpenDispatch.Domain.ValueObjects;

/// <summary>
/// A monetary amount held as a whole number of cents.
/// </summary>
/// <remarks>
/// <para>
/// Integer cents rather than <see cref="decimal"/> dollars: every amount in the system is
/// a currency amount that must round-trip through the database, the wire, and a client
/// without drifting, and sub-cent amounts are not a thing an invoice can express. Storing
/// the smallest indivisible unit makes that structural instead of a rounding convention
/// everyone has to remember.
/// </para>
/// <para>
/// Arithmetic is <c>checked</c>. Silently wrapping a total past <see cref="long.MaxValue"/>
/// is the one failure mode of a fixed-width money type, so it throws
/// <see cref="OverflowException"/> instead.
/// </para>
/// <para>
/// Negative amounts are legal — credits, adjustments and refunds are real.
/// </para>
/// </remarks>
public readonly record struct Money(long Cents)
{
    private const decimal CentsPerDollar = 100m;

    /// <summary>
    /// Converts a dollar amount to cents, rounding to the nearest cent (halves away from
    /// zero, the ordinary currency convention).
    /// </summary>
    /// <remarks>
    /// Rounding rather than truncating matters because callers pass computed values — a
    /// tax or margin multiplication lands on three or more decimal places, and truncation
    /// would silently shave money off every one of them.
    /// </remarks>
    /// <exception cref="OverflowException">
    /// The amount does not fit in <see cref="long"/> cents.
    /// </exception>
    public static Money FromDollars(decimal dollars) =>
        new(checked((long)decimal.Round(dollars * CentsPerDollar, 0, MidpointRounding.AwayFromZero)));

    /// <summary>Adds another amount to this one.</summary>
    /// <exception cref="OverflowException">The sum does not fit in <see cref="long"/> cents.</exception>
    public Money Add(Money other) => new(checked(Cents + other.Cents));
}

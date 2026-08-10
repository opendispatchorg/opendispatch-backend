using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Persistence.Conversions;

/// <summary>
/// Stores <see cref="Money"/> as the whole number of cents it already is.
/// </summary>
/// <remarks>
/// <para>
/// One column rather than a complex type with a single member: <see cref="Money"/> holds
/// exactly one value, and <c>bigint</c> is the storage that keeps the domain's promise that an
/// amount cannot drift. A <c>numeric</c> or <c>money</c> column would reintroduce the rounding
/// question the value object exists to settle.
/// </para>
/// <para>
/// Currency is not stored because the system does not model one. When it does, this becomes a
/// complex type of amount plus currency, and every money column gains a sibling.
/// </para>
/// </remarks>
internal sealed class MoneyConverter()
    : ValueConverter<Money, long>(money => money.Cents, cents => new Money(cents))
{
}

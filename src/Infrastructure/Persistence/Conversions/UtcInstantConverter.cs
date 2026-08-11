using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OpenDispatch.Infrastructure.Persistence.Conversions;

/// <summary>
/// Puts every instant into UTC on its way to the column.
/// </summary>
/// <remarks>
/// <para>
/// Npgsql refuses a <see cref="DateTimeOffset"/> with a non-zero offset against
/// <c>timestamptz</c> — <c>only offset 0 (UTC) is supported</c> — so every instant in the system
/// has to arrive already converted. That was true from step 25 and was being honoured by six
/// handlers calling <c>ToUniversalTime()</c> before they built a value object, which is six places
/// to remember a rule that has nothing to do with any of them.
/// </para>
/// <para>
/// Here it is one rule, applied to the model, that no handler can forget and no future slice has to
/// be told about — the same argument the strongly-typed ids and <c>Money</c> are registered by. The
/// conversion loses only the offset, which nothing reads: a <see cref="DateTimeOffset"/> is an
/// absolute instant, the domain compares by instant, and the column stores an instant.
/// </para>
/// <para>
/// Reading back is the identity, because what comes out of <c>timestamptz</c> is already UTC.
/// </para>
/// <para>
/// What this deliberately does <em>not</em> do is round to the microsecond a column can hold. That
/// belongs before the domain sees the value, not on the way past it: the plan is written by
/// comparing what a stop should be against what it already is, and the second of those has been
/// through the database. See <c>StopPlacement.Storable</c>.
/// </para>
/// </remarks>
internal sealed class UtcInstantConverter()
    : ValueConverter<DateTimeOffset, DateTimeOffset>(
        instant => instant.ToUniversalTime(),
        stored => stored)
{
}

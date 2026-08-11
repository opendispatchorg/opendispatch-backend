using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Persistence.Conversions;

/// <summary>
/// A storage key as its text.
/// </summary>
/// <remarks>
/// It goes back through <see cref="StorageKey.Parse"/> rather than a private constructor, so a row
/// holding something that is not a key fails loudly at load. That is deliberate: the value names a
/// file, and the failure mode of accepting a malformed one is reading somewhere it should not.
/// </remarks>
internal sealed class StorageKeyConverter()
    : ValueConverter<StorageKey, string>(key => key.Value, value => StorageKey.Parse(value))
{
}

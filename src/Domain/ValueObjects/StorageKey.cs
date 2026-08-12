using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.ValueObjects;

/// <summary>
/// Where an attachment's bytes live, as far as anything outside the storage adapter is concerned.
/// </summary>
/// <remarks>
/// <para>
/// A value object rather than a string, and the reason is security rather than tidiness. A storage
/// key becomes a path on a disk in the dev adapter and an object name in a bucket later, so a key
/// built from something a caller sent — a filename, a client-supplied path — is how directory
/// traversal gets in. This type cannot be constructed from arbitrary text: it is
/// <em>derived</em> from a tenant and an attachment id, both of which are already values the system
/// vouches for.
/// </para>
/// <para>
/// The one way to make one from text is <see cref="Parse"/>, which exists for the row coming back
/// out of the database and refuses anything that is not the shape this type produces. A key that
/// was stored by an older version and no longer parses is a loud failure at load rather than a
/// quiet read of the wrong file.
/// </para>
/// <para>
/// The organization is in the key, so a bucket or a directory listing sorts by tenant and a
/// misrouted read cannot land on another organization's photo by guessing one identifier.
/// </para>
/// </remarks>
public readonly record struct StorageKey
{
    private const char Separator = '/';

    private StorageKey(string value) => Value = value;

    /// <summary>The key itself: <c>{org}/{attachment}</c>, both as plain uuids.</summary>
    public string Value { get; }

    /// <summary>Derives the key an attachment's bytes are stored under.</summary>
    /// <param name="orgId">The tenant that owns the attachment.</param>
    /// <param name="attachmentId">The attachment, by the id its device gave it.</param>
    public static StorageKey For(OrgId orgId, AttachmentId attachmentId) =>
        new($"{orgId.Value:D}{Separator}{attachmentId.Value:D}");

    /// <summary>
    /// Rebuilds a key that came out of storage.
    /// </summary>
    /// <param name="value">The stored key.</param>
    /// <exception cref="DomainException">
    /// The text is not a key this type produces — two uuids separated by a slash, and nothing else.
    /// Anything looser would let a stored value name a file outside the store.
    /// </exception>
    public static StorageKey Parse(string value)
    {
        var parts = value?.Split(Separator) ?? [];

        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "D", out _) || !Guid.TryParseExact(parts[1], "D", out _))
        {
            throw new DomainException($"'{value}' is not a storage key.");
        }

        return new StorageKey(value!);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

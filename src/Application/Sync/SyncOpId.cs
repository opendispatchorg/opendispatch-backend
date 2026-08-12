namespace OpenDispatch.Application.Sync;

/// <summary>
/// Identifies one operation a technician's device performed — and, because the device mints it
/// before the operation is ever sent, the idempotency key the server dedupes by.
/// </summary>
/// <remarks>
/// <para>
/// A wrapper rather than a raw <see cref="Guid"/>, for the same reason the domain's identities
/// are: an op id and the id of the entity the op is about are both uuids, they sit next to each
/// other in every signature here, and swapping them would compile.
/// </para>
/// <para>
/// It lives here rather than in <c>Domain.Identifiers</c> with the others because a sync op is
/// not a business concept. It is a fact about the protocol — what a phone said it did — and the
/// domain has no opinion about phones.
/// </para>
/// </remarks>
/// <param name="Value">The identifier the device generated.</param>
public readonly record struct SyncOpId(Guid Value)
{
    /// <summary>Rebuilds an identifier from a value that came off the wire or out of storage.</summary>
    public static SyncOpId From(Guid value) => new(value);
}

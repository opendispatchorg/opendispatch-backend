namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies an Attachment — a photo or a signature captured in the field.
/// </summary>
/// <remarks>
/// <para>
/// The device mints it, before the photo has ever left the phone, and it is the idempotency key:
/// an upload that times out halfway and is retried carries the same id, which is how the server
/// knows it already has those bytes. Same reasoning as <c>SyncOpId</c>, one layer up — this one is
/// a domain identity because an attachment is a thing the business has, not a message about one.
/// </para>
/// <para>
/// It has a <see cref="From"/> and no <c>New</c>, because nothing on the server invents one. An
/// attachment nobody captured does not exist, and an id the server made up would belong to no
/// device and could never be matched against a retry.
/// </para>
/// </remarks>
public readonly record struct AttachmentId(Guid Value)
{
    /// <summary>Rebuilds an identifier from a value that came off the wire or out of storage.</summary>
    public static AttachmentId From(Guid value) => new(value);
}

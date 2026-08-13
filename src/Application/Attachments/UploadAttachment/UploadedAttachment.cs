namespace OpenDispatch.Application.Attachments.UploadAttachment;

/// <summary>Where an upload landed.</summary>
/// <param name="ServerId">
/// The handle the server holds this content under — its <c>StorageKey</c>, as a string. Not the
/// <see cref="OpenDispatch.Domain.Identifiers.AttachmentId"/> the device already knows: a device
/// asking "did my upload land?" wants to be told something it did not already have, and the
/// storage key is what the server actually decided (Document 3, step 43b's own entry on this
/// question). Opaque to the caller, the same register as <c>SyncCursor</c> — a client stores it
/// and does not parse it.
/// </param>
public sealed record UploadedAttachment(string ServerId);

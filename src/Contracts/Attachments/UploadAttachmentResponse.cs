namespace OpenDispatch.Contracts.Attachments;

/// <summary>The response from <c>POST /jobs/{id}/attachments</c>.</summary>
/// <param name="ServerId">
/// The handle the server holds this content under. Opaque — a client stores it and does not parse
/// it, the same register as a sync cursor.
/// </param>
public sealed record UploadAttachmentResponse(string ServerId);

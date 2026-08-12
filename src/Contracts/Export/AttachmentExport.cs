namespace OpenDispatch.Contracts.Export;

/// <summary>One captured photo or signature's metadata, as it appears in <c>GET /export</c>.</summary>
/// <param name="Id">The device's own id for the capture.</param>
/// <param name="JobId">The job it was captured against.</param>
/// <param name="Kind">Photograph or signature.</param>
/// <param name="ServerId">The handle its bytes are stored under.</param>
/// <param name="CreatedAt">When it was captured, by the device's clock.</param>
public sealed record AttachmentExport(Guid Id, Guid JobId, AttachmentKind Kind, string ServerId, DateTimeOffset CreatedAt);

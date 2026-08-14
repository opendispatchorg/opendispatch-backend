using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Attachments;

/// <summary>
/// What can be wrong when somebody asks for a capture.
/// </summary>
/// <remarks>
/// Two misses, and telling them apart is the point: a row nobody has is a caller asking for the
/// wrong thing, while a row whose bytes are missing is this deployment's problem — a volume that
/// was not mounted, a directory somebody cleared, a bucket the credentials no longer reach. Both
/// answer 404, because from the caller's side there is nothing to fetch either way, but only one of
/// them means somebody should look at the server.
/// </remarks>
public static class AttachmentErrors
{
    /// <summary>The code a caller branches on when there is no such attachment.</summary>
    public const string NotFoundCode = "attachment.notFound";

    /// <summary>The code that says the metadata is here and the bytes are not.</summary>
    public const string ContentMissingCode = "attachment.contentMissing";

    /// <summary>No attachment with that id — in this tenant, which is all a caller can ask about.</summary>
    public static Error NotFound(AttachmentId id) =>
        Error.NotFound(NotFoundCode, $"No attachment {id.Value} was found.");

    /// <summary>
    /// The attachment exists and its content does not.
    /// </summary>
    /// <remarks>
    /// Reported rather than dressed up as a 500: the request was fine, the row is real, and what a
    /// client can do about it — show the capture as unavailable rather than retry forever — is
    /// different from what it does with a server error.
    /// </remarks>
    public static Error ContentMissing(AttachmentId id) => Error.NotFound(
        ContentMissingCode,
        $"Attachment {id.Value} is recorded but its content is not in the store.");
}

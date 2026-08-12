namespace OpenDispatch.Domain.Attachments;

/// <summary>
/// What a technician captured.
/// </summary>
/// <remarks>
/// The two are separate because they are used differently rather than because they are stored
/// differently: a signature is evidence the customer accepted the work and belongs on the invoice
/// trail, a photo is a record of what was found. Values are numbered explicitly because this enum
/// is mirrored to the clients.
/// </remarks>
public enum AttachmentKind
{
    /// <summary>A picture of the site, the fault, or the finished work.</summary>
    Photo = 0,

    /// <summary>The customer's signature, captured on the technician's screen.</summary>
    Signature = 1,
}

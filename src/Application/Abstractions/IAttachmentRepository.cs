using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Loads and stores attachment metadata — everything about a capture except its bytes.
/// </summary>
/// <remarks>
/// <para>
/// Two methods, and the first one is the whole point: an upload asks whether it has seen this
/// attachment id before, and if it has, there is nothing to do. That check and the primary key
/// behind it are what make an upload safe to retry over a connection that keeps dropping.
/// </para>
/// <para>
/// Nothing here takes an <c>OrgId</c>. Tenant scope is ambient, so a device asking about an
/// attachment id that belongs to another organization is told there is no such attachment — which
/// is both true from where it is standing and the answer that leaks nothing.
/// </para>
/// </remarks>
public interface IAttachmentRepository
{
    /// <summary>
    /// Fetches one attachment by the id its device gave it.
    /// </summary>
    /// <returns>
    /// The attachment, or <see langword="null"/> if this tenant has none with that id — which is
    /// how an upload tells a retry from a first attempt.
    /// </returns>
    Task<Attachment?> GetAsync(AttachmentId id, CancellationToken ct);

    /// <summary>
    /// Stages a newly captured attachment. It is written when the unit of work is saved, which is
    /// after its bytes are in the store: metadata pointing at content that is not there yet would
    /// be readable for the length of a transaction.
    /// </summary>
    void Add(Attachment attachment);

    /// <summary>
    /// Fetches every attachment's metadata in the tenant, unpaged — step 50b's <c>GET /export</c>
    /// entry, following <see cref="ICustomerRepository.ListAsync"/>: business records a shop is
    /// entitled to take with it, not a display list a page has to render. Content itself is not
    /// included — a caller wanting bytes goes through <see cref="IAttachmentStorage"/> by the
    /// returned attachment's <c>StorageKey</c>.
    /// </summary>
    IAsyncEnumerable<Attachment> StreamAsync(CancellationToken ct);
}

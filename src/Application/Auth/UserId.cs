namespace OpenDispatch.Application.Auth;

/// <summary>Identifies one row in the minimal user store.</summary>
/// <remarks>
/// A wrapper rather than a raw <see cref="Guid"/>, for the same reason the domain's identities
/// are — and it lives here rather than in <c>Domain.Identifiers</c> with them for the same
/// reason <c>SyncOpId</c> lives in <c>Application.Sync</c> rather than there: a login is not a
/// business concept, so the domain has no opinion about who may authenticate.
/// </remarks>
/// <param name="Value">The identifier.</param>
public readonly record struct UserId(Guid Value)
{
    /// <summary>Mints a new identifier for a user that does not exist yet.</summary>
    public static UserId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage.</summary>
    public static UserId From(Guid value) => new(value);
}

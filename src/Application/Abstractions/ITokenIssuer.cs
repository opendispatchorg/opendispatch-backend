using OpenDispatch.Application.Auth;

namespace OpenDispatch.Application.Abstractions;

/// <summary>Turns an authenticated user into a bearer token carrying their org and role.</summary>
/// <remarks>
/// Synchronous, like <c>IClock</c>: signing a token is CPU-bound, not I/O, so there is nothing
/// for a caller to await. The one implementation (Infrastructure, JWT) is the whole of Document
/// 2 §7's "JWT bearer tokens" for the issuing side; validating one back is ASP.NET Core's own
/// bearer handler, configured in the composition root, and never routed through this port.
/// </remarks>
public interface ITokenIssuer
{
    /// <summary>Issues a token for this user, valid until its <see cref="AuthToken.ExpiresAt"/>.</summary>
    AuthToken Issue(AuthUser user);
}

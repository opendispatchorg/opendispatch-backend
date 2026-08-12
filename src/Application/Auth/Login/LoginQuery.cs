using OpenDispatch.Application.Messaging;

namespace OpenDispatch.Application.Auth.Login;

/// <summary>Exchanges a username and password for a token.</summary>
/// <param name="Username">Whoever is asking.</param>
/// <param name="Password">Proof it is them, sent in the clear over the TLS connection the host terminates.</param>
/// <remarks>
/// A query, not a command. It changes nothing — the store this step asks for is seeded out of
/// band, not through login (see <c>IUserStore.AddAsync</c>) — and the pipeline gives a
/// transaction only to commands, for the same reason a login should not want one: there is
/// nothing here for a rollback to undo.
/// </remarks>
public sealed record LoginQuery(string Username, string Password) : IQuery<AuthToken>;

namespace OpenDispatch.Application.Auth;

/// <summary>What a successful login hands back.</summary>
/// <param name="Token">The bearer token itself, opaque to everything but the party that reads it.</param>
/// <param name="ExpiresAt">When it stops being accepted. The caller is expected to log in again after this.</param>
public sealed record AuthToken(string Token, DateTimeOffset ExpiresAt);

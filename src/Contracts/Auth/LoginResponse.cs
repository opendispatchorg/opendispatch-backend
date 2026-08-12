namespace OpenDispatch.Contracts.Auth;

/// <summary>What a successful <c>POST /auth/login</c> returns.</summary>
/// <param name="Token">The bearer token — send it as <c>Authorization: Bearer {Token}</c>.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);

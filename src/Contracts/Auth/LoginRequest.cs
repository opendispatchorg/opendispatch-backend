namespace OpenDispatch.Contracts.Auth;

/// <summary>The body of <c>POST /auth/login</c>.</summary>
/// <param name="Username">Whoever is asking.</param>
/// <param name="Password">Proof it is them.</param>
public sealed record LoginRequest(string Username, string Password);

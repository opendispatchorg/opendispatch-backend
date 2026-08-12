namespace OpenDispatch.Infrastructure.Auth;

/// <summary>What <see cref="JwtTokenIssuer"/> needs to sign a token.</summary>
/// <remarks>
/// A plain record rather than <c>Api.Configuration.JwtOptions</c> itself, for the same reason
/// the connection string and the attachment root arrive at <c>AddInfrastructure</c> as factories
/// rather than as options types: Infrastructure may not reference Api (Document 2 §2), so the
/// host translates its own bound, validated configuration into whatever shape this layer asks
/// for.
/// </remarks>
/// <param name="SigningKey">The symmetric key every token is signed and verified with.</param>
/// <param name="Issuer">The <c>iss</c> claim.</param>
/// <param name="Audience">The <c>aud</c> claim.</param>
/// <param name="Expiry">How long a token is accepted for after it is issued.</param>
public sealed record JwtSigningOptions(string SigningKey, string Issuer, string Audience, TimeSpan Expiry);

using System.ComponentModel.DataAnnotations;

namespace OpenDispatch.Api.Configuration;

/// <summary>
/// JWT bearer settings, bound from the <c>Jwt</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="SigningKey"/> is a secret and belongs in the environment or a secret manager in
/// production, the same as <see cref="DatabaseOptions.ConnectionString"/> — the value committed
/// in <c>appsettings.json</c> is a local development default, not a production credential.
/// </remarks>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The symmetric key every token is signed and verified with. At least 32 characters, so an
    /// HMAC-SHA256 key is not weaker than the algorithm signing with it.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>The <c>iss</c> claim every token carries and is checked against.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>The <c>aud</c> claim every token carries and is checked against.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// How long a token is accepted for after it is issued. Generous by default — a work shift
    /// plus room either side — because nothing in the build plan gives a signed-in device a way
    /// to refresh one, and a technician offline in a basement cannot log in again mid-job.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ExpiryMinutes { get; init; } = 720;
}

/// <summary>Registration for <see cref="JwtOptions"/>.</summary>
public static class JwtOptionsRegistration
{
    /// <summary>
    /// Binds and validates <see cref="JwtOptions"/>. Validation runs at startup so a host with
    /// no signing key fails immediately and loudly, rather than at the first login.
    /// </summary>
    public static IServiceCollection AddJwtOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}

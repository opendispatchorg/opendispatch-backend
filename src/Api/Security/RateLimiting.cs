using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace OpenDispatch.Api.Security;

/// <summary>
/// How often one caller may try to log in, bound from the <c>RateLimit</c> configuration section.
/// </summary>
public sealed record RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>
    /// Whether the limiter is applied. On everywhere except where a host explicitly opts out —
    /// which today means the integration suite, whose classes log in dozens of times against one
    /// host from one address and would otherwise be rate-limiting themselves.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Attempts allowed per window, per client address.</summary>
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; init; } = 20;

    /// <summary>How long the window is.</summary>
    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; init; } = 300;
}

/// <summary>
/// A cap on credential guessing (<c>POST /auth/login</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Login is the one route where an anonymous caller can make this server do expensive work
/// on request.</strong> Verifying a password is 100,000 PBKDF2 iterations by design
/// (<c>Pbkdf2PasswordHasher</c>) — deliberately slow so a stolen hash is expensive to attack, which
/// also means an unlimited login endpoint is a way to spend this host's CPU without holding an
/// account. Everything else in this API needs a token first, and a token is issued here.
/// </para>
/// <para>
/// <strong>Partitioned by client address, not by username.</strong> The username arrives in the
/// JSON body, which means reading it at partition time would mean buffering and parsing a body
/// before deciding whether to accept the request at all — the work the limit exists to avoid. An
/// address-partitioned window is the standard shape and stops the attack that matters (many guesses
/// from one place); the defaults are set so an office behind one NAT address does not notice.
/// </para>
/// <para>
/// <strong>The numbers are configuration, not constants</strong>, so a deployment behind a shared
/// address can raise them and the test that proves the limiter works can lower them to two without
/// either one editing code.
/// </para>
/// </remarks>
public static class RateLimiting
{
    /// <summary>The policy name <c>POST /auth/login</c> asks for.</summary>
    public const string LoginPolicy = "login";

    /// <summary>Registers the limiter and the login policy.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">Where <see cref="RateLimitOptions"/> is bound from.</param>
    public static IServiceCollection AddLoginRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            // The options are resolved per request rather than read here, and that is not a style
            // choice: configuration read at registration time is read *before* a host's own
            // configuration sources are applied, so an eager read here would silently ignore
            // anything a deployment (or the test harness) overrode. Found by a test that set a
            // limit of two and was answered on the twentieth.
            limiter.AddPolicy(LoginPolicy, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

                return options.Enabled
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        // Null for an in-process TestServer connection, and shared by everything
                        // behind one NAT — both correct: the partition is "one place on the
                        // network", and a place this server cannot identify is one place.
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = options.PermitLimit,
                            Window = TimeSpan.FromSeconds(options.WindowSeconds),

                            // No queue: a caller past the limit is told so immediately rather than
                            // being held open, which would tie up a connection per guess and hand
                            // an attacker a cheaper way to exhaust this host than guessing.
                            QueueLimit = 0,
                        })
                    : RateLimitPartition.GetNoLimiter("disabled");
            });

            // The refusal is shaped like every other failure this API returns (Document 3,
            // step 46), rather than the framework's default empty 429 body.
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problemDetails = context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();

                await problemDetails.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many attempts.",
                        Detail = "Too many sign-in attempts from this address. Try again shortly.",
                    },
                }).ConfigureAwait(false);
            };
        });

        return services;
    }
}

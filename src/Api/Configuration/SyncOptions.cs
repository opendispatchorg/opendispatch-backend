using System.ComponentModel.DataAnnotations;

namespace OpenDispatch.Api.Configuration;

/// <summary>
/// How much of a technician's stream one pull may carry, bound from the <c>Sync</c> configuration
/// section.
/// </summary>
/// <remarks>
/// <para>
/// The cap exists because <c>GET /sync/pull?since=0</c> is a real request — a reinstalled app, a new
/// phone, a device that has been in a van for a month — and without it the answer is the
/// technician's entire history in one response over whatever connection they have.
/// </para>
/// <para>
/// Counted in <strong>transactions</strong>, not rows or bytes, because a page may not split one:
/// every row a transaction wrote shares its change stamp, and a device resuming mid-stamp would ask
/// for the same cursor forever. Two hundred is a day's dispatching many times over — an optimise of
/// forty jobs is one transaction — so an ordinary pull is never capped and a first sync drains in a
/// handful of round trips.
/// </para>
/// <para>
/// A deployment that wants smaller responses lowers it. There is no upper bound to enforce: the
/// number is the operator's own, and a host configured with a large one is choosing its own
/// response sizes rather than exposing anything.
/// </para>
/// </remarks>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>Transactions per pull. Must be at least one, or no device could ever advance.</summary>
    [Range(1, int.MaxValue)]
    public int PullPageTransactions { get; init; } = 200;
}

/// <summary>
/// Registration for <see cref="SyncOptions"/>.
/// </summary>
public static class SyncOptionsRegistration
{
    /// <summary>
    /// Binds and validates <see cref="SyncOptions"/>, on start like the rest: a host configured with
    /// a page of zero would answer every pull with nothing and no way forward, and finding that out
    /// at boot beats finding it out from a field technician.
    /// </summary>
    public static IServiceCollection AddSyncOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<SyncOptions>()
            .Bind(configuration.GetSection(SyncOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}

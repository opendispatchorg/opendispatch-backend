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

    /// <summary>
    /// Roughly how many rows a pull may carry, as a second bound on the same page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transactions alone bound the wrong thing, which the load measurement made visible: a page of
    /// two hundred transactions is small when each is one request, and enormous when one of them
    /// wrote a thousand rows. An optimise writes a day's plan in a single transaction; a bulk import
    /// writes in batches; the seeded shop wrote a year in twenty. A phone asked for its first sync
    /// against that received <strong>12,884 changes in one 2.3 MB response</strong> — over whatever
    /// connection a van has.
    /// </para>
    /// <para>
    /// So a page also stops when it has taken about this many rows. <em>About</em>, and deliberately:
    /// the count is what the stamps say they carry, before the reader knows which rows overlap
    /// between a stop and its job, and a transaction is still never split. One transaction larger
    /// than the whole budget is sent whole and overruns it — the alternative is a device that can
    /// never advance past it.
    /// </para>
    /// <para>
    /// Two thousand is a few hundred kilobytes of a technician's world: several days of dispatching,
    /// and a first sync that drains in a handful of round trips rather than one that has to survive
    /// a single enormous one.
    /// </para>
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int PullPageRows { get; init; } = 2_000;
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

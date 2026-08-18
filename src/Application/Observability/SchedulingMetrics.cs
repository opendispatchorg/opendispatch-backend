using System.Diagnostics.Metrics;

namespace OpenDispatch.Application.Observability;

/// <summary>
/// What the scheduling engine costs in production (Document 3, step 54).
/// </summary>
/// <remarks>
/// <para>
/// Optimising a day is the one operation in this system whose cost grows with the data rather than
/// with the request: a local search over a day of forty jobs and a day of four hundred are the same
/// call and are not the same wait. It is therefore the one that has to be measured, and the number
/// worth having is a distribution — a mean hides the afternoon that took nine seconds.
/// </para>
/// <para>
/// Registered as a singleton so the instruments are created once; recording on them is
/// thread-safe by design.
/// </para>
/// </remarks>
public sealed class SchedulingMetrics
{
    /// <summary>The instrument name a collector reads optimize latency from.</summary>
    public const string OptimizeDurationName = "opendispatch.scheduling.optimize.duration";

    private readonly Histogram<double> _optimizeDuration;

    /// <summary>Creates the instruments on the shared <see cref="OpenDispatchMetrics.MeterName"/> meter.</summary>
    /// <param name="meters">
    /// The host's meter factory. Injected rather than a <c>static readonly Meter</c> so the
    /// instruments belong to the container that made them — which is what lets one test observe one
    /// host's measurements while another host runs beside it.
    /// </param>
    public SchedulingMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(OpenDispatchMetrics.MeterName);

        _optimizeDuration = meter.CreateHistogram<double>(
            OptimizeDurationName,
            unit: "ms",
            description: "Time to re-plan a day: reading the jobs and crew, solving, and staging the plan.");
    }

    /// <summary>Records one completed optimisation.</summary>
    /// <param name="elapsed">
    /// How long the handler took — the reads, the solve and the writes it stages, but not the
    /// commit, which belongs to the transaction behavior wrapped around it and is a database
    /// number rather than an engine one.
    /// </param>
    public void Optimized(TimeSpan elapsed) => _optimizeDuration.Record(elapsed.TotalMilliseconds);
}

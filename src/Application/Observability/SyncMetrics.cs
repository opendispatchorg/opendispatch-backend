using System.Diagnostics.Metrics;

namespace OpenDispatch.Application.Observability;

/// <summary>
/// What the offline-sync protocol is doing to the operations devices push at it
/// (Document 3, step 54).
/// </summary>
/// <remarks>
/// <para>
/// The sync path is the one place in this system where a failure is <em>routine</em> and answered
/// with a 200: a refused operation rides inside a successful push response (see
/// <c>PushOpsHandler</c>), so nothing in an HTTP status code, an exception, or a log level says
/// how the field is actually going. Without these counters the day a client ships a payload this
/// server cannot read looks, from the outside, exactly like a quiet morning.
/// </para>
/// <para>
/// <strong>One counter for refusals, split by reason rather than two counters.</strong> The step's
/// "apply errors" and "conflicts" are the same event seen from two sides, and which side a
/// particular refusal is on is the reason it carries: <c>job.illegalTransition</c> and
/// <c>sync.notesSuperseded</c> are the protocol working — a stale phone met a moved job, an older
/// note lost — while <c>sync.malformedOperation</c> and <c>sync.unsupportedOperation</c> are a
/// client and a server that disagree about the wire, which is a bug somebody has to fix. Splitting
/// them into two instruments at the point of measurement would hard-code that judgement here;
/// carrying the reason as a dimension lets an alert draw the line and still leaves one number for
/// "how much is being refused at all".
/// </para>
/// <para>
/// The reason is always an <c>Error.Code</c> — a small, closed, non-localised set — so the tag
/// cannot explode in cardinality the way a message would.
/// </para>
/// </remarks>
public sealed class SyncMetrics
{
    /// <summary>The instrument name counting operations this server applied.</summary>
    public const string OpsAppliedName = "opendispatch.sync.ops.applied";

    /// <summary>The instrument name counting operations this server refused.</summary>
    public const string OpsConflictedName = "opendispatch.sync.ops.conflicted";

    /// <summary>The tag on <see cref="OpsConflictedName"/> carrying the refusal's error code.</summary>
    public const string ConflictReasonTag = "sync.conflict.reason";

    private readonly Counter<long> _applied;
    private readonly Counter<long> _conflicted;

    /// <summary>Creates the instruments on the shared <see cref="OpenDispatchMetrics.MeterName"/> meter.</summary>
    /// <param name="meters">The host's meter factory — see <see cref="SchedulingMetrics"/> for why it is injected.</param>
    public SyncMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(OpenDispatchMetrics.MeterName);

        _applied = meter.CreateCounter<long>(
            OpsAppliedName,
            unit: "{operation}",
            description: "Field operations a push applied, including ones an earlier push had already applied.");

        _conflicted = meter.CreateCounter<long>(
            OpsConflictedName,
            unit: "{operation}",
            description: "Field operations a push refused, tagged with the error code that refused them.");
    }

    /// <summary>Records the operations one push applied.</summary>
    /// <param name="count">How many. Zero is not recorded — a counter says nothing by staying still.</param>
    public void Applied(int count)
    {
        if (count > 0)
        {
            _applied.Add(count);
        }
    }

    /// <summary>Records one refused operation.</summary>
    /// <param name="reason">The refusing <c>Error.Code</c>.</param>
    public void Conflicted(string reason) =>
        _conflicted.Add(1, new KeyValuePair<string, object?>(ConflictReasonTag, reason));
}

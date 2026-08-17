using OpenDispatch.Api.Configuration;

namespace OpenDispatch.Api.Observability;

/// <summary>
/// The few things this host says about itself as it starts, source-generated.
/// </summary>
/// <remarks>
/// Reserved for facts an operator cannot discover by looking at a running host — configuration that
/// changes what the system is capable of rather than how it behaves on one request. There is one so
/// far.
/// </remarks>
internal static partial class StartupLog
{
    /// <remarks>
    /// Both branches are logged, and neither is a warning. A single-instance deployment with no
    /// backplane is a supported, ordinary way to run this; what is not supported is finding out
    /// which one you have by scaling to two and watching half the office stop receiving updates.
    /// </remarks>
    internal static void BoardRealtime(ILogger logger, bool backplane)
    {
        if (backplane)
        {
            Coordinated(logger);
        }
        else
        {
            SingleInstance(logger);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Dispatch board real-time is coordinated through Redis; this host may be scaled.")]
    private static partial void Coordinated(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Dispatch board real-time is in-process (no SignalR:Redis configured). Run exactly one "
            + "instance, or board events raised on one will not reach clients connected to another.")]
    private static partial void SingleInstance(ILogger logger);

    /// <remarks>
    /// Named endpoint included, because "telemetry is on" and "telemetry is on and pointed at a
    /// collector that moved" look identical from inside this process — the exporter drops what it
    /// cannot deliver rather than failing a request, which is the behaviour we want and the one that
    /// hides a typo. This line is where the typo is visible.
    /// </remarks>
    internal static void Telemetry(ILogger logger, bool exporting, string? endpoint)
    {
        if (exporting)
        {
            Exporting(logger, endpoint ?? "(unset)");
        }
        else
        {
            NotExporting(logger);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Exporting metrics and traces to {Endpoint} over OTLP.")]
    private static partial void Exporting(ILogger logger, string endpoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No telemetry exporter (no Otel:Endpoint configured). Instruments are published on the "
            + "'OpenDispatch' meter for anything attached to this process, and sent nowhere.")]
    private static partial void NotExporting(ILogger logger);

    /// <remarks>
    /// Said at startup for the same reason as the others, and useful in one more way: it is the
    /// first line of the run, so whichever shape it is written in demonstrates the shape.
    /// </remarks>
    internal static void LogFormat(ILogger logger, bool json)
    {
        if (json)
        {
            JsonLogs(logger);
        }
        else
        {
            HumanLogs(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Logging newline-delimited JSON to the console.")]
    private static partial void JsonLogs(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Logging human-readable text to the console. Set Logging:Json for a log pipeline that "
            + "parses properties rather than lines.")]
    private static partial void HumanLogs(ILogger logger);

    /// <remarks>
    /// The most consequential of the four, and the reason the disk branch names the risk rather than
    /// merely stating the setting: attachment content is the only data in this system that is not in
    /// Postgres, so on a platform whose container filesystem is recreated on deploy, a host that
    /// looks entirely healthy is discarding every photograph the field captures. That cannot be seen
    /// from outside the process and is not visible until the second release — which is exactly the
    /// kind of fact these lines exist for.
    /// </remarks>
    internal static void AttachmentStore(ILogger logger, AttachmentOptions attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        if (attachments.UsesObjectStore)
        {
            InBucket(logger, attachments.Bucket, attachments.ServiceUrl is { Length: > 0 } endpoint ? endpoint : "AWS S3");
        }
        else
        {
            OnDisk(logger, attachments.Root);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Attachment content is stored in bucket {Bucket} at {Endpoint}.")]
    private static partial void InBucket(ILogger logger, string bucket, string endpoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Attachment content is stored on the local filesystem at {Root}. It is the only data "
            + "not in Postgres: back it up with the database, and do not run this host on a platform "
            + "with an ephemeral container filesystem without configuring Attachments:Bucket — a "
            + "deploy would destroy every photograph and signature captured since the last one.")]
    private static partial void OnDisk(ILogger logger, string root);
}

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
}

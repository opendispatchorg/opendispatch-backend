using System.Globalization;
using Serilog;
using Serilog.Formatting.Compact;

namespace OpenDispatch.Api.Observability;

/// <summary>
/// Whether this host's log is written for a person or for a machine.
/// </summary>
/// <remarks>
/// <para>
/// The console template that has been here since step 54 is for a developer reading a terminal.
/// A deployment ships its log lines somewhere that parses them — a collector, Loki, CloudWatch — and
/// a parser handed a human-readable template gets one string per line and loses every property the
/// application went to the trouble of attaching, the correlation id first among them.
/// </para>
/// <para>
/// So: one key. <c>Logging:Json</c> writes newline-delimited JSON (Serilog's compact format, which
/// every log pipeline can read); unset writes what it always wrote. The sink is chosen in code
/// rather than through <c>Serilog:WriteTo</c> in configuration deliberately — the configuration
/// route exists and works, but it means an operator writing an assembly-qualified formatter type
/// into JSON, and the failure when they mistype it is a host with no logs at all.
/// </para>
/// <para>
/// Everything else about the logger stays in configuration: minimum levels, overrides, enrichers,
/// and any additional sink a deployment wants are read from <c>Serilog:*</c> as before. This owns
/// the console and nothing else.
/// </para>
/// </remarks>
public static class ConsoleLogging
{
    /// <summary>The configuration key that turns the console into JSON.</summary>
    public const string JsonKey = "Logging:Json";

    /// <summary>
    /// Writes to the console in whichever shape this deployment asked for.
    /// </summary>
    /// <param name="logger">The logger being configured.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The same configuration, for chaining.</returns>
    public static LoggerConfiguration WriteToConsole(
        this LoggerConfiguration logger,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        return IsJson(configuration)
            ? logger.WriteTo.Console(new CompactJsonFormatter())

            // Invariant culture keeps output identical regardless of the host's locale — a date in
            // a log line that changes shape with the machine is one a search cannot match.
            : logger.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
    }

    /// <summary>Whether this host logs JSON.</summary>
    /// <param name="configuration">The host's configuration.</param>
    public static bool IsJson(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetValue(JsonKey, defaultValue: false);
    }
}

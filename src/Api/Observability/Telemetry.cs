using System.Reflection;
using OpenDispatch.Application.Observability;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OpenDispatch.Api.Observability;

/// <summary>
/// Where this host's measurements go, when a deployment says where.
/// </summary>
/// <remarks>
/// <para>
/// The instruments have existed since step 54 and nothing consumed them: a shop's only way to see
/// how the system was behaving was to attach <c>dotnet-counters</c> to the process, which nobody
/// does at three in the morning and nobody can do at all through a container orchestrator. This is
/// the export half — the same meter, the same spans, sent somewhere that keeps them.
/// </para>
/// <para>
/// <strong>Off until configured</strong>, the pattern <see cref="Board.BoardBackplane"/> and CORS
/// already follow: no <c>Otel:Endpoint</c>, no exporter, no dependency on a collector that may not
/// exist. A deployment that wants telemetry names a collector; one that does not is not paying for
/// an SDK to sit in the process, and the host says which it is at startup either way.
/// </para>
/// <para>
/// <strong>OTLP rather than a Prometheus endpoint</strong>, because it carries metrics <em>and</em>
/// traces over one connection to one collector, and because what a deployment does after the
/// collector — Prometheus, Grafana Cloud, Honeycomb, a file — stops being this application's
/// business. A collector is one container; scraping is one more thing to expose and secure.
/// </para>
/// <para>
/// <strong>A collector that is not there must not stop the shop working.</strong> The exporter
/// retries and drops on the floor; it never blocks a request, and the API keeps serving with a
/// misconfigured or dead endpoint. That is pinned by test, because the opposite — telemetry taking
/// the system down — is the failure that makes people delete their instrumentation.
/// </para>
/// </remarks>
public static class Telemetry
{
    /// <summary>The configuration key naming the collector to export to.</summary>
    public const string EndpointKey = "Otel:Endpoint";

    /// <summary>
    /// The configuration key choosing the wire protocol: <c>grpc</c> (the default) or
    /// <c>http/protobuf</c>.
    /// </summary>
    /// <remarks>
    /// Both are ordinary — a collector's gRPC receiver is usually 4317 and its HTTP one 4318 — and
    /// getting it wrong produces a silence that looks exactly like "no telemetry configured". One
    /// key is cheaper than that support call.
    /// </remarks>
    public const string ProtocolKey = "Otel:Protocol";

    /// <summary>What this host calls itself in whatever the collector feeds.</summary>
    /// <remarks>
    /// Overridable, because a deployment running staging and production into one collector needs to
    /// tell them apart, and the service name is where every backend expects that to be said.
    /// </remarks>
    public const string ServiceNameKey = "Otel:ServiceName";

    /// <summary>The name a deployment gets if it does not choose one.</summary>
    public const string DefaultServiceName = "opendispatch-api";

    /// <summary>
    /// Registers metric and trace export, if a collector is configured.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host's configuration, read now rather than per request.</param>
    /// <returns>Whether an exporter was registered — the host says so at startup either way.</returns>
    /// <exception cref="InvalidOperationException">
    /// <c>Otel:Protocol</c> is something other than the two protocols there are. A typo here would
    /// otherwise be indistinguishable from a working exporter nobody is receiving.
    /// </exception>
    public static bool AddOpenDispatchTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var endpoint = configuration[EndpointKey];

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var collector))
        {
            throw new InvalidOperationException(
                $"'{EndpointKey}' must be an absolute URI such as http://collector:4317, not '{endpoint}'.");
        }

        var protocol = Protocol(configuration[ProtocolKey]);
        var service = configuration[ServiceNameKey] ?? DefaultServiceName;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(service, serviceVersion: Version()))
            .WithMetrics(metrics => metrics
                // The instruments this system publishes — the optimiser's duration, the sync
                // counters — beside the ones the framework and the runtime publish. The three
                // together are what an alert can be written against: ours say what the business is
                // doing, theirs say whether the host can keep doing it.
                .AddMeter(OpenDispatchMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(exporter => Configure(exporter, collector, protocol)))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()

                // Npgsql publishes its own spans under this name, so a slow request shows *which*
                // query it was waiting on rather than only that it was slow. Harmless when the
                // driver emits nothing.
                .AddSource("Npgsql")
                .AddOtlpExporter(exporter => Configure(exporter, collector, protocol)));

        return true;
    }

    private static void Configure(OtlpExporterOptions exporter, Uri collector, OtlpExportProtocol protocol)
    {
        exporter.Endpoint = collector;
        exporter.Protocol = protocol;
    }

    private static OtlpExportProtocol Protocol(string? configured) => configured?.ToLowerInvariant() switch
    {
        null or "" or "grpc" => OtlpExportProtocol.Grpc,
        "http/protobuf" or "http" => OtlpExportProtocol.HttpProtobuf,
        _ => throw new InvalidOperationException(
            $"'{ProtocolKey}' must be 'grpc' or 'http/protobuf', not '{configured}'."),
    };

    /// <summary>
    /// The build, so a collector's data can be attributed to a deployed version.
    /// </summary>
    /// <remarks>
    /// The informational version, which carries the source revision when one is stamped — the
    /// question being answered is "did this start when we deployed", and a three-part assembly
    /// version cannot answer it.
    /// </remarks>
    private static string Version() =>
        typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Telemetry).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}

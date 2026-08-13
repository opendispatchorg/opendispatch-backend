namespace OpenDispatch.Application.Observability;

/// <summary>
/// The meter every instrument in this system is published under (Document 3, step 54).
/// </summary>
/// <remarks>
/// <para>
/// One meter, not one per slice. A meter name is what a collector subscribes to — an OpenTelemetry
/// exporter, <c>dotnet-counters</c>, or a test's own <c>MetricCollector</c> — and a host that
/// publishes under three names is one an operator can configure two thirds of correctly. The slice
/// an instrument belongs to is already in its own dotted name.
/// </para>
/// <para>
/// Nothing here decides where measurements <em>go</em>. The instruments are published; whether a
/// deployment exports them to Prometheus, to OTLP, or to nothing at all is that deployment's
/// configuration and not this project's business — which is why no exporter is registered
/// anywhere in the solution.
/// </para>
/// </remarks>
public static class OpenDispatchMetrics
{
    /// <summary>The meter name a collector subscribes to.</summary>
    public const string MeterName = "OpenDispatch";
}

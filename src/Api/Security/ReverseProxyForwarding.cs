using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace OpenDispatch.Api.Security;

/// <summary>
/// Whether to believe <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c>, and from whom.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Off unless a deployment says otherwise, because trusting these headers is a decision
/// about the network, not about the application.</strong> A host reachable only through an ingress
/// should read them: without that, every request appears to come from the proxy over plain HTTP, so
/// the rate limiter above partitions the whole internet into one bucket and every log line names the
/// same address. A host reachable directly must not: anybody can send those headers, and believing
/// them would let a caller forge the address the rate limiter partitions by.
/// </para>
/// <para>
/// <strong>Enabling it with no known proxies clears the trust list deliberately</strong> — the
/// framework otherwise trusts only loopback, which is never the ingress's address in a container.
/// That is safe exactly when this host is unreachable except through the proxy, which is the
/// deployment shape the flag is claiming. A deployment that can name its proxy addresses should:
/// <c>ReverseProxy:KnownProxies</c> and <c>ReverseProxy:KnownNetworks</c> narrow it back down.
/// </para>
/// </remarks>
public static class ReverseProxyForwarding
{
    /// <summary>The configuration section this reads.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>Configures which forwarded headers are read and who may set them.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <remarks>
    /// Bound at resolve time rather than at registration, for the reason <see cref="ClientCors"/>
    /// gives: configuration read while the container is being built is read before a host's own
    /// sources are applied. The middleware is therefore always in the pipeline and reads
    /// <see cref="ForwardedHeaders.None"/> — doing nothing at all — unless this host says it is
    /// behind a proxy.
    /// </remarks>
    public static IServiceCollection AddReverseProxyForwarding(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            if (!configuration.GetValue($"{SectionName}:Enabled", defaultValue: false))
            {
                options.ForwardedHeaders = ForwardedHeaders.None;
                return;
            }

            var knownProxies = configuration.GetSection($"{SectionName}:KnownProxies").Get<string[]>() ?? [];
            var knownNetworks = configuration.GetSection($"{SectionName}:KnownNetworks").Get<string[]>() ?? [];

            // The two that change behaviour here: who the caller is (the rate limiter's partition,
            // every log line) and whether the request arrived over TLS. Not X-Forwarded-Host, which
            // nothing in this API derives a URL from.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var proxy in knownProxies)
            {
                if (IPAddress.TryParse(proxy, out var address))
                {
                    options.KnownProxies.Add(address);
                }
            }

            foreach (var network in knownNetworks)
            {
                var parts = network.Split('/', 2);

                if (parts.Length == 2
                    && IPAddress.TryParse(parts[0], out var prefix)
                    && int.TryParse(parts[1], out var length))
                {
                    options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
                }
            }
        });

        return services;
    }
}

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// Groups every test class that boots its own <see cref="ApiFactory"/>, so xunit never starts
/// two of them at once.
/// </summary>
/// <remarks>
/// <para>
/// <c>WebApplicationFactory&lt;Program&gt;</c> resolves Program.cs's top-level-statement entry
/// point through <c>Microsoft.Extensions.Hosting.HostFactoryResolver</c>, which coordinates
/// through process-wide static state while a host is booting. Two factories starting
/// concurrently race on that state and one loses with
/// <c>"The entry point exited without ever building an IHost."</c> — nondeterministically, and
/// only once there were two classes to race: before step 44, <c>HealthEndpointTests</c> was the
/// only class in the whole suite that ever booted a real host.
/// </para>
/// <para>
/// Classes here still each get their own <see cref="ApiFactory"/> via
/// <c>IClassFixture&lt;ApiFactory&gt;</c> — this collection shares nothing, unlike
/// <see cref="PostgresCollectionDefinition"/>. It only stops xunit from running two of them at
/// the same time, which is the whole fix.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ApiHostCollectionDefinition
{
    public const string Name = "ApiHost";
}

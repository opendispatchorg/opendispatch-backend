namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// Binds <see cref="PostgresFixture"/> to a collection so xunit creates it once and hands
/// the same instance to every test class marked
/// <c>[Collection(PostgresCollectionDefinition.Name)]</c>. Classes in this collection do
/// not run in parallel with each other.
/// </summary>
/// <remarks>
/// <para>
/// xunit's convention would name this <c>PostgresCollection</c>, but CA1711 reserves that
/// suffix for actual collection types, so the attribute's own name is used instead.
/// </para>
/// <para>
/// Since step 45 this collection also carries every class that boots a real host through
/// <c>ApiFactory</c>/<c>WebApplicationFactory&lt;Program&gt;</c>, whether or not that class
/// touches Postgres — <c>HealthEndpointTests</c> and <c>AuthFlowTests</c> among them. That
/// mechanism (<c>Microsoft.Extensions.Hosting.HostFactoryResolver</c>, resolving a
/// top-level-statement entry point) coordinates through process-wide static state while a host
/// boots, and two hosts starting at once race on it — nondeterministically, and reproducibly,
/// found when step 45 added a second real-host class alongside step 44's. Folding those classes
/// in here rather than giving them a collection of their own means one shared "nothing in this
/// collection runs concurrently with anything else in it" guarantee instead of two guarantees
/// that would need to agree with each other. The cost is a Postgres container spun up for a class
/// that does not read it — acceptable, since nothing here is in <c>make test-fast</c>'s path.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}

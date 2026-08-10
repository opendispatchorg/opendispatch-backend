using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Persistence;

/// <summary>
/// The context reaching a real PostGIS database, resolved out of the running host rather than
/// newed up here — so a connection string that never arrives, a provider registered wrongly, or
/// a model that cannot be built all fail this rather than waiting for the first feature slice.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class AppDbContextTests : IDisposable
{
    private readonly ApiFactory _factory;

    public AppDbContextTests(PostgresFixture postgres) =>
        _factory = new ApiFactory { ConnectionString = postgres.ConnectionString };

    [Fact]
    public async Task ConnectsToTheConfiguredDatabase()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await context.Database.CanConnectAsync());
    }

    /// <summary>
    /// The plugin half of the provider registration. Without <c>UseNetTopologySuite()</c> the
    /// context connects perfectly well and only fails much later, when step 25 asks it to store a
    /// <c>GeoPoint</c> as <c>geography(Point)</c> — so the round trip is asserted here, where the
    /// registration is, rather than left to be discovered as a mapping error two steps away.
    /// </summary>
    [Fact]
    public async Task ReadsPostGisGeographyAsANetTopologySuitePoint()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var point = await context.Database
            .SqlQuery<Point>($"""SELECT ST_SetSRID(ST_MakePoint(-0.1276, 51.5072), 4326)::geography AS "Value" """)
            .SingleAsync();

        Assert.Equal(-0.1276, point.X, precision: 6);
        Assert.Equal(51.5072, point.Y, precision: 6);
        Assert.Equal(4326, point.SRID);
    }

    public void Dispose() => _factory.Dispose();
}

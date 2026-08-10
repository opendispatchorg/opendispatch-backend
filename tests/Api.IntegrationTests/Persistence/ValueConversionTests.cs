using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Persistence;

/// <summary>
/// The conversions that let the domain entities be the persisted entities, proved against a real
/// PostGIS database rather than against EF's in-memory model.
/// </summary>
/// <remarks>
/// The options come from the real <see cref="PersistenceRegistration.AddPersistence"/>, so a
/// provider or plugin configured only in the test would not count as configured.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ValueConversionTests : IAsyncLifetime
{
    private static readonly TimeWindow Morning = new(
        new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 10, 12, 30, 0, TimeSpan.Zero));

    // Somewhere unambiguous: a latitude that is not a legal longitude would let an axis swap
    // throw instead of quietly storing the wrong place, which is not the failure being guarded
    // against. These two are both in range, so only the assertion catches a swap.
    private static readonly GeoPoint Trafalgar = new(51.5080, -0.1281);

    private readonly PostgresFixture _postgres;
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly DbContextOptions<AppDbContext> _options;

    public ValueConversionTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _services = new ServiceCollection()
            .AddPersistence(_ => postgres.ConnectionString)
            .BuildServiceProvider();
        _scope = _services.CreateScope();
        _options = _scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
    }

    public async Task InitializeAsync()
    {
        await using var context = NewContext();

        // The schema comes from the model, so the columns under test are the ones the mapping
        // actually asks for. Dropped first because the container is shared for the whole run.
        await context.Database.ExecuteSqlRawAsync(
            $"""DROP TABLE IF EXISTS "{ConverterProbeContext.TableName}";""");
        await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript());
    }

    [Fact]
    public async Task RoundTripsEveryTypedIdAndValueObject()
    {
        var probe = ConverterProbe.Create(Money.FromDollars(1234.56m), Trafalgar, Morning);

        await using (var write = NewContext())
        {
            write.Probes.Add(probe);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var loaded = await read.Probes.SingleAsync(p => p.Id == probe.Id);

        Assert.Equal(probe.Id, loaded.Id);
        Assert.Equal(probe.OrgId, loaded.OrgId);
        Assert.Equal(probe.AssignmentId, loaded.AssignmentId);
        Assert.Equal(probe.CustomerId, loaded.CustomerId);
        Assert.Equal(probe.InvoiceId, loaded.InvoiceId);
        Assert.Equal(probe.LineItemId, loaded.LineItemId);
        Assert.Equal(probe.ServiceLocationId, loaded.ServiceLocationId);
        Assert.Equal(probe.TechnicianId, loaded.TechnicianId);

        Assert.Equal(123_456L, loaded.Amount.Cents);
        Assert.Equal(Morning, loaded.Window);

        // Not Assert.Equal(Trafalgar, ...): a swapped axis is the failure worth naming, and
        // geography round-trips through doubles, so the ordinates are compared separately.
        Assert.Equal(Trafalgar.Lat, loaded.Location.Lat, precision: 6);
        Assert.Equal(Trafalgar.Lng, loaded.Location.Lng, precision: 6);
    }

    /// <summary>
    /// A pair of doubles would satisfy the round trip above and be useless to PostGIS, so the
    /// column type is asserted rather than inferred from the value coming back.
    /// </summary>
    [Fact]
    public async Task StoresAGeoPointAsAGeographyPointColumn()
    {
        await using var connection = await _postgres.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT type, srid
            FROM geography_columns
            WHERE f_table_name = @table AND f_geography_column = 'Location';
            """,
            connection);
        command.Parameters.AddWithValue("table", ConverterProbeContext.TableName);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "Location is not a geography column.");
        Assert.Equal("Point", reader.GetString(0));
        Assert.Equal(4326, reader.GetInt32(1));
    }

    /// <summary>
    /// The version has to move for the token to mean anything, so both halves are asserted: that
    /// a save advances it, and that a writer holding the version it read at loses.
    /// </summary>
    [Fact]
    public async Task AdvancesTheVersionOnSaveAndRefusesAStaleWrite()
    {
        var probe = ConverterProbe.Create(Money.Zero, Trafalgar, Morning);

        await using (var seed = NewContext())
        {
            seed.Probes.Add(probe);
            await seed.SaveChangesAsync();
        }

        await using var dispatcher = NewContext();
        await using var other = NewContext();
        var mine = await dispatcher.Probes.SingleAsync(p => p.Id == probe.Id);
        var theirs = await other.Probes.SingleAsync(p => p.Id == probe.Id);

        Assert.Equal(0L, mine.Version);

        mine.MoveTo(new GeoPoint(51.5194, -0.1270));
        await dispatcher.SaveChangesAsync();

        Assert.Equal(1L, mine.Version);

        theirs.MoveTo(new GeoPoint(51.5007, -0.1246));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());

        await using var read = NewContext();
        var reloaded = await read.Probes.SingleAsync(p => p.Id == probe.Id);

        Assert.Equal(1L, reloaded.Version);
        Assert.Equal(51.5194, reloaded.Location.Lat, precision: 6);
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _services.DisposeAsync();
    }

    private ConverterProbeContext NewContext() => new(_options);
}

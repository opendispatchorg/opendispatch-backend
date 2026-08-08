using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// One PostGIS container for the whole integration suite. Starting a container costs
/// seconds, so it is started once per run and shared by every test in
/// <see cref="PostgresCollectionDefinition"/> rather than per test class.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same multi-arch PostGIS image as docker-compose, so the schema under test and the
    // schema you develop against cannot drift.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("imresamu/postgis:17-3.6")
        .WithDatabase("opendispatch")
        .WithUsername("opendispatch")
        .WithPassword("opendispatch")
        .Build();

    /// <summary>Connection string for the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // The image ships PostGIS, but the extension is enabled per database.
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS postgis;", connection);
        await command.ExecuteNonQueryAsync();

        // Seam: EF Core migrations get applied here, once at suite start, when they exist
        // (step 17). Until then a test that needs a schema creates it itself.
    }

    /// <summary>Opens a connection to the shared container's database.</summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

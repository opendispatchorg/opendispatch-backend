using Npgsql;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests;

/// <summary>
/// Proves the shared harness works: a real PostGIS container, reachable, with the
/// extension enabled and rows surviving a write/read round trip.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class PostgresFixtureTests
{
    private readonly PostgresFixture _postgres;

    public PostgresFixtureTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task ContainerHasPostGisEnabled()
    {
        await using var connection = await _postgres.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT postgis_lib_version();", connection);

        var version = await command.ExecuteScalarAsync() as string;

        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    [Fact]
    public async Task RoundTripsARow()
    {
        await using var connection = await _postgres.OpenConnectionAsync();

        await using (var create = new NpgsqlCommand(
            "CREATE TABLE IF NOT EXISTS harness_probe (id int primary key, label text not null);",
            connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using (var insert = new NpgsqlCommand(
            "INSERT INTO harness_probe (id, label) VALUES (1, 'round-trip') ON CONFLICT (id) DO UPDATE SET label = excluded.label;",
            connection))
        {
            await insert.ExecuteNonQueryAsync();
        }

        await using var read = new NpgsqlCommand("SELECT label FROM harness_probe WHERE id = 1;", connection);
        var label = await read.ExecuteScalarAsync() as string;

        Assert.Equal("round-trip", label);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Persistence;

/// <summary>
/// What the migration actually builds.
/// </summary>
/// <remarks>
/// <see cref="PostgresFixture"/> already migrates its database at the start of the run, so a
/// migration that cannot apply at all fails every test in the suite rather than one here. What is
/// left is the half a passing <c>Migrate()</c> does not tell you: that it runs against a database
/// that has had nothing done to it, and that what it built is what was asked for.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class SchemaTests
{
    private const string ProbeDatabase = "migration_probe";

    private readonly PostgresFixture _postgres;

    public SchemaTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// Its own database, created empty for this test, because the fixture's is not the hardest
    /// case: the PostGIS image installs the extension into the database it creates at startup, so
    /// migrating that one would pass whether or not the migration enabled the extension itself.
    /// A database made with <c>CREATE DATABASE</c> has no PostGIS, which is the state a real
    /// deployment starts from.
    /// </summary>
    [Fact]
    public async Task AppliesToACompletelyFreshDatabase()
    {
        await CreateProbeDatabaseAsync();

        try
        {
            var connectionString = ConnectionStringFor(ProbeDatabase);
            await using var services = new ServiceCollection()
                .AddPersistence(_ => connectionString)
                .BuildServiceProvider();
            using var scope = services.CreateScope();

            // No tenant is resolved, and none is needed: migrating queries no entity, so no
            // query filter is ever evaluated.
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

            var extensions = await QueryAsync(
                connectionString,
                "SELECT extname FROM pg_extension;",
                reader => reader.GetString(0));

            Assert.Contains("postgis", extensions);

            var tables = await QueryAsync(
                connectionString,
                "SELECT tablename FROM pg_tables WHERE schemaname = 'public';",
                reader => reader.GetString(0));

            Assert.Contains("jobs", tables);
            Assert.Contains("assignments", tables);
            Assert.Contains("technicians", tables);
            Assert.Contains("customers", tables);
            Assert.Contains("invoices", tables);
            Assert.Contains("organizations", tables);
            Assert.Contains("service_locations", tables);
            Assert.Contains("line_items", tables);
        }
        finally
        {
            await DropProbeDatabaseAsync();
        }
    }

    /// <summary>
    /// A B-tree over a geography column is not an error, it is just useless: it answers "equals",
    /// and every question the board asks of a point is "near".
    /// </summary>
    [Theory]
    [InlineData("ix_jobs_location")]
    [InlineData("ix_technicians_home_base")]
    [InlineData("ix_service_locations_point")]
    public async Task IndexesGeographyColumnsWithGist(string index)
    {
        var definitions = await IndexDefinitionsAsync();

        Assert.Contains("USING gist", definitions[index], StringComparison.Ordinal);
    }

    /// <summary>
    /// The dispatch board's one question, in one index. It is hand-written in the migration
    /// rather than declared on the model — EF cannot index a complex type's member — so unlike
    /// every other index here, nothing but this test would notice it going missing.
    /// </summary>
    [Fact]
    public async Task IndexesTheDispatchBoardLookup()
    {
        var definitions = await IndexDefinitionsAsync();

        Assert.Contains(
            "(org_id, status, window_start)",
            definitions["ix_jobs_org_id_status_window_start"],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexesEveryOrgScopedTableOnItsTenant()
    {
        var definitions = await IndexDefinitionsAsync();

        Assert.Contains("ix_jobs_org_id", definitions.Keys);
        Assert.Contains("ix_assignments_org_id", definitions.Keys);
        Assert.Contains("ix_technicians_org_id", definitions.Keys);
        Assert.Contains("ix_customers_org_id", definitions.Keys);
        Assert.Contains("ix_invoices_org_id", definitions.Keys);

        // A job is planned once, and this index is the only place that rule can live.
        Assert.Contains("CREATE UNIQUE INDEX", definitions["ix_assignments_job_id"], StringComparison.Ordinal);
    }

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.ConnectionString) { Database = database }.ConnectionString;

    private Task CreateProbeDatabaseAsync() =>
        // FORCE so a connection left over from a previous run cannot block the drop.
        ExecuteOnMaintenanceDatabaseAsync(
            $"DROP DATABASE IF EXISTS {ProbeDatabase} WITH (FORCE);",
            $"CREATE DATABASE {ProbeDatabase};");

    private Task DropProbeDatabaseAsync() =>
        ExecuteOnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {ProbeDatabase} WITH (FORCE);");

    private async Task ExecuteOnMaintenanceDatabaseAsync(params string[] statements)
    {
        // CREATE DATABASE cannot run inside a transaction or against the database being created,
        // so it goes through the server's own maintenance database.
        await using var connection = new NpgsqlConnection(ConnectionStringFor("postgres"));
        await connection.OpenAsync();

        foreach (var statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task<Dictionary<string, string>> IndexDefinitionsAsync()
    {
        var rows = await QueryAsync(
            _postgres.ConnectionString,
            "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'public';",
            reader => (Name: reader.GetString(0), Definition: reader.GetString(1)));

        return rows.ToDictionary(row => row.Name, row => row.Definition, StringComparer.Ordinal);
    }

    private static async Task<List<T>> QueryAsync<T>(
        string connectionString,
        string sql,
        Func<NpgsqlDataReader, T> read)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
        }

        return rows;
    }
}

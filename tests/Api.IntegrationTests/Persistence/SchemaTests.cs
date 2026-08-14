using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application;
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
                // Only so the context can be resolved: since step 31 it needs somewhere to
                // publish domain events, and migrating raises none.
                .AddApplication()
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
            Assert.Contains("sync_ops", tables);
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

    /// <summary>
    /// Every table carries the change stamp a sync cursor is a position in, and every one of them
    /// has the trigger that maintains it.
    /// </summary>
    /// <remarks>
    /// The two halves are asserted against each other because they come from different places and
    /// only one of them is automatic: the column is swept onto the model, the trigger is written
    /// by hand in a migration. A table added later gets the column for free and the trigger only
    /// if somebody remembers — and the symptom of forgetting is a phone that never hears about an
    /// edit, which nothing else in the suite would notice.
    /// </remarks>
    [Fact]
    public async Task StampsEveryTableWithTheTransactionThatLastWroteIt()
    {
        // The tables this application owns: not EF's own bookkeeping, and not the two PostGIS
        // brought with it when the migration enabled the extension.
        var tables = await QueryAsync(
            _postgres.ConnectionString,
            """
            SELECT tablename FROM pg_tables t
            WHERE schemaname = 'public'
              AND tablename <> '__EFMigrationsHistory'
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend
                  JOIN pg_class ON pg_class.oid = pg_depend.objid
                  JOIN pg_namespace ON pg_namespace.oid = pg_class.relnamespace
                  WHERE pg_class.relname = t.tablename
                    AND pg_namespace.nspname = t.schemaname
                    AND pg_depend.deptype = 'e');
            """,
            reader => reader.GetString(0));

        var stamped = await QueryAsync(
            _postgres.ConnectionString,
            """
            SELECT table_name FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name = 'change_seq';
            """,
            reader => reader.GetString(0));

        var triggered = await QueryAsync(
            _postgres.ConnectionString,
            """
            SELECT relname FROM pg_trigger
            JOIN pg_class ON pg_class.oid = pg_trigger.tgrelid
            WHERE tgname = 'stamp_change_seq' AND NOT tgisinternal;
            """,
            reader => reader.GetString(0));

        Assert.NotEmpty(tables);
        Assert.Equal(tables.Order(), stamped.Order());
        Assert.Equal(tables.Order(), triggered.Order());
    }

    /// <summary>
    /// The indexes the two read paths that run constantly depend on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-written in a migration, like the board's composite index above and for a related reason
    /// — <c>change_seq</c> is a shadow property added after the configurations run, so no
    /// configuration can name it. Outside the model means nothing else would notice one going
    /// missing, and what going missing looks like is not an error: it is a pull that gets slower
    /// every month until a shop with real history times out.
    /// </para>
    /// <para>
    /// The columns are asserted, not just the names, because an index that exists over the wrong
    /// columns answers no query and passes a name check.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("ix_assignments_technician_id_change_seq", "(technician_id, change_seq)")]
    [InlineData("ix_assignments_change_seq", "(change_seq)")]
    [InlineData("ix_jobs_change_seq", "(change_seq)")]
    [InlineData("ix_assignments_org_id_scheduled_start", "(org_id, scheduled_start)")]
    [InlineData("ix_sync_ops_applied_at", "(applied_at)")]
    public async Task IndexesWhatTheSyncAndBoardPathsReadBy(string index, string columns)
    {
        var definitions = await IndexDefinitionsAsync();

        Assert.Contains(index, definitions.Keys);
        Assert.Contains(columns, definitions[index], StringComparison.Ordinal);
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

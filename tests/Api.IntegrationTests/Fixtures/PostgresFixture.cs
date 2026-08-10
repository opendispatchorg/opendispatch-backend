using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// One migrated PostGIS container for the whole integration suite. Starting a container costs
/// seconds, so it is started once per run and shared by every test in
/// <see cref="PostgresCollectionDefinition"/> rather than per test class.
/// </summary>
/// <remarks>
/// The schema is built by applying the real migrations to an empty database, which is the same
/// thing <c>make migrate</c> does to the compose database — so the schema under test is the
/// schema that ships, and a migration that does not apply from scratch fails the whole suite
/// rather than one test.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same multi-arch PostGIS image as docker-compose, so the schema under test and the
    // schema you develop against cannot drift.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("imresamu/postgis:17-3.6")
        .WithDatabase("opendispatch")
        .WithUsername("opendispatch")
        .WithPassword("opendispatch")
        .Build();

    private ServiceProvider? _services;
    private IServiceScope? _scope;
    private DbContextOptions<AppDbContext>? _options;

    /// <summary>Connection string for the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Built through the real registrations, so the provider and plugins under test are the
        // ones the host runs with. AddApplication is here because since step 31 the persistence
        // layer publishes domain events on save, and publishing needs the mediator that
        // registration supplies.
        _services = TestHost.Over(this).BuildServiceProvider();
        _scope = _services.CreateScope();
        _options = _scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();

        // Enables the PostGIS extension too — that is part of the migration, not a favour the
        // test harness does for it. No tenant: migrating queries no entity, so no filter runs.
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Database.MigrateAsync();
    }

    /// <summary>
    /// A service scope acting as one organization, over the same registration the host uses.
    /// </summary>
    /// <remarks>
    /// One scope is one unit of work and one tenant: every repository resolved from it shares a
    /// context, which is the arrangement a handler gets and the reason a save can commit two
    /// aggregates together. Resolving from separate scopes would test something the application
    /// never does.
    /// </remarks>
    public IServiceScope ActingAs(OrgId tenant) =>
        (_services ?? throw new InvalidOperationException("The fixture has not been initialised."))
            .ActingAs(tenant);

    /// <summary>A context scoped to one organization. The caller disposes it.</summary>
    public AppDbContext NewContext(OrgId tenant)
    {
        var context = new AppDbContext(
            _options ?? throw new InvalidOperationException("The fixture has not been initialised."),
            Acting(tenant));

        return context;
    }

    private static TestTenantContext Acting(OrgId tenant)
    {
        var acting = new TestTenantContext();
        acting.ActAs(tenant);

        return acting;
    }

    /// <summary>Opens a connection to the shared container's database.</summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task DisposeAsync()
    {
        _scope?.Dispose();

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

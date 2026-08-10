using Microsoft.EntityFrameworkCore;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Organizations;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Infrastructure.Persistence.Conversions;

namespace OpenDispatch.Infrastructure.Persistence;

/// <summary>
/// The unit of work over the OpenDispatch database.
/// </summary>
/// <remarks>
/// <para>
/// The domain entities are the persisted entities (Document 2 §6): there is no second set of
/// row-shaped classes and no mapping chain between them. What makes that safe is configuration
/// rather than compromise in the domain — value objects as owned types, typed ids through value
/// converters, backing fields for the private collections, <c>Version</c> as the concurrency
/// token. None of it leaks back the other way, so the domain still references nothing.
/// </para>
/// <para>
/// There is a <see cref="DbSet{TEntity}"/> per aggregate root and nothing else. Owned children —
/// a customer's service locations, an invoice's line items — are reached through their root, and
/// aggregates reference each other by id, so a set for them would be an invitation to load one
/// outside the boundary that keeps it consistent.
/// </para>
/// </remarks>
public sealed class AppDbContext : DbContext
{
    /// <summary>Creates the context. Options carry the provider, connection string and interceptors.</summary>
    /// <param name="options">Provider and behaviour configuration, supplied by the composition root.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    /// <summary>The demand: what customers need doing.</summary>
    public DbSet<Job> Jobs => Set<Job>();

    /// <summary>The plan: which technician goes where, when, and in what order.</summary>
    public DbSet<Assignment> Assignments => Set<Assignment>();

    /// <summary>The crew.</summary>
    public DbSet<Technician> Technicians => Set<Technician>();

    /// <summary>Customers, each owning the locations work happens at.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>Invoices, each owning its line items.</summary>
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <summary>The tenants. Every other row in the database is scoped by one of these.</summary>
    public DbSet<Organization> Organizations => Set<Organization>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every strongly-typed id in the system, and the fact that each is a uuid. Registered
        // once for the whole model rather than per property, so a JobId column is a JobId column
        // wherever one appears and no per-aggregate configuration has to name a converter.
        configurationBuilder.Properties<AssignmentId>().HaveConversion<AssignmentIdConverter>();
        configurationBuilder.Properties<CustomerId>().HaveConversion<CustomerIdConverter>();
        configurationBuilder.Properties<InvoiceId>().HaveConversion<InvoiceIdConverter>();
        configurationBuilder.Properties<JobId>().HaveConversion<JobIdConverter>();
        configurationBuilder.Properties<LineItemId>().HaveConversion<LineItemIdConverter>();
        configurationBuilder.Properties<OrgId>().HaveConversion<OrgIdConverter>();
        configurationBuilder.Properties<ServiceLocationId>().HaveConversion<ServiceLocationIdConverter>();
        configurationBuilder.Properties<TechnicianId>().HaveConversion<TechnicianIdConverter>();

        // The value objects. Document 2 §6 calls these owned types, which is what EF called value
        // objects when it was written; EF's answer for them now is complex types, and —
        // decisively — an owned type is an entity type, which a struct cannot be. All of ours are
        // readonly record structs.
        //
        // Two of the three collapse to a single column, so they are conversions: Money is its
        // cents, and a GeoPoint is one geography(Point) — splitting it into two doubles would
        // round-trip perfectly well and be unusable by PostGIS. The third, TimeWindow, is
        // genuinely two instants and cannot be registered here at all; see TimeWindowMapping.
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>();
        configurationBuilder.Properties<GeoPoint>()
            .HaveConversion<GeoPointConverter>()
            .HaveColumnType(GeoPointConverter.ColumnType);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configuration lives one class per aggregate rather than in a single growing method
        // here, and is discovered rather than listed: a new aggregate arrives with its own
        // IEntityTypeConfiguration and this file does not change.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Last, so it reaches every root in the model however it got there.
        AggregateRootConventions.Apply(modelBuilder);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Advances <see cref="AggregateRoot.Version"/> on every root this transaction changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The concurrency token has to move for the check to mean anything, and the aggregate is
    /// the wrong place to move it from: a version is a fact about a stored row, not about the
    /// business, and having each intent method remember to bump it is the same
    /// everyone-must-remember problem the domain exists to remove.
    /// </para>
    /// <para>
    /// EF has already captured the value the row was read at, so the bump changes what is
    /// written without touching what is compared: <c>SET version = @new WHERE ... AND version =
    /// @original</c>. A root inserted this transaction keeps the version it was constructed
    /// with.
    /// </para>
    /// </remarks>
    private void StampVersions()
    {
        foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
        {
            if (entry.State is EntityState.Modified)
            {
                entry.Entity.BumpVersion();
            }
        }
    }
}

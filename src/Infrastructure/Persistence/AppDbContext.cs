using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auditing;
using OpenDispatch.Application.Auth;
using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Organizations;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Infrastructure.Events;
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
/// There is a <see cref="DbSet{TEntity}"/> per aggregate root, and one that is not: owned
/// children — a customer's service locations, an invoice's line items — are reached through
/// their root, and aggregates reference each other by id, so a set for them would be an
/// invitation to load one outside the boundary that keeps it consistent. The sync op log is
/// neither a root nor a child of one. It is the protocol's own record of what devices have
/// already done, and the only way to reach it is a set of its own.
/// </para>
/// </remarks>
public sealed class AppDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    /// <summary>Creates the context. Options carry the provider, connection string and interceptors.</summary>
    /// <param name="options">Provider and behaviour configuration, supplied by the composition root.</param>
    /// <param name="tenant">Whose data this context may see.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
        : base(options) => _tenant = tenant;

    /// <summary>
    /// The organization every query through this context is scoped to.
    /// </summary>
    /// <remarks>
    /// Public because the tenant filters are expressions built against it, and an expression EF
    /// compiles cannot reach a private member. Reading it before anything has resolved a tenant
    /// throws, which is why <c>CanConnect</c> and the migration path — neither of which queries an
    /// entity — work without one.
    /// </remarks>
    public OrgId CurrentOrgId => _tenant.OrgId;

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

    /// <summary>
    /// What technicians' devices have already done. Written once per applied operation and never
    /// edited; it exists so that pushing the same operation twice changes nothing the second time.
    /// </summary>
    public DbSet<SyncOpRecord> SyncOps => Set<SyncOpRecord>();

    /// <summary>
    /// What technicians captured in the field, minus the bytes: those live behind
    /// <c>IAttachmentStorage</c>, because a row is not where a photograph belongs.
    /// </summary>
    public DbSet<Attachment> Attachments => Set<Attachment>();

    /// <summary>
    /// Notes saying a stop is gone. The one thing pull cannot read off a row, because the row is
    /// what went.
    /// </summary>
    public DbSet<SyncRemoval> SyncRemovals => Set<SyncRemoval>();

    /// <summary>
    /// Who did what: one entry per command that changed anything (see <c>AuditBehavior</c>).
    /// </summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>
    /// Domain events waiting to be delivered, written in the transaction that raised them.
    /// </summary>
    /// <remarks>
    /// Not a business record and not tenant-scoped: it is how a reaction survives a process that
    /// dies between committing work and announcing it. See <c>OutboxMessage</c>.
    /// </remarks>
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    /// <summary>
    /// Who may sign in. Not an aggregate and not a tenant-owned business record — a login is how a
    /// caller <em>acquires</em> a tenant — which is why the store that reads it ignores the query
    /// filters this context applies to everything else carrying an <c>OrgId</c>.
    /// </summary>
    public DbSet<AuthUser> Users => Set<AuthUser>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every strongly-typed id in the system, and the fact that each is a uuid. Registered
        // once for the whole model rather than per property, so a JobId column is a JobId column
        // wherever one appears and no per-aggregate configuration has to name a converter.
        configurationBuilder.Properties<AssignmentId>().HaveConversion<AssignmentIdConverter>();
        configurationBuilder.Properties<AttachmentId>().HaveConversion<AttachmentIdConverter>();
        configurationBuilder.Properties<CustomerId>().HaveConversion<CustomerIdConverter>();
        configurationBuilder.Properties<InvoiceId>().HaveConversion<InvoiceIdConverter>();
        configurationBuilder.Properties<JobId>().HaveConversion<JobIdConverter>();
        configurationBuilder.Properties<JobLineId>().HaveConversion<JobLineIdConverter>();
        configurationBuilder.Properties<LineItemId>().HaveConversion<LineItemIdConverter>();
        configurationBuilder.Properties<OrgId>().HaveConversion<OrgIdConverter>();
        configurationBuilder.Properties<ServiceLocationId>().HaveConversion<ServiceLocationIdConverter>();
        configurationBuilder.Properties<SyncOpId>().HaveConversion<SyncOpIdConverter>();
        configurationBuilder.Properties<TechnicianId>().HaveConversion<TechnicianIdConverter>();
        configurationBuilder.Properties<UserId>().HaveConversion<UserIdConverter>();

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
        configurationBuilder.Properties<StorageKey>().HaveConversion<StorageKeyConverter>();
        configurationBuilder.Properties<GeoPoint>()
            .HaveConversion<GeoPointConverter>()
            .HaveColumnType(GeoPointConverter.ColumnType);

        // Every instant, wherever it appears — a window's two ends, a stop's start, an invoice's
        // issue date — goes to its column in UTC. Npgsql refuses any other offset against
        // timestamptz, and until this line six handlers each remembered to convert before building
        // a value object. One rule on the model is one rule; six is a rule somebody eventually
        // forgets, and the next one to write is a sync op arriving from a phone.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcInstantConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Declared on the model so the migration creates it, rather than left as something a
        // database has to have had done to it beforehand. Every geography column depends on it.
        modelBuilder.HasPostgresExtension("postgis");

        // Configuration lives one class per aggregate rather than in a single growing method
        // here, and is discovered rather than listed: a new aggregate arrives with its own
        // IEntityTypeConfiguration and this file does not change.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Last, so they reach every type in the model however it got there — including the ones
        // whose configurations have not been written yet.
        AggregateRootConventions.Apply(modelBuilder);
        ChangeStamps.Apply(modelBuilder);
        TenantQueryFilters.Apply(modelBuilder, this);
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
    /// <para>
    /// A change to something a root <em>owns</em> counts as a change to the root, which EF does
    /// not say by itself: appending a line to a job leaves the job's own entry <c>Unchanged</c>
    /// until the update is built, so a version that only followed the entry would sit still while
    /// the row underneath it moved. That matters most to the thing furthest from here — a
    /// technician's phone, which is told what to base its next operation on by reading a version.
    /// </para>
    /// </remarks>
    private void StampVersions()
    {
        foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
        {
            var changed = entry.State switch
            {
                EntityState.Modified => true,

                // Added and Deleted are deliberately not here: a root created in this transaction
                // keeps the version it was constructed with, and one being removed has nothing
                // left to stamp.
                EntityState.Unchanged => OwnsSomethingChanged(entry),
                _ => false,
            };

            if (changed)
            {
                entry.Entity.BumpVersion();
            }
        }
    }

    /// <summary>
    /// Whether anything this root owns — a service location, an invoice line, a line recorded in
    /// the field — has been added, edited, or removed in this transaction.
    /// </summary>
    /// <remarks>
    /// Walked from the root rather than from the tracked children, because a child does not know
    /// its owner: EF gives a dependent no navigation back, and finding one by foreign key would be
    /// a key comparison written by hand. Added and edited children are visible in the live
    /// collection (<see cref="Owned"/>); a <em>removed</em> one is not — it has already left — so
    /// <see cref="LostAChild"/> looks for it the other way, by shadow foreign key, among the
    /// tracker's <see cref="EntityState.Deleted"/> entries. Step 47 is what first removes an owned
    /// child (<c>Customer.RemoveLocation</c>, reachable for real once it has an HTTP caller), which
    /// is why this half exists now rather than from the start.
    /// </remarks>
    private bool OwnsSomethingChanged(EntityEntry<AggregateRoot> root) =>
        root.Collections
            .Concat<NavigationEntry>(root.References)
            .Where(navigation => navigation.Metadata.TargetEntityType.IsOwned())
            .Any(navigation =>
                Owned(navigation).Any(child => Entry(child).State is not EntityState.Unchanged)
                || LostAChild(root, navigation));

    private static IEnumerable<object> Owned(NavigationEntry navigation) => navigation switch
    {
        CollectionEntry collection => collection.CurrentValue?.Cast<object>() ?? [],
        _ => navigation.CurrentValue is { } only ? [only] : [],
    };

    /// <summary>
    /// Whether this navigation lost a child this transaction: something of its target type,
    /// pending deletion, whose shadow foreign key still points at this root.
    /// </summary>
    /// <remarks>
    /// A deleted owned entity keeps its values — including the foreign key EF mapped its
    /// ownership through (<c>CustomerConfiguration</c>'s <c>WithOwner().HasForeignKey(...)</c>)
    /// — right up until the delete is written, so this reads it back rather than needing a
    /// navigation that does not exist.
    /// </remarks>
    private bool LostAChild(EntityEntry<AggregateRoot> root, NavigationEntry navigation)
    {
        if (navigation.Metadata is not INavigation { ForeignKey: var foreignKey })
        {
            return false;
        }

        return ChangeTracker.Entries()
            .Where(entry => entry.State == EntityState.Deleted && entry.Metadata == foreignKey.DeclaringEntityType)
            .Any(entry => foreignKey.Properties
                .Zip(foreignKey.PrincipalKey.Properties)
                .All(pair => Equals(
                    entry.Property(pair.First.Name).CurrentValue,
                    root.Property(pair.Second.Name).CurrentValue)));
    }
}

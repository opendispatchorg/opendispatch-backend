using Microsoft.EntityFrameworkCore;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Organizations;
using OpenDispatch.Domain.Technicians;

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
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configuration lives one class per aggregate rather than in a single growing method
        // here, and is discovered rather than listed: a new aggregate arrives with its own
        // IEntityTypeConfiguration and this file does not change.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        UnconfiguredAggregates.ExcludeUntilConfigured(modelBuilder);
    }
}

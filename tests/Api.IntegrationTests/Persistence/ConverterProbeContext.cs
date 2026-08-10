using Microsoft.EntityFrameworkCore;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.Infrastructure.Persistence.Conversions;

namespace OpenDispatch.Api.IntegrationTests.Persistence;

/// <summary>
/// <see cref="AppDbContext"/> plus one throwaway entity, so the conversions can be exercised
/// against a real database two steps before any aggregate is mapped.
/// </summary>
/// <remarks>
/// It derives rather than duplicating, which is the whole point: the conventions, the
/// aggregate-root rules and the version stamping under test are the ones the real context uses,
/// not a copy that could drift from them. Deriving is legal because EF only requires the options'
/// context type to be assignable from the context being built.
/// </remarks>
internal sealed class ConverterProbeContext : AppDbContext
{
    /// <summary>The probe's table. Named here because the test creates and drops it directly.</summary>
    internal const string TableName = "converter_probe";

    public ConverterProbeContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<ConverterProbe> Probes => Set<ConverterProbe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Before the base call, so AggregateRootConventions — which runs last there — sees the
        // probe and applies the same DomainEvents and Version rules it applies to a real root.
        modelBuilder.Entity<ConverterProbe>(probe =>
        {
            probe.ToTable(TableName);
            probe.HasKey(p => p.Id);
            probe.HasTimeWindow(p => p.Window);
        });

        base.OnModelCreating(modelBuilder);
    }
}

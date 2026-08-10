using Microsoft.EntityFrameworkCore;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// Builds the schema from the model, for the one step between the aggregates being mapped and
/// there being a migration to apply.
/// </summary>
/// <remarks>
/// Step 27 replaces this with <c>Database.Migrate()</c> in <see cref="PostgresFixture"/>, and the
/// difference matters: a script generated from the model always agrees with the model, so until
/// the migration exists these tests cannot catch a migration that has drifted from it. What they
/// do catch is a mapping that Postgres will not accept, which is what step 26 is about.
/// </remarks>
internal static class Schema
{
    /// <summary>Drops every table the model owns and recreates them.</summary>
    internal static async Task RecreateAsync(AppDbContext context)
    {
        var tables = context.Model
            .GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(table => table is not null)
            .Distinct()
            .Select(table => $"\"{table}\"");

        // CASCADE because the owned tables carry foreign keys back to their roots, and dropping
        // in dependency order is a list that would need maintaining every time one is added.
        //
        // EF1002 suppressed: the interpolated values are table names read out of the EF model,
        // not input. They also cannot be parameters — Postgres does not take an identifier as
        // one — so the choice is this or a hand-maintained list of literals.
        var drop = $"DROP TABLE IF EXISTS {string.Join(", ", tables)} CASCADE;";
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(drop);
        await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript());
#pragma warning restore EF1002
    }
}

using Microsoft.EntityFrameworkCore;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Api.Operations;

/// <summary>
/// <c>dotnet OpenDispatch.Api.dll migrate</c>: applies every migration this build carries to the
/// database it is configured for, then exits.
/// </summary>
/// <remarks>
/// <para>
/// The only way a deployment can apply the schema. <c>make migrate</c> runs <c>dotnet ef</c>, which
/// needs the SDK, the tool manifest and the source tree — none of which a published application or
/// a container image has. This verb needs the built app and a connection string, which is exactly
/// what a deployment has.
/// </para>
/// <para>
/// <strong>Deliberately not automatic on startup.</strong> Two replicas rolling out together would
/// both migrate at once, and the second one's failure would be a crash loop in something whose job
/// is to serve traffic. A schema change is a step in a deployment, run once, with somebody or
/// something watching the exit code — so it is a verb, and the container runs it as a one-shot
/// before the API comes up.
/// </para>
/// <para>
/// Allowed in every environment, unlike <c>SeedCommand</c>: a production database is the one this
/// exists for. It reports the migrations it applied, and says so plainly when there was nothing to
/// do, because "did that deploy actually change the schema?" is the question the log is read for.
/// </para>
/// </remarks>
internal static class MigrateCommand
{
    /// <summary>The argument that asks for a migration rather than a server.</summary>
    private const string Verb = "migrate";

    /// <summary>Whether this process was started to migrate rather than to serve.</summary>
    /// <param name="args">The host's command-line arguments.</param>
    public static bool Requested(string[] args) => Array.Exists(args, argument =>
        string.Equals(argument, Verb, StringComparison.Ordinal));

    /// <summary>Applies what is outstanding.</summary>
    /// <param name="app">The built host, for its services.</param>
    /// <returns>The process exit code: zero if the database is up to date, one if it could not be.</returns>
    public static async Task<int> RunAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();

        try
        {
            // Resolved inside the try, not above it, and that is a fix from a real run rather than
            // a preference: resolving the context is where a refused connection string surfaces
            // (the startup guards validate options on first use), and outside the try that
            // escaped to the host's own fatal handler — a correct exit code under the message
            // "OpenDispatch API terminated unexpectedly", which is the wrong thing to hand
            // somebody reading a failed deployment.
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

            var pending = (await database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();

            if (pending.Count == 0)
            {
                MigrateLog.NothingToDo(app.Logger);

                return 0;
            }

            // Materialized before the call rather than inside it: the analyzer will not have a
            // join evaluated at a log level that might be off, and this line is the record of what
            // a deployment actually changed.
            var names = string.Join(", ", pending);

            MigrateLog.Applying(app.Logger, pending.Count, names);

            await database.MigrateAsync().ConfigureAwait(false);

            MigrateLog.Applied(app.Logger, pending.Count);

            return 0;
        }
        catch (Exception failed)
        {
            // Every failure here is one a deployment must stop on — an unreachable database, a
            // migration that cannot apply, a permission the connection does not have — so this
            // reports and returns non-zero rather than letting the host's own fatal handler
            // describe it as a host that "terminated unexpectedly".
            MigrateLog.Failed(app.Logger, failed);

            return 1;
        }
    }
}

/// <summary>What the migrate verb has to say, source-generated.</summary>
internal static partial class MigrateLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "The database is up to date; nothing to apply.")]
    internal static partial void NothingToDo(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migration(s): {Migrations}.")]
    internal static partial void Applying(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} migration(s).")]
    internal static partial void Applied(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The database could not be migrated.")]
    internal static partial void Failed(ILogger logger, Exception exception);
}
